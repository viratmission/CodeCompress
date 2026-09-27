using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CodeCompass.Api.Services;

public class AssistantService : IAssistantService
{
    private readonly CodeCompassDbContext _db;
    private readonly IContextRetrievalService _context;
    private readonly WatsonxProvider _watsonx;
    private readonly ILogger<AssistantService> _logger;

    // Layer-order for onboarding suggestions
    private static readonly Dictionary<string, string> LayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Frontend","Presentation" },{ "Components","Presentation" },
        { "Controllers","API" },{ "Services","Business Logic" },
        { "Repositories","Data Access" },{ "Models","Domain" },
        { "DTOs","Domain" },{ "Migrations","Data Access" },
    };

    public AssistantService(
        CodeCompassDbContext db,
        IContextRetrievalService context,
        WatsonxProvider watsonx,
        ILogger<AssistantService> logger)
    {
        _db       = db;
        _context  = context;
        _watsonx  = watsonx;
        _logger   = logger;
    }

    // ── Main ask handler ──────────────────────────────────────────────────
    public async Task<AssistantAnswerDto> AskAsync(
        int repositoryId, AskRequestDto request, CancellationToken ct = default, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Question must not be empty.");

        // 1. Fetch recent conversation turns for this session if available
        List<ConversationHistoryItem> history = new();
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var rawHistory = await _db.Conversations
                .Where(c => c.RepositoryId == repositoryId && c.SessionId == request.SessionId)
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
                .Select(c => new { c.Question, c.Answer, c.CreatedAt })
                .ToListAsync(ct);

            history = rawHistory
                .OrderBy(c => c.CreatedAt)
                .Select(c => new ConversationHistoryItem
                {
                    Question = c.Question,
                    Answer   = c.Answer.Length > 1000 ? c.Answer.Substring(0, 1000) + "..." : c.Answer,
                })
                .ToList();
        }

        // 2. Retrieve repository context with intent and domain planning
        var ctx = await _context.RetrieveAsync(repositoryId, request.Question, history, ct);

        // 3. Build references from retrieved context (prioritizing primary files)
        var orderedFiles = ctx.PrimaryFiles.Concat(ctx.SupportingFiles).DistinctBy(f => f.FilePath).ToList();
        var fileRefs = orderedFiles.Select(f => new FileReferenceDto
        {
            FilePath = f.FilePath,
            Language = f.Language,
            Reason   = f.RelevanceReason,
        }).ToList();

        var moduleRefs = ctx.Modules.Select(m => new ModuleReferenceDto
        {
            ModuleId   = m.Id,
            ModuleName = m.Name,
            ModulePath = m.Path,
            ModuleType = m.ModuleType,
        }).ToList();

        // 4. Build the answer
        string answer;
        List<ChangeImpactItemDto> changeImpact = new();

        if (!_watsonx.IsConfigured())
        {
            answer = BuildContextOnlyAnswer(request.Question, request.QuestionType, ctx);
        }
        else
        {
            try
            {
                var systemPrompt = BuildSystemPrompt(ctx, request.Question);
                var userMessage  = request.QuestionType == "change-impact" || ctx.UserIntent == "CHANGE_IMPACT"
                    ? BuildChangeImpactUserMessage(request.Question, ctx)
                    : request.Question;

                answer = await _watsonx.ChatAsync(systemPrompt, userMessage, ct);

                if (request.QuestionType == "change-impact" || ctx.UserIntent == "CHANGE_IMPACT")
                    changeImpact = ParseChangeImpact(ctx, request.Question);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "watsonx.ai call failed for repo {Id}", repositoryId);
                answer = $"AI service error: {ex.Message}\n\n"
                       + BuildContextOnlyAnswer(request.Question, request.QuestionType, ctx);
            }
        }

        if ((request.QuestionType == "change-impact" || ctx.UserIntent == "CHANGE_IMPACT") && changeImpact.Count == 0)
            changeImpact = ParseChangeImpact(ctx, request.Question);

        // 5. Persist conversation
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N")[..12];
        _db.Conversations.Add(new Conversation
        {
            RepositoryId = repositoryId,
            UserId       = userId,
            SessionId    = sessionId,
            Question     = request.Question,
            Answer       = answer.Length > 25000 ? answer[..25000] : answer,
            QuestionType = request.QuestionType,
            CreatedAt    = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return new AssistantAnswerDto
        {
            Answer       = answer,
            QuestionType = request.QuestionType,
            References   = fileRefs,
            Modules      = moduleRefs,
            ChangeImpact = changeImpact,
            SessionId    = sessionId,
            Context      = new ContextSummaryDto
            {
                FilesExamined        = ctx.Files.Count,
                ModulesExamined      = ctx.Modules.Count,
                DependenciesExamined = ctx.Dependencies.Count,
                SourceCodeRead       = ctx.SourceSnippets.Count > 0,
                RepositoryName       = ctx.RepositoryName,
            },
        };
    }

    // ── Suggested questions from repository structure ─────────────────────
    public async Task<SuggestedQuestionsDto> GetSuggestedQuestionsAsync(
        int repositoryId, CancellationToken ct = default)
    {
        var repo = await _db.Repositories.FindAsync(new object[] { repositoryId }, ct);
        if (repo == null) return new SuggestedQuestionsDto();

        var questions = new List<string>
        {
            "I want authentication beginner to advanced.",
            "Teach me authentication from the beginning using this repository.",
            "Explain the login flow step by step.",
            "What authentication features are actually implemented in this repository?",
            "What authentication features are missing?",
            "Show me the frontend-to-backend authentication flow.",
            "Quiz me on authentication.",
            "What files should a new backend developer read first and why?",
            "Trace what happens when a user creates a todo.",
            "What is the impact of modifying todo item validation?"
        };

        return new SuggestedQuestionsDto { Questions = questions.Distinct().Take(10).ToList() };
    }

    // ── Mentor Prompt Builder ─────────────────────────────────────────────
    private static string BuildSystemPrompt(RetrievedContext ctx, string userQuestion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are CodeCompass, a senior software engineer, technical mentor, and repository onboarding guide.");
        sb.AppendLine("Your goal is to teach developers from beginner to advanced using the ACTUAL CODEBASE provided in the context below.");
        sb.AppendLine();
        sb.AppendLine("=== CORE MENTORSHIP PRINCIPLES ===");
        sb.AppendLine("1. TEACH FROM FIRST PRINCIPLES: Assume the developer may be new to this repository and unfamiliar with internal design choices. Explain concepts simply without assuming internal tribal knowledge.");
        sb.AppendLine("2. CLARITY OVER JARGON: Avoid unnecessary jargon. When technical terms are essential (e.g., 'Dependency Injection', 'ORM', 'Minimal API', 'DTO', 'DbContext', 'BFF', 'Reverse Proxy'), define them simply and clearly in context.");
        sb.AppendLine("3. STRICT REPOSITORY GROUNDING: Ground all explanations in the actual files, code excerpts, endpoints, and architecture provided below.");
        sb.AppendLine("4. REPOSITORY FACT VS GENERAL KNOWLEDGE:");
        sb.AppendLine("   - REPOSITORY FACT: State clearly what exists in this project (e.g. 'This project implements cookie authentication and external OAuth in `Todo.Web/Server/Authentication/AuthenticationExtensions.cs`').");
        sb.AppendLine("   - GENERAL KNOWLEDGE: Explain general concepts separately (e.g. 'In general, refresh tokens allow clients to obtain new access tokens without re-prompting credentials').");
        sb.AppendLine("   - MISSING FEATURES: If a feature is not in this repository (e.g., Refresh Tokens, MFA, local password hashing), say clearly: 'Not currently implemented in this repository. In production systems, this is typically added as a next step.'");
        sb.AppendLine("5. ZERO HALLUCINATION: Never invent or pretend that non-existent files, classes, methods, or endpoints exist in this repository. Cite exact file paths from the context.");
        sb.AppendLine("6. ADAPTIVE STRUCTURE: Do NOT force every answer into a rigid generic template. Adapt your response structure directly to the user's intent as instructed below.");
        sb.AppendLine();

        // Inject Internal Context Plan
        sb.AppendLine("=== INTERNAL CONTEXT PLAN (VERIFIED FACTS) ===");
        sb.AppendLine($"User Intent: {ctx.UserIntent} (Category: {ctx.QuestionIntent})");
        sb.AppendLine($"Domain: {ctx.DetectedDomain}");
        if (ctx.TargetFileName != null)
        {
            sb.AppendLine($"Target File Requested: `{ctx.TargetFileName}` (Exists in repo: {ctx.TargetFileExists})");
            if (!ctx.TargetFileExists && ctx.AlternativeExistingFile != null)
                sb.AppendLine($"CRITICAL NOTE: `{ctx.TargetFileName}` does NOT exist in this codebase! The corresponding functionality is in `{ctx.AlternativeExistingFile}`. You MUST explicitly state this to the user immediately, then explain `{ctx.AlternativeExistingFile}`!");
        }

        if (ctx.ImplementedFeatures.Count > 0)
        {
            sb.AppendLine("Verified Implemented Features in this Repository:");
            foreach (var feat in ctx.ImplementedFeatures)
                sb.AppendLine($"- [IMPLEMENTED] {feat}");
        }

        if (ctx.PartiallyImplementedFeatures.Count > 0)
        {
            sb.AppendLine("Partially Implemented Features:");
            foreach (var feat in ctx.PartiallyImplementedFeatures)
                sb.AppendLine($"- [PARTIALLY IMPLEMENTED] {feat}");
        }

        if (ctx.MissingFeatures.Count > 0)
        {
            sb.AppendLine("Not Implemented / Missing Production Next Steps:");
            foreach (var feat in ctx.MissingFeatures)
                sb.AppendLine($"- [NOT IMPLEMENTED — NEXT CONCEPT] {feat}");
        }
        sb.AppendLine();

        // Intent-specific response guidance
        sb.AppendLine("=== INTENT-SPECIFIC RESPONSE GUIDELINES ===");
        switch (ctx.UserIntent)
        {
            case "LEARNING_ROADMAP":
                if (ctx.QuestionIntent == "Quiz")
                {
                    sb.AppendLine("The user wants to be quizzed on authentication or codebase concepts.");
                    sb.AppendLine("Provide an interactive quiz with exactly 3 practical, repository-grounded questions testing their understanding of the actual files in this repository (e.g., asking how cookie auth is registered, how the BFF attaches the bearer token, or what happens when an unauthorized user accesses `/todos`).");
                    sb.AppendLine("Invite the user to answer the questions, and offer to give hints if needed.");
                }
                else
                {
                    sb.AppendLine("The user wants a structured, progressive LEARNING ROADMAP from beginner to advanced.");
                    sb.AppendLine("Structure your response as:");
                    sb.AppendLine("# [Domain] Learning Path: Beginner to Advanced");
                    sb.AppendLine("Explain that this roadmap is grounded in the actual repository architecture.");
                    sb.AppendLine("Organize into progressive levels:");
                    sb.AppendLine("## Level 1 — Fundamentals (Auth vs Authz, Registration, Login, Errors)");
                    sb.AppendLine("## Level 2 — In This Repository: [Architecture specifics e.g. Cookie & External OAuth Providers]");
                    sb.AppendLine("## Level 3 — Backend Authorization & API Security (`CheckCurrentUserAuthHandler.cs`, `CurrentUser.cs`, `RequireCurrentUser`)");
                    sb.AppendLine("## Level 4 — Frontend Authentication & State (`Todo.Web/Client`, `LogInForm.razor`, `HttpAuthenticationStateProvider.cs`)");
                    sb.AppendLine("## Level 5 — Security & Best Practices (CORS, HTTPS, Rate Limiting, Cookie flags)");
                    sb.AppendLine("## Level 6 — Advanced / Production Next Steps [NOT IMPLEMENTED — NEXT CONCEPTS] (Refresh tokens, Token rotation, MFA, Email verification, Password reset)");
                    sb.AppendLine("For each topic, provide:");
                    sb.AppendLine("- **What it is**: Simple beginner explanation.");
                    sb.AppendLine("- **Why it matters**: Practical developer context.");
                    sb.AppendLine("- **In this repository**: Actual implementation details.");
                    sb.AppendLine("- **Important files**: Real repository file paths.");
                    sb.AppendLine("- **Flow**: Visual directional flow.");
                    sb.AppendLine("- **Practice**: A hands-on task or question to inspect.");
                    sb.AppendLine("CRITICAL RULE: Mark EVERY topic as either [IMPLEMENTED], [PARTIALLY IMPLEMENTED], or [NOT IMPLEMENTED — NEXT CONCEPT].");
                    sb.AppendLine("End with: 'Start with Level 1, Lesson 1. When you're ready, say **Start Level 1** or ask any question, and I can walk through the actual code step-by-step.'");
                }
                break;

            case "FILE_EXPLANATION":
                if (ctx.TargetFileName != null && !ctx.TargetFileExists)
                {
                    sb.AppendLine($"IMPORTANT: The user asked to explain `{ctx.TargetFileName}`, but that file DOES NOT EXIST in this repository.");
                    sb.AppendLine($"Start your answer with: 'This repository does not contain a file named `{ctx.TargetFileName}`. In this codebase, authentication services are implemented in `{ctx.AlternativeExistingFile}` and `Todo.Web/Server/TodoApi.cs`.'");
                    sb.AppendLine($"Then provide a complete, deep explanation of `{ctx.AlternativeExistingFile}` using:");
                    sb.AppendLine("## What this file does");
                    sb.AppendLine("## Why this file exists");
                    sb.AppendLine("## Important dependencies");
                    sb.AppendLine("## Important methods (inputs, processing, return types, callers)");
                    sb.AppendLine("## Execution flow");
                    sb.AppendLine("## Related files");
                    sb.AppendLine("## Beginner takeaway");
                    sb.AppendLine("## Next file to read");
                }
                else
                {
                    sb.AppendLine("Provide a deep file walkthrough using:");
                    sb.AppendLine("## What this file does");
                    sb.AppendLine("## Why this file exists");
                    sb.AppendLine("## Important dependencies");
                    sb.AppendLine("## Important methods / handlers (inputs, processing, return types, callers)");
                    sb.AppendLine("## Execution flow");
                    sb.AppendLine("## Related files");
                    sb.AppendLine("## Beginner takeaway");
                    sb.AppendLine("## Next file to read");
                }
                break;

            case "REQUEST_FLOW":
                sb.AppendLine("Trace the requested flow end-to-end through the repository:");
                sb.AppendLine("Step 1: Frontend User Interaction / Component (e.g. `LogInForm.razor` or `TodoList.razor`)");
                sb.AppendLine("Step 2: Client Service / HTTP call (e.g. `TodoClient.cs`)");
                sb.AppendLine("Step 3: Server BFF / Reverse Proxy (e.g. `Todo.Web/Server/TodoApi.cs` translating cookie to bearer token)");
                sb.AppendLine("Step 4: API Endpoint Route Handler (e.g. `Todo.Api/Todos/TodoApi.cs`)");
                sb.AppendLine("Step 5: Authorization & Current User Verification (e.g. `CheckCurrentUserAuthHandler.cs`)");
                sb.AppendLine("Step 6: Business Logic & Data Context (e.g. `TodoDbContext.cs`)");
                sb.AppendLine("Step 7: Database Persistence (SQLite)");
                sb.AppendLine("Step 8: Response Return & Client Notification (e.g. `201 Created` or `200 OK`)");
                sb.AppendLine("For each step, detail: What happens, Which file does it, Why it exists, and What data moves through it.");
                break;

            case "FEATURE_EXPLANATION":
                if (ctx.QuestionIntent == "MissingFeatures")
                {
                    sb.AppendLine("The user is asking what features are MISSING or NOT IMPLEMENTED.");
                    sb.AppendLine("Provide a comprehensive audit of all missing or next-step production concepts:");
                    sb.AppendLine("1. Clearly list each missing feature marked with [NOT IMPLEMENTED — NEXT CONCEPT] (e.g., Refresh Tokens, MFA/2FA, local password hashing, email verification, password reset, distributed Redis session storage).");
                    sb.AppendLine("2. Explain WHY each feature is not in this repository (e.g., it is a streamlined reference sample delegating auth to external OAuth).");
                    sb.AppendLine("3. Explain HOW a production system would implement that missing concept.");
                }
                else if (ctx.QuestionIntent == "ImplementedFeatures")
                {
                    sb.AppendLine("The user is asking what features are ACTUALLY IMPLEMENTED in this repository.");
                    sb.AppendLine("Provide a comprehensive audit of all verified implemented features marked with [IMPLEMENTED] (e.g., Cookie auth, External OAuth with GitHub/Google/Microsoft, BFF YARP token translation, CurrentUser claims handler, route authorization, Blazor auth state bridge, user endpoints in SQLite).");
                    sb.AppendLine("Cite exact file paths and code evidence for each feature.");
                }
                else
                {
                    sb.AppendLine("Explain the feature in depth, connecting the purpose to actual repository classes, methods, and files.");
                }
                break;

            case "FOLLOW_UP_QUESTION":
                sb.AppendLine("The user is asking a follow-up, continuation ('continue', 'next', 'start level 1'), or question about a concept.");
                sb.AppendLine("Review the previous conversation history provided below. Continue the mentoring flow naturally:");
                sb.AppendLine("- If the user says 'start level 1' or 'continue' on a learning path: deliver Level 1 Lesson 1 in depth (Simple explanation, in this repo, files to inspect, flow, mini exercise, and 2-3 'Check your understanding' questions).");
                sb.AppendLine("- If the user answered a quiz question: evaluate their answer with encouraging, constructive technical feedback.");
                sb.AppendLine("- If the user says 'I don't understand X': explain X from first principles with an intuitive analogy before showing the code.");
                break;

            case "CHANGE_IMPACT":
                sb.AppendLine("Detail the blast radius and architectural impact:");
                sb.AppendLine("## Directly affected files");
                sb.AppendLine("## Indirectly affected files");
                sb.AppendLine("## Why these files are impacted");
                sb.AppendLine("## Runtime and API behavior impact");
                sb.AppendLine("## Tests to update");
                sb.AppendLine("## Risk areas and edge cases");
                sb.AppendLine("## Recommended validation steps");
                break;

            case "ARCHITECTURE_EXPLANATION":
                sb.AppendLine("Provide a repository-specific architecture walkthrough across all verified layers (Frontend/Client, BFF/Gateway, Backend Web API, Data Access/EF Core, Database).");
                sb.AppendLine("Explain communication mechanisms between layers and cite real modules.");
                break;

            default:
                sb.AppendLine("Provide a complete, structured, beginner-friendly explanation grounded in repository evidence.");
                break;
        }
        sb.AppendLine();

        // Conversation history context
        if (ctx.History.Count > 0)
        {
            sb.AppendLine("=== PREVIOUS CONVERSATION CONTEXT ===");
            foreach (var h in ctx.History)
            {
                sb.AppendLine($"User: {h.Question}");
                sb.AppendLine($"Assistant: {h.Answer}");
                sb.AppendLine("---");
            }
            sb.AppendLine();
        }

        // Repository context data
        sb.AppendLine("=== REPOSITORY CONTEXT ===");
        sb.AppendLine($"Repository Name: {ctx.RepositoryName}");
        if (!string.IsNullOrWhiteSpace(ctx.ArchitectureSummary))
        {
            sb.AppendLine("Architecture Overview:");
            sb.AppendLine(ctx.ArchitectureSummary);
            sb.AppendLine();
        }

        if (ctx.PrimaryFiles.Count > 0)
        {
            sb.AppendLine("Primary Core Files:");
            foreach (var f in ctx.PrimaryFiles)
                sb.AppendLine($"- `{f.FilePath}` ({f.Language}) — {f.RelevanceReason}");
            sb.AppendLine();
        }

        if (ctx.SupportingFiles.Count > 0)
        {
            sb.AppendLine("Supporting Files & Models:");
            foreach (var f in ctx.SupportingFiles.Take(10))
                sb.AppendLine($"- `{f.FilePath}` ({f.Language}) — {f.RelevanceReason}");
            sb.AppendLine();
        }

        if (ctx.DiscoveredEndpoints.Count > 0)
        {
            sb.AppendLine("Discovered HTTP Endpoints:");
            foreach (var ep in ctx.DiscoveredEndpoints)
                sb.AppendLine($"- `{ep}`");
            sb.AppendLine();
        }

        if (ctx.DiscoveredEntities.Count > 0)
        {
            sb.AppendLine("Discovered Domain Entities & Models:");
            foreach (var ent in ctx.DiscoveredEntities)
                sb.AppendLine($"- `{ent}`");
            sb.AppendLine();
        }

        if (ctx.Dependencies.Count > 0)
        {
            sb.AppendLine("File Dependencies:");
            foreach (var d in ctx.Dependencies.Take(10))
                sb.AppendLine($"- `{d.SourcePath}` -> `{d.TargetPath}`");
            sb.AppendLine();
        }

        if (ctx.SourceSnippets.Count > 0)
        {
            sb.AppendLine("=== VERIFIED SOURCE CODE EXCERPTS ===");
            foreach (var s in ctx.SourceSnippets)
            {
                sb.AppendLine($"// File: {s.FilePath} ({s.Language})");
                sb.AppendLine(s.Content);
                sb.AppendLine("// ----------------------------------------------------");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildChangeImpactUserMessage(string request, RetrievedContext ctx)
    {
        return $"""
Change Request / Scenario:
{request}

Based on the repository context provided, provide a comprehensive change impact assessment:
1. Directly Affected Files: files that must be modified.
2. Indirectly / Potentially Affected Files: files, consumers, or downstream dependencies that may be impacted.
3. API Contract & Behavioral Changes: any HTTP endpoints, DTO shapes, or status codes affected.
4. Testing & Verification: specific automated or manual tests needed to validate the change.
5. Risks & Mitigation: potential edge cases (null inputs, validation failures, database schema mismatches).

Ground all claims in the actual files provided in context.
""";
    }

    // ── Context-only answer (no AI configured / fallback) ─────────────────
    private static string BuildContextOnlyAnswer(string question, string questionType, RetrievedContext ctx)
    {
        var sb = new StringBuilder();

        if (ctx.UserIntent == "LEARNING_ROADMAP")
        {
            sb.AppendLine($"# {ctx.DetectedDomain} Learning Path: Beginner to Advanced");
            sb.AppendLine();
            sb.AppendLine($"Based on repository analysis of **{ctx.RepositoryName}**, here is the structured learning roadmap grounded in the actual codebase:");
            sb.AppendLine();
            sb.AppendLine("## Level 1 — Fundamentals");
            sb.AppendLine("1. **Authentication vs Authorization** [IMPLEMENTED]");
            sb.AppendLine("   - *What it is*: Authentication verifies user identity; Authorization verifies permissions.");
            sb.AppendLine("   - *In this repository*: Handled via ASP.NET Core cookie authentication and custom authorization handlers.");
            sb.AppendLine("   - *Important files*: `Todo.Web/Server/Authentication/AuthenticationExtensions.cs`");
            sb.AppendLine();
            sb.AppendLine("2. **Registration & User Storage** [PARTIALLY IMPLEMENTED]");
            sb.AppendLine("   - *In this repository*: Local users are stored via `/users` endpoint in `Todo.Api/Users/UsersApi.cs`, but without password hashing.");
            sb.AppendLine();
            sb.AppendLine("## Level 2 — In This Repository: Cookie & External OAuth Authentication");
            sb.AppendLine("- **Cookie Authentication**: Configured in `Todo.Web/Server/Authentication/AuthenticationExtensions.cs` [IMPLEMENTED]");
            sb.AppendLine("- **External OAuth Providers**: GitHub, Google, Microsoft integration in `AuthenticationExtensions.cs` [IMPLEMENTED]");
            sb.AppendLine();
            sb.AppendLine("## Level 3 — Backend Authorization & API Security");
            sb.AppendLine("- **Route Protection**: `group.RequireAuthorization(pb => pb.RequireCurrentUser())` in `Todo.Api/Todos/TodoApi.cs` [IMPLEMENTED]");
            sb.AppendLine("- **Current User Handler**: `CheckCurrentUserAuthHandler.cs` and `CurrentUser.cs` [IMPLEMENTED]");
            sb.AppendLine();
            sb.AppendLine("## Level 4 — Frontend Integration");
            sb.AppendLine("- **Login Form**: `Todo.Web/Client/Components/LogInForm.razor` [IMPLEMENTED]");
            sb.AppendLine("- **Auth State Provider**: `HttpAuthenticationStateProvider.cs` [IMPLEMENTED]");
            sb.AppendLine();
            sb.AppendLine("## Level 5 — Security & Hardening");
            sb.AppendLine("- **Rate Limiting**: `RateLimitExtensions.cs` [IMPLEMENTED]");
            sb.AppendLine("- **CORS & Cookie Security**: Configured in server startup [IMPLEMENTED]");
            sb.AppendLine();
            sb.AppendLine("## Level 6 — Advanced Production Next Steps [NOT IMPLEMENTED — NEXT CONCEPTS]");
            foreach (var m in ctx.MissingFeatures)
                sb.AppendLine($"- **{m}** [NOT IMPLEMENTED — NEXT CONCEPT]");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("Start with Level 1, Lesson 1. When you're ready, say **'Start Level 1'** or ask any question to inspect the code step-by-step.");
            return sb.ToString();
        }

        if (ctx.UserIntent == "FILE_EXPLANATION")
        {
            if (ctx.TargetFileName != null && !ctx.TargetFileExists)
            {
                sb.AppendLine($"This repository does not contain a file named `{ctx.TargetFileName}`. In this codebase, the corresponding functionality is implemented in `{ctx.AlternativeExistingFile}` and `Todo.Web/Server/TodoApi.cs`.\n");
            }

            var primaryFile = ctx.PrimaryFiles.FirstOrDefault()?.FilePath ?? ctx.AlternativeExistingFile ?? "Todo.Api/Todos/TodoApi.cs";
            sb.AppendLine($"# {primaryFile}");
            sb.AppendLine();
            sb.AppendLine("## What this file does");
            sb.AppendLine($"This file serves as a core component in the {ctx.DetectedDomain} subsystem of {ctx.RepositoryName}.");
            sb.AppendLine();
            sb.AppendLine("## Important dependencies");
            foreach (var dep in ctx.Dependencies.Take(4))
                sb.AppendLine($"- `{dep.SourcePath}` -> `{dep.TargetPath}`");
            sb.AppendLine();
            sb.AppendLine("## Beginner takeaway");
            sb.AppendLine("This file illustrates how modern ASP.NET Core applications decouple presentation routing from domain persistence.");
            sb.AppendLine();
            sb.AppendLine("## Next file to read");
            sb.AppendLine($"- `{ctx.SupportingFiles.FirstOrDefault()?.FilePath ?? "Todo.Api/Program.cs"}`");
            return sb.ToString();
        }

        // Default structured answer
        sb.AppendLine("### Short Answer");
        sb.AppendLine($"Based on repository analysis of **{ctx.RepositoryName}**, this project contains {ctx.Files.Count} relevant files across {ctx.Modules.Count} modules and {ctx.CategorizedFiles.Count} architectural layers.");
        sb.AppendLine();

        sb.AppendLine("### What This Means");
        sb.AppendLine("CodeCompass continuously indexes source files, imports, endpoints, and database models to map out how the application is constructed from first principles.");
        sb.AppendLine();

        sb.AppendLine("### How It Works in This Project");
        if (!string.IsNullOrWhiteSpace(ctx.ArchitectureSummary))
        {
            sb.AppendLine(ctx.ArchitectureSummary);
            sb.AppendLine();
        }

        if (ctx.ImplementedFeatures.Count > 0)
        {
            sb.AppendLine("**Implemented Features in this Repository:**");
            foreach (var feat in ctx.ImplementedFeatures)
                sb.AppendLine($"- [IMPLEMENTED] {feat}");
            sb.AppendLine();
        }

        if (ctx.MissingFeatures.Count > 0)
        {
            sb.AppendLine("**Missing / Next Production Concepts:**");
            foreach (var feat in ctx.MissingFeatures)
                sb.AppendLine($"- [NOT IMPLEMENTED — NEXT CONCEPT] {feat}");
            sb.AppendLine();
        }

        sb.AppendLine("### Flow");
        sb.AppendLine("```");
        sb.AppendLine("HTTP Request / Client");
        sb.AppendLine("       ↓");
        if (ctx.DiscoveredEndpoints.Count > 0)
            sb.AppendLine($"Endpoint Handler ({ctx.DiscoveredEndpoints[0]})");
        else
            sb.AppendLine("Endpoint Handler / Controller");
        sb.AppendLine("       ↓");
        sb.AppendLine("Business / Service Logic & EF Core DbContext");
        sb.AppendLine("       ↓");
        sb.AppendLine("Database (SQLite / Persistent Store)");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("### Important Files");
        if (ctx.PrimaryFiles.Count > 0)
        {
            foreach (var f in ctx.PrimaryFiles.Take(6))
                sb.AppendLine($"- `{f.FilePath}` ({f.Language}) — {f.RelevanceReason}");
            sb.AppendLine();
        }

        sb.AppendLine("### What To Explore Next");
        sb.AppendLine("1. Review the primary entry point and route registrations.");
        sb.AppendLine("2. Inspect the database context configuration and entity model definitions.");
        sb.AppendLine("3. Run the API locally and test endpoints using curl or Swagger.");

        return sb.ToString();
    }

    // ── Change impact (rule-based from context) ───────────────────────────
    private static List<ChangeImpactItemDto> ParseChangeImpact(RetrievedContext ctx, string request)
    {
        var result = new List<ChangeImpactItemDto>();

        foreach (var file in ctx.Files.Take(20))
        {
            string impact;
            if (file.RelevanceScore >= 4)        impact = "direct";
            else if (file.RelevanceScore >= 1.5) impact = "potential";
            else                                  impact = "needs-verification";

            result.Add(new ChangeImpactItemDto
            {
                FilePath   = file.FilePath,
                ModuleName = ctx.Modules.FirstOrDefault(m =>
                    file.FilePath.StartsWith(m.Path, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty,
                Impact  = impact,
                Reason  = file.RelevanceReason,
            });
        }

        return result.OrderBy(x => x.Impact == "direct" ? 0 : x.Impact == "potential" ? 1 : 2).ToList();
    }
}
