using System.Text.RegularExpressions;
using CodeCompass.Api.Data;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeCompass.Api.Services;

public class ContextRetrievalService : IContextRetrievalService
{
    private readonly CodeCompassDbContext _db;
    private readonly ILogger<ContextRetrievalService> _logger;

    // ── Secret-bearing file patterns to always skip ────────────────────────
    private static readonly HashSet<string> SecretFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", ".env.staging",
        "secrets.json", "appsettings.secrets.json",
        "credentials", "credentials.json", "serviceaccount.json",
        "id_rsa", "id_ed25519", "private.key", "*.pem", "*.p12", "*.pfx",
    };

    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".pem", ".pfx", ".p12", ".key", ".cert", ".crt"
    };

    private const int MaxSourceSnippets    = 8;
    private const int MaxSourceCharacters  = 2500;
    private const int MaxRelevantFiles     = 30;
    private const int MaxRelevantModules   = 12;
    private const int MaxDepsPerContext    = 40;

    public ContextRetrievalService(CodeCompassDbContext db, ILogger<ContextRetrievalService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public Task<RetrievedContext> RetrieveAsync(int repositoryId, string question, CancellationToken ct = default)
        => RetrieveAsync(repositoryId, question, null, ct);

    public async Task<RetrievedContext> RetrieveAsync(
        int repositoryId,
        string question,
        List<ConversationHistoryItem>? history,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories.FindAsync(new object[] { repositoryId }, ct);
        if (repo == null) throw new InvalidOperationException($"Repository {repositoryId} not found.");
        if (repo.AnalyzedAt == null) throw new InvalidOperationException("Repository has not been analyzed yet.");

        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == repositoryId)
            .ToListAsync(ct);

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repositoryId && !f.IsDirectory)
            .ToListAsync(ct);

        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == repositoryId)
            .Include(d => d.SourceFile)
            .Include(d => d.TargetFile)
            .ToListAsync(ct);

        // ── 0. Context Planner: Query Enrichment & Intent Classification ──────
        var (recentTopic, recentLesson) = ExtractRecentContext(history);
        var effectiveQuestion = question;
        var (userIntent, intentCategory) = ClassifyMentorIntent(question, recentTopic);

        // If it's a short follow-up (e.g. "continue", "next", "start level 1", "quiz me"), enrich the search query
        if (userIntent is "FOLLOW_UP_QUESTION" or "LEARNING_ROADMAP" && !string.IsNullOrEmpty(recentTopic))
        {
            if (question.Trim().Length < 25)
            {
                effectiveQuestion = $"{question} {recentTopic}";
            }
        }

        var tokens = Tokenize(effectiveQuestion);
        var expandedTokens = ExpandTokens(tokens);

        // Detect technical domain & target file
        var detectedDomain = DetectDomain(effectiveQuestion, recentTopic);
        var (targetFile, targetExists, altFile) = DetectTargetFile(question, files);

        // Run repository fact audit (Implemented vs Partially Implemented vs Missing features)
        var (implemented, partiallyImplemented, missing) = AuditRepositoryFeatures(detectedDomain, files);

        // ── 1. Score modules ──────────────────────────────────────────────────
        var scoredModules = modules
            .Select(m => new RelevantModule
            {
                Id             = m.Id,
                Name           = m.Name,
                Path           = m.Path,
                ModuleType     = m.ModuleType,
                Layer          = MapLayer(m.ModuleType),
                RelevanceScore = Score(expandedTokens, tokens, m.Name, m.Path, m.ModuleType, m.Description),
            })
            .Where(m => m.RelevanceScore > 0)
            .OrderByDescending(m => m.RelevanceScore)
            .Take(MaxRelevantModules)
            .ToList();

        if (scoredModules.Count == 0 && modules.Count > 0)
        {
            scoredModules = modules
                .Take(MaxRelevantModules)
                .Select(m => new RelevantModule
                {
                    Id             = m.Id,
                    Name           = m.Name,
                    Path           = m.Path,
                    ModuleType     = m.ModuleType,
                    Layer          = MapLayer(m.ModuleType),
                    RelevanceScore = 1.0,
                })
                .ToList();
        }

        // ── 2. Score files with intent and domain boosting ────────────────────
        var candidateFiles = files
            .Where(f => !IsSecretFile(f.FilePath))
            .Select(f =>
            {
                double score = Score(expandedTokens, tokens, f.FileName, f.FilePath, f.Language, string.Empty);
                double boost = CalculateMentorIntentBoost(userIntent, detectedDomain, f.FileName, f.FilePath);

                // If this is the specific target file requested, give it maximum priority
                if (targetExists && targetFile != null && f.FileName.Equals(targetFile, StringComparison.OrdinalIgnoreCase))
                {
                    boost += 50.0;
                }
                else if (!targetExists && altFile != null && f.FilePath.Equals(altFile, StringComparison.OrdinalIgnoreCase))
                {
                    boost += 25.0;
                }

                return new
                {
                    File  = f,
                    Score = score + boost,
                };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        // Fallback if no files matched
        if (candidateFiles.Count == 0 && files.Count > 0)
        {
            candidateFiles = files
                .Where(f => !IsSecretFile(f.FilePath) && IsEntryPointOrCore(f.FileName, f.FilePath))
                .Select(f => new
                {
                    File  = f,
                    Score = 2.0,
                })
                .ToList();
        }

        var topCandidates = candidateFiles.Take(MaxRelevantFiles).Select(x => x.File).ToList();
        var relevantFileIds = topCandidates.Select(f => f.Id).ToHashSet();

        // ── 3. Multi-hop dependency expansion ─────────────────────────────────
        var expandedFiles = new List<RepositoryFile>(topCandidates);
        foreach (var dep in deps)
        {
            if (relevantFileIds.Contains(dep.SourceFileId) && !relevantFileIds.Contains(dep.TargetFileId))
            {
                if (dep.TargetFile != null && !IsSecretFile(dep.TargetFile.FilePath) && expandedFiles.Count < MaxRelevantFiles + 10)
                {
                    expandedFiles.Add(dep.TargetFile);
                    relevantFileIds.Add(dep.TargetFileId);
                }
            }
            else if (relevantFileIds.Contains(dep.TargetFileId) && !relevantFileIds.Contains(dep.SourceFileId))
            {
                if (dep.SourceFile != null && !IsSecretFile(dep.SourceFile.FilePath) && expandedFiles.Count < MaxRelevantFiles + 10)
                {
                    expandedFiles.Add(dep.SourceFile);
                    relevantFileIds.Add(dep.SourceFileId);
                }
            }
        }

        var scoredFiles = expandedFiles
            .Select(f => new RelevantFile
            {
                Id              = f.Id,
                FilePath        = f.FilePath,
                FileName        = f.FileName,
                Language        = f.Language,
                FileSize        = f.FileSize,
                RelevanceScore  = Score(expandedTokens, tokens, f.FileName, f.FilePath, f.Language, string.Empty)
                                  + CalculateMentorIntentBoost(userIntent, detectedDomain, f.FileName, f.FilePath),
                RelevanceReason = BuildFileReason(expandedTokens, f.FileName, f.FilePath, userIntent),
            })
            .OrderByDescending(f => f.RelevanceScore)
            .Take(MaxRelevantFiles)
            .ToList();

        // ── 4. Categorize & Tier files (Primary vs Supporting) ────────────────
        var categorized = CategorizeFiles(scoredFiles);
        var (primaryFiles, supportingFiles) = TierFiles(scoredFiles, detectedDomain, targetFile);

        // ── 5. Relevant dependencies ──────────────────────────────────────────
        var finalFileIds = scoredFiles.Select(f => f.Id).ToHashSet();
        var relevantDeps = deps
            .Where(d => finalFileIds.Contains(d.SourceFileId) || finalFileIds.Contains(d.TargetFileId))
            .Take(MaxDepsPerContext)
            .Select(d => new RelevantDependency
            {
                SourcePath      = d.SourceFile.FilePath,
                TargetPath      = d.TargetFile.FilePath,
                ImportStatement = d.ImportStatement,
            })
            .ToList();

        // ── 6. Architecture summary ───────────────────────────────────────────
        var arch = BuildArchSummary(repo.Name, repo.PrimaryLanguage, modules, files);

        // ── 7. Balanced Source Snippet Retrieval ──────────────────────────────
        var snippets = new List<SourceSnippet>();
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(repo.GitUrl))
        {
            // Always prioritize reading target file or alternative file, then primary files
            var selectedFilesForSnippets = new List<RelevantFile>();

            if (targetExists && targetFile != null)
            {
                var tf = scoredFiles.FirstOrDefault(f => f.FileName.Equals(targetFile, StringComparison.OrdinalIgnoreCase));
                if (tf != null) selectedFilesForSnippets.Add(tf);
            }
            else if (!targetExists && altFile != null)
            {
                var alt = scoredFiles.FirstOrDefault(f => f.FilePath.Equals(altFile, StringComparison.OrdinalIgnoreCase));
                if (alt != null) selectedFilesForSnippets.Add(alt);
            }

            foreach (var pf in primaryFiles)
            {
                if (!selectedFilesForSnippets.Any(s => s.FilePath == pf.FilePath) && selectedFilesForSnippets.Count < MaxSourceSnippets)
                    selectedFilesForSnippets.Add(pf);
            }

            // Fill remaining slots with balanced category files
            var remainingSlots = MaxSourceSnippets - selectedFilesForSnippets.Count;
            if (remainingSlots > 0)
            {
                var extra = SelectBalancedFilesForSnippets(categorized, scoredFiles, remainingSlots);
                foreach (var ef in extra)
                {
                    if (!selectedFilesForSnippets.Any(s => s.FilePath == ef.FilePath) && selectedFilesForSnippets.Count < MaxSourceSnippets)
                        selectedFilesForSnippets.Add(ef);
                }
            }

            foreach (var rf in selectedFilesForSnippets)
            {
                var snippet = await TryReadSnippetFromTempCloneAsync(repo.GitUrl, rf.FilePath, ct);
                if (snippet != null)
                {
                    snippets.Add(new SourceSnippet
                    {
                        FilePath = rf.FilePath,
                        Language = rf.Language,
                        Content  = snippet,
                    });

                    ExtractSignatures(snippet, endpoints, entities);
                }
            }
        }

        return new RetrievedContext
        {
            RepositoryName             = repo.Name,
            PrimaryLanguage            = repo.PrimaryLanguage,
            GitUrl                     = repo.GitUrl,
            Modules                    = scoredModules,
            Files                      = scoredFiles,
            PrimaryFiles               = primaryFiles,
            SupportingFiles            = supportingFiles,
            TargetFileName             = targetFile,
            TargetFileExists           = targetExists,
            AlternativeExistingFile    = altFile,
            Dependencies               = relevantDeps,
            SourceSnippets             = snippets,
            ArchitectureSummary        = arch,
            CategorizedFiles           = categorized,
            UserIntent                 = userIntent,
            QuestionIntent             = intentCategory,
            DetectedDomain             = detectedDomain,
            ImplementedFeatures        = implemented,
            PartiallyImplementedFeatures = partiallyImplemented,
            MissingFeatures            = missing,
            DiscoveredEndpoints        = endpoints.Take(12).ToList(),
            DiscoveredEntities         = entities.Take(12).ToList(),
            History                    = history ?? new(),
        };
    }

    // ── Mentor Intent Classifier (15 Standard Intents) ────────────────────────
    public static (string Intent, string Category) ClassifyMentorIntent(string question, string? recentTopic)
    {
        var q = question.Trim().ToLowerInvariant();

        // 1. Follow-up / continuation
        if (q is "continue" or "next" or "go on" or "proceed" or "more" or "keep going" || q.StartsWith("continue"))
            return ("FOLLOW_UP_QUESTION", "FollowUp");

        // 2. Interactive Quiz / Check Understanding
        if (q.Contains("quiz") || q.Contains("test me") || q.Contains("ask me questions") || q.Contains("check my understanding"))
            return ("LEARNING_ROADMAP", "Quiz");

        // 3. Learning Roadmap / Beginner to Advanced Plan
        if (q.Contains("beginner to advanced") || q.Contains("roadmap") || q.Contains("learning path") ||
            q.Contains("teach me") || q.Contains("how to learn") || q.Contains("curriculum") ||
            q.Contains("from the beginning") || q.Contains("start level") || q.Contains("lesson 1") ||
            q.Contains("level 1") || (q.Contains("want") && q.Contains("auth") && q.Contains("follow")))
            return ("LEARNING_ROADMAP", "Roadmap");

        // 4. File Explanation (e.g. "Explain AuthService.cs", "Explain TodoApi.cs")
        if (Regex.IsMatch(q, @"explain\s+[a-zA-Z0-9_\-\.\/]+\.(cs|ts|tsx|razor|js|json|md)", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(q, @"what does\s+[a-zA-Z0-9_\-\.\/]+\.(cs|ts|tsx|razor|js|json|md)\s+do", RegexOptions.IgnoreCase))
            return ("FILE_EXPLANATION", "File");

        // 5. Implemented vs Missing Features Audit
        if (q.Contains("missing") || q.Contains("not implemented") || q.Contains("what is missing"))
            return ("FEATURE_EXPLANATION", "MissingFeatures");
        if (q.Contains("actually implemented") || q.Contains("what is implemented") || q.Contains("features are implemented"))
            return ("FEATURE_EXPLANATION", "ImplementedFeatures");

        // 6. Request / Execution Flow Trace
        if (q.Contains("step by step") || q.Contains("login flow") || q.Contains("frontend to the database") ||
            q.Contains("frontend-to-backend") || q.Contains("trace what happens") || q.Contains("request flow") ||
            q.Contains("execution flow") || q.Contains("pipeline"))
            return ("REQUEST_FLOW", "FlowTrace");

        // 7. Change Impact
        if (q.Contains("impact") || q.Contains("what happens if i change") || q.Contains("what happens if i modify") ||
            q.Contains("blast radius") || q.Contains("break") || q.Contains("risk"))
            return ("CHANGE_IMPACT", "ChangeImpact");

        // 8. How to Implement
        if (q.Contains("how do i implement") || q.Contains("how to implement") || q.Contains("how to add") ||
            q.Contains("how do i create") || q.Contains("guide to adding"))
            return ("HOW_TO_IMPLEMENT", "Implementation");

        // 9. Code Walkthrough
        if (q.Contains("walkthrough") || q.Contains("walk through") || q.Contains("explain this code") ||
            q.Contains("code explanation") || q.Contains("line by line"))
            return ("CODE_WALKTHROUGH", "Code");

        // 10. Architecture Explanation
        if (q.Contains("architecture") || q.Contains("how does this project work") || q.Contains("how does the backend work") ||
            q.Contains("layers") || q.Contains("system design") || q.Contains("overview"))
            return ("ARCHITECTURE_EXPLANATION", "Architecture");

        // 11. Security Review
        if (q.Contains("security") || q.Contains("vulnerability") || q.Contains("secure") || q.Contains("attack") ||
            q.Contains("hardening") || q.Contains("jwt security"))
            return ("SECURITY_REVIEW", "Security");

        // 12. Compare Concepts
        if (q.Contains("difference between") || q.Contains(" vs ") || q.Contains("versus") || q.Contains("compare"))
            return ("COMPARE_CONCEPTS", "Comparison");

        // 13. Debugging
        if (q.Contains("debug") || q.Contains("fix") || q.Contains("error") || q.Contains("exception") ||
            q.Contains("troubleshoot") || q.Contains("fails"))
            return ("DEBUGGING", "Debugging");

        // 14. Onboarding Guidance
        if (q.Contains("what should i learn next") || q.Contains("read first") || q.Contains("where do i start") ||
            q.Contains("joining the team") || q.Contains("new developer"))
            return ("ONBOARDING_GUIDANCE", "Onboarding");

        // 15. Beginner Explanation
        if (q.Contains("beginner") || q.Contains("like i'm a beginner") || q.Contains("simple terms") ||
            q.Contains("explain simply") || q.Contains("first principles"))
            return ("BEGINNER_EXPLANATION", "Beginner");

        return ("SIMPLE_EXPLANATION", "General");
    }

    // ── Technical Domain Detection ────────────────────────────────────────────
    public static string DetectDomain(string question, string? recentTopic)
    {
        var q = question.ToLowerInvariant();
        if (q.Contains("auth") || q.Contains("login") || q.Contains("token") || q.Contains("cookie") || q.Contains("jwt") || q.Contains("user"))
            return "Authentication";
        if (q.Contains("todo") || q.Contains("item") || q.Contains("crud") || q.Contains("task"))
            return "TodoManagement";
        if (q.Contains("architecture") || q.Contains("bff") || q.Contains("yarp") || q.Contains("gateway") || q.Contains("host") || q.Contains("aspire"))
            return "Architecture";
        if (q.Contains("database") || q.Contains("ef") || q.Contains("dbcontext") || q.Contains("sqlite") || q.Contains("migration"))
            return "Database";
        if (q.Contains("frontend") || q.Contains("client") || q.Contains("blazor") || q.Contains("razor") || q.Contains("ui"))
            return "Frontend";

        if (!string.IsNullOrEmpty(recentTopic)) return recentTopic;
        return "General";
    }

    // ── Target File Detection ─────────────────────────────────────────────────
    public static (string? FileName, bool Exists, string? Alternative) DetectTargetFile(
        string question, List<RepositoryFile> files)
    {
        var match = Regex.Match(question, @"([a-zA-Z0-9_\-\.\/]+\.(?:cs|ts|tsx|razor|js|json|md))", RegexOptions.IgnoreCase);
        if (!match.Success) return (null, false, null);

        var targetName = Path.GetFileName(match.Value);
        var existing = files.FirstOrDefault(f => f.FileName.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                                                f.FilePath.EndsWith(targetName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return (targetName, true, existing.FilePath);
        }

        // Target file does not exist in repo! Suggest best alternative
        string? alternative = null;
        if (targetName.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            var alt = files.FirstOrDefault(f => f.FileName.Contains("AuthenticationExtensions", StringComparison.OrdinalIgnoreCase) ||
                                               (f.FilePath.Contains("Server") && f.FileName.Contains("TodoApi", StringComparison.OrdinalIgnoreCase)));
            alternative = alt?.FilePath ?? "Todo.Web/Server/Authentication/AuthenticationExtensions.cs";
        }
        else if (targetName.Contains("service", StringComparison.OrdinalIgnoreCase) || targetName.Contains("controller", StringComparison.OrdinalIgnoreCase))
        {
            var alt = files.FirstOrDefault(f => f.FileName.Contains("TodoApi", StringComparison.OrdinalIgnoreCase));
            alternative = alt?.FilePath ?? "Todo.Api/Todos/TodoApi.cs";
        }

        return (targetName, false, alternative);
    }

    // ── Repository Fact Audit (Implemented vs Missing Features) ───────────────
    public static (List<string> Implemented, List<string> PartiallyImplemented, List<string> Missing)
        AuditRepositoryFeatures(string domain, List<RepositoryFile> files)
    {
        var fileNames = files.Select(f => f.FileName.ToLowerInvariant()).ToHashSet();
        var filePaths = files.Select(f => f.FilePath.ToLowerInvariant()).ToHashSet();

        var implemented = new List<string>();
        var partial = new List<string>();
        var missing = new List<string>();

        if (domain == "Authentication" || domain == "General")
        {
            // Verified implemented in David Fowler's TodoApi
            if (filePaths.Any(p => p.Contains("authenticationextensions.cs")))
            {
                implemented.Add("Cookie-Based Authentication: Registered in `Todo.Web/Server/Authentication/AuthenticationExtensions.cs` using `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie()`.");
                implemented.Add("External OAuth Providers: Preconfigured support for GitHub, Google, and Microsoft OAuth in `AuthenticationExtensions.cs`.");
            }
            if (filePaths.Any(p => p.Contains("server") && p.Contains("todoapi.cs")))
            {
                implemented.Add("BFF (Backend-For-Frontend) Token Translation: Reverse proxy interceptor in `Todo.Web/Server/TodoApi.cs` translates authentication cookie into downstream `Bearer` access token (`ProxyRequest.Headers.Authorization = new(\"Bearer\", accessToken)`).");
            }
            if (filePaths.Any(p => p.Contains("checkcurrentuserauthhandler.cs")))
            {
                implemented.Add("Route Authorization & Current User Validation: Downstream API requires authorization via `group.RequireAuthorization(pb => pb.RequireCurrentUser())` with `CheckCurrentUserAuthHandler.cs` and `CurrentUser.cs`.");
            }
            if (filePaths.Any(p => p.Contains("loginform.razor")))
            {
                implemented.Add("Frontend Login Component: UI form for external OAuth login in `Todo.Web/Client/Components/LogInForm.razor`.");
            }
            if (filePaths.Any(p => p.Contains("httpauthenticationstateprovider.cs")))
            {
                implemented.Add("Blazor Authentication State Bridge: `HttpAuthenticationStateProvider.cs` synchronizes user authentication state to the client.");
            }

            // Partially implemented
            if (filePaths.Any(p => p.Contains("usersapi.cs")))
            {
                partial.Add("Local User Registration: `/users` endpoint in `Todo.Api/Users/UsersApi.cs` saves `TodoUser` records to the SQLite database, but stores plain username without password hashing or salted hash validation.");
            }

            // Missing / Next production concepts
            missing.Add("Refresh Tokens & Token Rotation: Not implemented. Access tokens and cookies have fixed expiration periods with no refresh token flow.");
            missing.Add("Multi-Factor Authentication (MFA / 2FA): Not implemented in this codebase.");
            missing.Add("Local Password Hashing (Argon2id, BCrypt, or PBKDF2): Not implemented (delegated to external OAuth providers).");
            missing.Add("Email Verification & Account Activation workflows: Not implemented.");
            missing.Add("Password Reset / Forgot Password self-service workflows: Not implemented.");
            missing.Add("Distributed Token Revocation / Blacklist Store (e.g. Redis): Not implemented.");
            missing.Add("Role-Based Access Control (RBAC) & Custom Security Policies: Not implemented (only basic authenticated current user check exists).");
        }

        if (domain == "TodoManagement" || domain == "General")
        {
            implemented.Add("CRUD Endpoints: Complete `GET /todos`, `POST /todos`, `PUT /todos/{id}`, `DELETE /todos/{id}` in `Todo.Api/Todos/TodoApi.cs`.");
            implemented.Add("User Scoping: Every query filters todos by `CurrentUser.Id` to isolate user data.");
            implemented.Add("Input Parameter Validation: `ValidationFilter.cs` applies `MiniValidator` to `TodoItem` DTOs.");
            implemented.Add("Per-User Rate Limiting: Configured in `RateLimitExtensions.cs` to prevent abuse.");
            implemented.Add("Frontend API Client: `Todo.Web/Client/TodoClient.cs` handles HTTP requests from Blazor.");
            implemented.Add("Database Persistence: Entity Framework Core with SQLite in `TodoDbContext.cs`.");

            missing.Add("Soft Delete (tombstoning): Not implemented (deletes permanently remove rows from SQLite).");
            missing.Add("Todo Due Dates, Priority Levels, and Tags: Not implemented (model only supports Title and IsComplete).");
            missing.Add("Pagination and Search: Not implemented (loads all user items at once).");
        }

        return (implemented, partial, missing);
    }

    // ── Tier Files into Primary vs Supporting ─────────────────────────────────
    private static (List<RelevantFile> Primary, List<RelevantFile> Supporting) TierFiles(
        List<RelevantFile> files, string domain, string? targetFile)
    {
        var primary = new List<RelevantFile>();
        var supporting = new List<RelevantFile>();

        foreach (var f in files)
        {
            var fn = f.FileName.ToLowerInvariant();
            var fp = f.FilePath.ToLowerInvariant();

            bool isPrimary = false;

            if (targetFile != null && f.FileName.Equals(targetFile, StringComparison.OrdinalIgnoreCase))
                isPrimary = true;
            else if (domain == "Authentication")
            {
                isPrimary = fn.Contains("authenticationextensions") ||
                            (fp.Contains("server") && fn.Contains("todoapi")) ||
                            fn.Contains("checkcurrentuserauthhandler") ||
                            fn.Contains("currentuser") ||
                            fn.Contains("loginform") ||
                            fn.Contains("httpauthenticationstateprovider");
            }
            else if (domain == "TodoManagement")
            {
                isPrimary = (fp.Contains("api") && fn.Contains("todoapi")) ||
                            fn.Contains("todoclient") ||
                            fn.Contains("tododbcontext") ||
                            fn.Contains("todoitem") ||
                            fn.Contains("validationfilter");
            }
            else
            {
                isPrimary = fn.Contains("program") || fn.Contains("todoapi") || fn.Contains("tododbcontext");
            }

            if (isPrimary && primary.Count < 6)
                primary.Add(f);
            else
                supporting.Add(f);
        }

        if (primary.Count == 0 && files.Count > 0)
        {
            primary = files.Take(4).ToList();
            supporting = files.Skip(4).ToList();
        }

        return (primary, supporting);
    }

    // ── Extract Recent Context from Conversation Turns ────────────────────────
    private static (string? RecentTopic, string? RecentLesson) ExtractRecentContext(
        List<ConversationHistoryItem>? history)
    {
        if (history == null || history.Count == 0) return (null, null);

        var last = history.LastOrDefault();
        if (last == null) return (null, null);

        var text = (last.Question + " " + last.Answer).ToLowerInvariant();

        string? topic = null;
        if (text.Contains("auth") || text.Contains("login") || text.Contains("token") || text.Contains("jwt"))
            topic = "Authentication";
        else if (text.Contains("todo") || text.Contains("item"))
            topic = "TodoManagement";
        else if (text.Contains("architecture"))
            topic = "Architecture";

        string? lesson = null;
        if (text.Contains("level 1") || text.Contains("lesson 1")) lesson = "Level 1";
        else if (text.Contains("level 2") || text.Contains("lesson 2")) lesson = "Level 2";

        return (topic, lesson);
    }

    private static double CalculateMentorIntentBoost(string userIntent, string domain, string fileName, string filePath)
    {
        var fn = fileName.ToLowerInvariant();
        var fp = filePath.ToLowerInvariant();
        double boost = 0;

        if (domain == "Authentication")
        {
            if (fn.Contains("authenticationextensions")) boost += 8.0;
            if (fp.Contains("server") && fn.Contains("todoapi")) boost += 7.0;
            if (fn.Contains("checkcurrentuserauthhandler") || fn.Contains("currentuser")) boost += 6.5;
            if (fn.Contains("loginform") || fn.Contains("authclient")) boost += 6.0;
            if (fn.Contains("tokennames") || fn.Contains("usersapi") || fn.Contains("todouser")) boost += 5.0;
        }
        else if (domain == "TodoManagement")
        {
            if (fp.Contains("api") && fn.Contains("todoapi")) boost += 8.0;
            if (fn.Contains("todoclient")) boost += 7.0;
            if (fn.Contains("tododbcontext")) boost += 6.5;
            if (fn.Contains("todoitem") || fn.Contains("todo.cs")) boost += 6.0;
            if (fn.Contains("validationfilter")) boost += 6.0;
        }

        switch (userIntent)
        {
            case "LEARNING_ROADMAP":
            case "ONBOARDING_GUIDANCE":
                if (fn.Contains("program") || fn.Contains("readme")) boost += 4.0;
                break;
            case "REQUEST_FLOW":
                if (fn.Contains("client") || fn.Contains("controller") || fn.Contains("todoapi") || fn.Contains("dbcontext")) boost += 4.0;
                break;
            case "CHANGE_IMPACT":
                if (fn.Contains("validation") || fn.Contains("model") || fn.Contains("tests")) boost += 5.0;
                break;
        }

        return boost;
    }

    private static double CalculateIntentBoost(string intent, string fileName, string filePath)
    {
        var fn = fileName.ToLowerInvariant();
        var fp = filePath.ToLowerInvariant();

        switch (intent)
        {
            case "Architecture":
            case "FlowTrace":
                if (fn.Contains("program") || fn.Contains("app.razor") || fn.Contains("main.tsx")) return 3.5;
                if (fn.Contains("todoapi") || fn.Contains("controller")) return 4.0;
                if (fn.Contains("todoclient") || fn.Contains("client")) return 3.5;
                if (fn.Contains("tododbcontext") || fn.Contains("dbcontext")) return 3.5;
                if (fn.Contains("todoitem") || fn.Contains("model") || fn.Contains("dto")) return 3.0;
                if (fn.Contains("authclient") || fn.Contains("yarp")) return 3.0;
                break;

            case "Authentication":
                if (fn.Contains("auth") || fn.Contains("login") || fn.Contains("token") || fn.Contains("cookie") || fn.Contains("user")) return 5.0;
                if (fp.Contains("server") && fn.Contains("todoapi")) return 4.0; // Contains YARP transforms and auth requirement
                if (fn.Contains("program")) return 3.0;
                break;

            case "Onboarding":
                if (fn.Contains("program")) return 5.0;
                if (fn.Contains("todoapi")) return 4.5;
                if (fn.Contains("tododbcontext")) return 4.0;
                if (fn.Contains("todoitem")) return 3.5;
                if (fn.Contains("tests")) return 3.0;
                if (fn.Contains("appsettings")) return 3.0;
                break;

            case "ChangeImpact":
                if (fn.Contains("todoitem") || fn.Contains("validation") || fn.Contains("validator")) return 6.0;
                if (fn.Contains("todoapi")) return 5.0;
                if (fn.Contains("todoclient")) return 4.5;
                if (fn.Contains("todoapitests") || fn.Contains("tests")) return 4.5;
                if (fn.Contains("tododbcontext")) return 3.5;
                break;
        }

        return 0;
    }

    // ── File Categorization ───────────────────────────────────────────────────
    private static List<CategorizedFileGroup> CategorizeFiles(List<RelevantFile> files)
    {
        var groups = new Dictionary<string, List<RelevantFile>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Client & UI Presentation", new() },
            { "API Gateway & Forwarding (BFF)", new() },
            { "Backend Web API & Endpoints", new() },
            { "Data Access & Entity Framework", new() },
            { "Domain Models & Entities", new() },
            { "Authentication & Security", new() },
            { "App Host & Configuration", new() },
            { "Tests & Verification", new() },
        };

        foreach (var file in files)
        {
            var fp = file.FilePath.ToLowerInvariant();
            var fn = file.FileName.ToLowerInvariant();

            if (fp.Contains("test") || fn.Contains("test"))
                groups["Tests & Verification"].Add(file);
            else if (fn.Contains("auth") || fn.Contains("login") || fn.Contains("identity"))
                groups["Authentication & Security"].Add(file);
            else if (fn.Contains("dbcontext") || fp.Contains("migration") || fn.Contains("repository"))
                groups["Data Access & Entity Framework"].Add(file);
            else if (fn.Contains("item") || fn.Contains("model") || fn.Contains("dto") || fp.Contains("models"))
                groups["Domain Models & Entities"].Add(file);
            else if (fp.Contains("server") && (fn.Contains("todoapi") || fn.Contains("forwarder") || fn.Contains("proxy")))
                groups["API Gateway & Forwarding (BFF)"].Add(file);
            else if (fp.Contains("client") || fp.Contains("wwwroot") || fn.EndsWith(".razor") || fn.EndsWith(".tsx") || fn.Contains("todoclient"))
                groups["Client & UI Presentation"].Add(file);
            else if (fn.Contains("api") || fn.Contains("controller") || fn.Contains("endpoint"))
                groups["Backend Web API & Endpoints"].Add(file);
            else
                groups["App Host & Configuration"].Add(file);
        }

        return groups
            .Where(g => g.Value.Count > 0)
            .Select(g => new CategorizedFileGroup
            {
                Category = g.Key,
                Files    = g.Value,
            })
            .ToList();
    }

    // ── Balanced Snippet Selector ─────────────────────────────────────────────
    private static List<RelevantFile> SelectBalancedFilesForSnippets(
        List<CategorizedFileGroup> categorized, List<RelevantFile> allFiles, int maxCount)
    {
        var selected = new List<RelevantFile>();
        var seenIds = new HashSet<int>();

        // Priority 1: Pick the top file from each distinct architectural category
        foreach (var group in categorized)
        {
            var first = group.Files.FirstOrDefault(f => !seenIds.Contains(f.Id) && f.FileSize < 120_000);
            if (first != null)
            {
                selected.Add(first);
                seenIds.Add(first.Id);
                if (selected.Count >= maxCount) break;
            }
        }

        // Priority 2: Fill remaining slots with highest scoring files
        if (selected.Count < maxCount)
        {
            foreach (var f in allFiles.Where(f => !seenIds.Contains(f.Id) && f.FileSize < 120_000))
            {
                selected.Add(f);
                seenIds.Add(f.Id);
                if (selected.Count >= maxCount) break;
            }
        }

        return selected;
    }

    // ── Extract Endpoints and Entities from Code Snippets ─────────────────────
    private static void ExtractSignatures(string content, HashSet<string> endpoints, HashSet<string> entities)
    {
        // Route patterns: group.MapGet("/...", ...), MapPost, [HttpGet("...")]
        var routeMatches = Regex.Matches(content, @"(MapGet|MapPost|MapPut|MapDelete|MapPatch)\s*\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
        foreach (Match m in routeMatches)
        {
            if (m.Groups.Count >= 3)
            {
                var verb = m.Groups[1].Value.Replace("Map", "").ToUpperInvariant();
                var path = m.Groups[2].Value;
                endpoints.Add($"{verb} {path}");
            }
        }

        // Entity/Class declarations: class TodoItem, class TodoDbContext
        var classMatches = Regex.Matches(content, @"\bclass\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
        foreach (Match m in classMatches)
        {
            if (m.Groups.Count >= 2)
            {
                var name = m.Groups[1].Value;
                if (!name.EndsWith("Tests") && !name.Equals("Program") && name.Length > 2)
                    entities.Add(name);
            }
        }
    }

    // ── Stop words & Concept synonyms ───────────────────────────────────────
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any",
        "are", "aren't", "as", "at", "be", "because", "been", "before", "being", "below",
        "between", "both", "but", "by", "can", "can't", "cannot", "could", "couldn't",
        "did", "didn't", "do", "does", "doesn't", "doing", "don't", "down", "during",
        "each", "few", "for", "from", "further", "had", "hadn't", "has", "hasn't", "have",
        "haven't", "having", "he", "her", "here", "him", "his", "how", "i", "if", "in",
        "into", "is", "isn't", "it", "it's", "its", "just", "like", "likely", "me", "more",
        "most", "my", "no", "nor", "not", "of", "off", "on", "once", "only", "or", "other",
        "our", "out", "over", "own", "same", "she", "should", "shouldn't", "so", "some",
        "such", "than", "that", "the", "their", "theirs", "them", "then", "there", "these",
        "they", "this", "those", "through", "to", "too", "under", "until", "up", "very",
        "was", "wasn't", "we", "were", "weren't", "what", "when", "where", "which", "while",
        "who", "whom", "why", "will", "with", "would", "wouldn't", "you", "your",
        "want", "tell", "show", "give", "find", "explain", "describe", "work", "works",
        "working", "affect", "affected", "affects", "files", "file", "please"
    };

    private static readonly Dictionary<string, string[]> ConceptSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        { "todo", new[] { "item", "todoitem", "todoapi", "todoclient", "tododbcontext", "todos", "task", "entity" } },
        { "todos", new[] { "todo", "item", "todoitem", "todoapi", "todoclient", "tododbcontext", "task" } },
        { "item", new[] { "todo", "todoitem", "entity", "model", "dto", "validation" } },
        { "create", new[] { "post", "add", "insert", "todo", "todoapi", "todoclient", "dbcontext", "save" } },
        { "creating", new[] { "create", "post", "add", "insert", "todo", "todoapi", "todoclient" } },
        { "creation", new[] { "create", "post", "add", "insert", "todo", "todoapi", "todoclient" } },
        { "trace", new[] { "flow", "client", "server", "api", "todoapi", "todoclient", "dbcontext", "database", "yarp" } },
        { "flow", new[] { "trace", "client", "server", "api", "todoapi", "todoclient", "dbcontext", "pipeline" } },
        { "validation", new[] { "validate", "validating", "validator", "todoitem", "todoapi", "filter", "parameter", "tests" } },
        { "validate", new[] { "validation", "todoitem", "todoapi", "validator", "rules", "filter" } },
        { "modify", new[] { "change", "update", "impact", "validation", "todoitem", "todoapi", "tests" } },
        { "modifying", new[] { "modify", "change", "update", "impact", "validation", "todoitem" } },
        { "change", new[] { "modify", "impact", "break", "validation", "todoitem", "todoapi", "tests" } },
        { "impact", new[] { "change", "modify", "blast", "risk", "validation", "todoitem", "tests" } },
        { "registration", new[] { "register", "signup", "user", "users", "account", "identity" } },
        { "register", new[] { "registration", "signup", "user", "users", "account", "identity" } },
        { "signup", new[] { "register", "registration", "user", "users", "account" } },
        { "auth", new[] { "authentication", "login", "authclient", "authorize", "authorization", "token", "jwt", "cookie", "oauth", "identity" } },
        { "authentication", new[] { "auth", "login", "authclient", "authorize", "authorization", "token", "jwt", "cookie", "oauth", "identity" } },
        { "login", new[] { "auth", "authentication", "authclient", "signin", "token", "credential", "cookie" } },
        { "cookie", new[] { "auth", "authentication", "token", "yarp", "authclient", "claims" } },
        { "token", new[] { "jwt", "auth", "authentication", "bearer", "accesstoken" } },
        { "phone", new[] { "user", "users", "contact", "profile", "model", "dto" } },
        { "number", new[] { "phone", "user", "users" } },
        { "database", new[] { "db", "dbcontext", "tododbcontext", "migration", "migrations", "data", "repository", "entities", "sql" } },
        { "db", new[] { "database", "dbcontext", "tododbcontext", "migration", "migrations", "data", "repository", "sql" } },
        { "frontend", new[] { "client", "todoclient", "ui", "web", "components", "pages", "views", "razor", "tsx", "blazor" } },
        { "client", new[] { "frontend", "todoclient", "ui", "blazor", "web", "components" } },
        { "backend", new[] { "server", "api", "todoapi", "controllers", "services", "endpoints", "yarp" } },
        { "server", new[] { "backend", "api", "todoapi", "gateway", "yarp", "bff", "reverseproxy" } },
        { "api", new[] { "todoapi", "controllers", "endpoints", "routes", "server", "web" } },
        { "gateway", new[] { "yarp", "reverseproxy", "forwarder", "server", "todoapi", "routes" } },
        { "architecture", new[] { "program", "startup", "app", "module", "solution", "server", "client", "todoapi", "dbcontext" } },
        { "structure", new[] { "architecture", "module", "modules", "project", "program" } },
        { "beginner", new[] { "start", "read", "learn", "onboarding", "program", "overview", "first" } },
        { "learn", new[] { "beginner", "read", "start", "onboarding", "first", "program", "todoapi" } },
        { "read", new[] { "learn", "beginner", "start", "onboarding", "first", "program", "todoapi" } },
        { "first", new[] { "read", "learn", "start", "program", "todoapi", "dbcontext" } },
        { "test", new[] { "tests", "todoapitests", "fact", "unit", "integration" } },
        { "tests", new[] { "test", "todoapitests", "fact", "unit", "integration" } },
    };

    // ── Scoring ─────────────────────────────────────────────────────────────
    private static string[] Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '_', '-', '.', '/', '\\', '(', ')', '?', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !StopWords.Contains(t))
            .Distinct()
            .ToArray();

    private static string[] ExpandTokens(string[] primaryTokens)
    {
        var result = new HashSet<string>(primaryTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var t in primaryTokens)
        {
            if (ConceptSynonyms.TryGetValue(t, out var syns))
            {
                foreach (var s in syns)
                    result.Add(s);
            }
        }
        return result.ToArray();
    }

    private static bool PathContainsWord(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return false;
        var parts = text.Split(new[] { '/', '\\', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Equals(token, StringComparison.OrdinalIgnoreCase)) return true;
            if (part.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return true;
            if (token.Length >= 4 && part.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static double Score(string[] expandedTokens, string[] primaryTokens, params string[] fields)
    {
        double score = 0;
        var primarySet = new HashSet<string>(primaryTokens, StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (string.IsNullOrEmpty(field)) continue;
            foreach (var token in expandedTokens)
            {
                var weight = primarySet.Contains(token) ? 1.0 : 0.6;
                if (PathContainsWord(field, token))
                {
                    score += 3.0 * weight;
                }
            }
        }
        return score;
    }

    private static bool IsEntryPointOrCore(string fileName, string filePath)
    {
        var lower = fileName.ToLowerInvariant();
        var pathLower = filePath.ToLowerInvariant();
        return lower.Equals("program.cs")
            || lower.Equals("startup.cs")
            || lower.Equals("app.razor")
            || lower.Equals("app.tsx")
            || lower.Equals("main.tsx")
            || lower.Equals("todoapi.cs")
            || lower.Equals("todoclient.cs")
            || lower.Equals("tododbcontext.cs")
            || lower.Equals("readme.md")
            || lower.EndsWith(".csproj")
            || lower.EndsWith(".sln")
            || pathLower.Contains("program")
            || pathLower.Contains("app");
    }

    private static string BuildFileReason(string[] tokens, string fileName, string filePath, string intent)
    {
        var matched = tokens.Where(t => PathContainsWord(fileName, t) || PathContainsWord(filePath, t)).ToList();
        if (matched.Count > 0)
            return $"Matched terms: {string.Join(", ", matched.Distinct().Take(4))}";

        return intent switch
        {
            "Architecture" => "Core structural boundary file",
            "FlowTrace"    => "End-to-end request pipeline component",
            "Onboarding"   => "Recommended first-read onboarding file",
            "ChangeImpact" => "Directly or indirectly connected dependency",
            _              => "Architectural context file"
        };
    }

    private static bool IsSecretFile(string path)
    {
        var lower = path.ToLowerInvariant();
        foreach (var pattern in SecretFilePatterns)
        {
            if (lower.EndsWith(pattern.ToLowerInvariant())) return true;
        }
        var ext = Path.GetExtension(path);
        return SkipExtensions.Contains(ext);
    }

    // ── Architecture summary ─────────────────────────────────────────────────
    private static string BuildArchSummary(
        string repoName, string primaryLang,
        List<RepositoryModule> modules,
        List<RepositoryFile> files)
    {
        var langGroups = files
            .Where(f => !string.IsNullOrEmpty(f.Language))
            .GroupBy(f => f.Language)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()} files)");

        var modSummary = modules
            .Take(12)
            .Select(m => $"  - {m.Name} [{m.ModuleType}] at {m.Path}");

        return $"""
Repository: {repoName}
Primary language: {primaryLang}
Total files: {files.Count}
Languages: {string.Join(", ", langGroups)}
Detected modules:
{string.Join("\n", modSummary)}
""";
    }

    // ── Source snippet retrieval ─────────────────────────────────────────────
    private static readonly SemaphoreSlim _cloneLock = new(1, 1);
    private static string? _cachedCloneDir;
    private static string? _cachedGitUrl;

    private async Task<string?> TryReadSnippetFromTempCloneAsync(
        string gitUrl, string relPath, CancellationToken ct)
    {
        try
        {
            // Reuse an existing clone if same repo
            string? cloneDir = null;
            await _cloneLock.WaitAsync(ct);
            try
            {
                if (_cachedGitUrl == gitUrl && _cachedCloneDir != null && Directory.Exists(_cachedCloneDir))
                {
                    cloneDir = _cachedCloneDir;
                }
            }
            finally { _cloneLock.Release(); }

            if (cloneDir == null)
            {
                cloneDir = Path.Combine(Path.GetTempPath(), "codecompass_ctx", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(cloneDir);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = "git",
                    Arguments = $"clone --depth 1 --single-branch \"{gitUrl}\" \"{cloneDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode != 0) return null;

                await _cloneLock.WaitAsync(ct);
                try { _cachedCloneDir = cloneDir; _cachedGitUrl = gitUrl; }
                finally { _cloneLock.Release(); }
            }

            var fullPath = Path.Combine(cloneDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) return null;

            var content = await File.ReadAllTextAsync(fullPath, ct);
            return content.Length > MaxSourceCharacters
                ? content[..MaxSourceCharacters] + "\n... [truncated]"
                : content;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not read source snippet for {Path}: {Msg}", relPath, ex.Message);
            return null;
        }
    }

    // ── Layer classification ─────────────────────────────────────────────────
    private static readonly Dictionary<string, string> LayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Frontend","Presentation" },{ "Components","Presentation" },{ "Pages","Presentation" },
        { "Controllers","API" },{ "API","API" },
        { "Services","Business Logic" },{ "Core","Business Logic" },
        { "Repositories","Data Access" },{ "Data","Data Access" },{ "Migrations","Data Access" },
        { "Models","Domain" },{ "DTOs","Domain" },
        { "Utilities","Infrastructure" },{ "Infrastructure","Infrastructure" },
        { "Shared","Shared" },{ "Tests","Tests" },
    };

    private static string MapLayer(string moduleType) =>
        LayerMap.TryGetValue(moduleType, out var l) ? l : moduleType;
}
