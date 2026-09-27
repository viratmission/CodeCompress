using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CodeCompass.Api.Services;

public class OnboardingService : IOnboardingService
{
    private readonly CodeCompassDbContext _db;
    private readonly WatsonxProvider _watsonx;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        CodeCompassDbContext db,
        WatsonxProvider watsonx,
        ILogger<OnboardingService> logger)
    {
        _db      = db;
        _watsonx = watsonx;
        _logger  = logger;
    }

    // ── Get or create onboarding path for a repository & role ───────────────
    public async Task<OnboardingPathDto> GetOrCreateOnboardingPathAsync(
        int repositoryId, string role, CancellationToken ct = default, int? userId = null)
    {
        var repo = await _db.Repositories
            .Include(r => r.Modules)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);

        if (repo == null)
            throw new InvalidOperationException($"Repository {repositoryId} not found.");

        var normalizedRole = NormalizeRole(role);

        // Check if an onboarding path already exists for this repo and role
        var path = await _db.OnboardingPaths
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.RepositoryId == repositoryId && p.Role == normalizedRole, ct);

        if (path == null)
        {
            path = await GeneratePathAsync(repo, normalizedRole, ct);
        }

        // Check or create UserOnboarding session scoped by current user
        var userOnboarding = await _db.UserOnboardings
            .Include(u => u.UserSteps)
            .FirstOrDefaultAsync(u => u.RepositoryId == repositoryId 
                                   && u.OnboardingPathId == path.Id
                                   && (userId == null ? u.UserId == null : u.UserId == userId), ct);

        if (userOnboarding == null)
        {
            userOnboarding = new UserOnboarding
            {
                RepositoryId       = repositoryId,
                OnboardingPathId   = path.Id,
                UserId             = userId,
                ProgressPercentage = 0,
                StartedAt          = DateTime.UtcNow,
            };
            _db.UserOnboardings.Add(userOnboarding);
            await _db.SaveChangesAsync(ct);

            // Create UserOnboardingStep records
            foreach (var step in path.Steps.OrderBy(s => s.Order))
            {
                _db.UserOnboardingSteps.Add(new UserOnboardingStep
                {
                    UserOnboardingId = userOnboarding.Id,
                    OnboardingStepId = step.Id,
                    Status           = "pending",
                });
            }
            await _db.SaveChangesAsync(ct);
        }

        // Reload user steps with statuses
        var userSteps = await _db.UserOnboardingSteps
            .Where(us => us.UserOnboardingId == userOnboarding.Id)
            .ToDictionaryAsync(us => us.OnboardingStepId, ct);

        var moduleDict = repo.Modules.ToDictionary(m => m.Id);

        var stepSummaries = path.Steps
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                userSteps.TryGetValue(s.Id, out var us);
                moduleDict.TryGetValue(s.ModuleId ?? 0, out var mod);

                return new OnboardingStepSummaryDto
                {
                    Id          = s.Id,
                    Title       = s.Title,
                    Description = s.Description,
                    Order       = s.Order,
                    StepType    = s.StepType,
                    ModuleId    = s.ModuleId,
                    ModuleName  = mod?.Name ?? string.Empty,
                    ModulePath  = mod?.Path ?? string.Empty,
                    Layer       = mod != null ? MapLayer(mod.ModuleType) : "Overview",
                    Status      = us?.Status ?? "pending",
                    CompletedAt = us?.CompletedAt,
                };
            })
            .ToList();

        // Calculate actual progress percentage from completed steps
        var totalSteps = stepSummaries.Count;
        var completedCount = stepSummaries.Count(s => s.Status == "completed");
        var progressPct = totalSteps > 0 ? Math.Round((double)completedCount / totalSteps * 100, 1) : 0;

        if (Math.Abs(userOnboarding.ProgressPercentage - progressPct) > 0.01)
        {
            userOnboarding.ProgressPercentage = progressPct;
            await _db.SaveChangesAsync(ct);
        }

        var relatedModules = repo.Modules
            .Select(m => new RepositoryModuleDto
            {
                Id          = m.Id,
                Name        = m.Name,
                Path        = m.Path,
                ModuleType  = m.ModuleType,
                Description = m.Description,
            })
            .ToList();

        var relevantFiles = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repositoryId && !f.IsDirectory)
            .OrderBy(f => f.FilePath)
            .Take(30)
            .Select(f => new RepositoryFileDto
            {
                Id          = f.Id,
                FilePath    = f.FilePath,
                FileName    = f.FileName,
                Extension   = f.Extension,
                Language    = f.Language,
                FileSize    = f.FileSize,
                IsDirectory = f.IsDirectory,
            })
            .ToListAsync(ct);

        return new OnboardingPathDto
        {
            Id                 = path.Id,
            RepositoryId       = repositoryId,
            Role               = path.Role,
            Title              = path.Title,
            Description        = path.Description,
            UserOnboardingId   = userOnboarding.Id,
            ProgressPercentage = progressPct,
            Steps              = stepSummaries,
            RelatedModules     = relatedModules,
            RelevantFiles      = relevantFiles,
        };
    }

    // ── Get step detail with grounded explanations and files ─────────────────
    public async Task<OnboardingStepDetailDto?> GetStepDetailAsync(
        int userOnboardingId, int stepId, CancellationToken ct = default, int? userId = null)
    {
        var userOnboarding = await _db.UserOnboardings
            .FirstOrDefaultAsync(u => u.Id == userOnboardingId, ct);

        if (userOnboarding == null) return null;

        if (userId.HasValue && userOnboarding.UserId.HasValue && userOnboarding.UserId.Value != userId.Value)
            throw new UnauthorizedAccessException("Forbidden: You do not have permission to access another user's onboarding progress.");

        var userStep = await _db.UserOnboardingSteps
            .Include(us => us.OnboardingStep)
            .ThenInclude(s => s.OnboardingPath)
            .FirstOrDefaultAsync(us => us.UserOnboardingId == userOnboardingId && us.OnboardingStepId == stepId, ct);

        if (userStep == null) return null;

        var step = userStep.OnboardingStep;
        var repoId = step.OnboardingPath.RepositoryId;

        RepositoryModule? module = null;
        if (step.ModuleId.HasValue)
        {
            module = await _db.RepositoryModules.FindAsync(new object[] { step.ModuleId.Value }, ct);
        }

        // Retrieve relevant files for this step/module
        List<RepositoryFile> files;
        if (module != null)
        {
            files = await _db.RepositoryFiles
                .Where(f => f.RepositoryId == repoId && !f.IsDirectory && f.FilePath.StartsWith(module.Path))
                .OrderBy(f => f.FilePath)
                .Take(15)
                .ToListAsync(ct);
        }
        else
        {
            // Overview or task step: pick top entry point files
            files = await _db.RepositoryFiles
                .Where(f => f.RepositoryId == repoId && !f.IsDirectory &&
                    (f.FileName.Contains("Program") || f.FileName.Contains("App") ||
                     f.FileName.EndsWith(".csproj") || f.FileName.EndsWith(".json") || f.FileName.Equals("README.md")))
                .Take(10)
                .ToListAsync(ct);
        }

        // Retrieve dependencies connected to these files
        var fileIds = files.Select(f => f.Id).ToHashSet();
        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == repoId && (fileIds.Contains(d.SourceFileId) || fileIds.Contains(d.TargetFileId)))
            .Include(d => d.SourceFile)
            .Include(d => d.TargetFile)
            .Take(10)
            .Select(d => new RepositoryDependencyDto
            {
                Id              = d.Id,
                SourceFilePath  = d.SourceFile.FilePath,
                TargetFilePath  = d.TargetFile.FilePath,
                DependencyType  = d.DependencyType,
                ImportStatement = d.ImportStatement,
            })
            .ToListAsync(ct);

        // Learning content
        var whatToLearn = BuildWhatYouWillLearn(step, module, files);
        var whyItMatters = BuildWhyItMatters(step, module);

        // AI explanation
        var aiExplanation = await GenerateStepExplanationAsync(step, module, files, deps, ct);

        return new OnboardingStepDetailDto
        {
            StepId           = step.Id,
            UserOnboardingId = userOnboardingId,
            Title            = step.Title,
            WhatYouWillLearn = whatToLearn,
            WhyItMatters     = whyItMatters,
            StepType         = step.StepType,
            RelatedModule    = module != null ? new RepositoryModuleDto
            {
                Id          = module.Id,
                Name        = module.Name,
                Path        = module.Path,
                ModuleType  = module.ModuleType,
                Description = module.Description,
            } : null,
            RelevantFiles    = files.Select(f => new RepositoryFileDto
            {
                Id          = f.Id,
                FilePath    = f.FilePath,
                FileName    = f.FileName,
                Extension   = f.Extension,
                Language    = f.Language,
                FileSize    = f.FileSize,
                IsDirectory = f.IsDirectory,
            }).ToList(),
            Dependencies     = deps,
            AiExplanation    = aiExplanation,
            Status           = userStep.Status,
            CompletedAt      = userStep.CompletedAt,
        };
    }

    // ── Complete step and recalculate dynamic progress ──────────────────────
    public async Task<CompleteStepResponseDto> CompleteStepAsync(
        int userOnboardingId, int stepId, CancellationToken ct = default, int? userId = null)
    {
        var userOnboarding = await _db.UserOnboardings
            .FirstOrDefaultAsync(u => u.Id == userOnboardingId, ct);

        if (userOnboarding == null)
            throw new InvalidOperationException($"User onboarding session {userOnboardingId} not found.");

        if (userId.HasValue && userOnboarding.UserId.HasValue && userOnboarding.UserId.Value != userId.Value)
            throw new UnauthorizedAccessException("Forbidden: You do not have permission to modify another user's onboarding progress.");

        var userStep = await _db.UserOnboardingSteps
            .FirstOrDefaultAsync(us => us.UserOnboardingId == userOnboardingId && us.OnboardingStepId == stepId, ct);

        if (userStep == null)
            throw new InvalidOperationException($"Step {stepId} not found for onboarding session {userOnboardingId}.");

        userStep.Status      = "completed";
        userStep.CompletedAt = DateTime.UtcNow;

        var allSteps = await _db.UserOnboardingSteps
            .Where(us => us.UserOnboardingId == userOnboardingId)
            .ToListAsync(ct);

        var total = allSteps.Count;
        var completed = allSteps.Count(us => us.Status == "completed");
        var progressPct = total > 0 ? Math.Round((double)completed / total * 100, 1) : 0;

        userOnboarding.ProgressPercentage = progressPct;
        if (completed == total)
        {
            userOnboarding.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return new CompleteStepResponseDto
        {
            UserOnboardingId   = userOnboardingId,
            StepId             = stepId,
            Status             = "completed",
            CompletedAt        = userStep.CompletedAt.Value,
            ProgressPercentage = progressPct,
            CompletedSteps     = completed,
            TotalSteps         = total,
            IsFinished         = completed == total,
        };
    }

    // ── Dynamic Path Generator using actual repository intelligence ──────────
    private async Task<OnboardingPath> GeneratePathAsync(
        Repository repo, string role, CancellationToken ct)
    {
        var modules = repo.Modules.ToList();
        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repo.Id && !f.IsDirectory)
            .ToListAsync(ct);

        var title = $"{role} Onboarding Guide for {repo.Name}";
        var desc = $"Structured learning path tailored for a {role} joining the {repo.Name} codebase.";

        var path = new OnboardingPath
        {
            RepositoryId = repo.Id,
            Role         = role,
            Title        = title,
            Description  = desc,
            CreatedAt    = DateTime.UtcNow,
        };

        _db.OnboardingPaths.Add(path);
        await _db.SaveChangesAsync(ct);

        var steps = new List<OnboardingStep>();
        int order = 1;

        // Step 1: Project structure overview (common to all roles)
        steps.Add(new OnboardingStep
        {
            OnboardingPathId = path.Id,
            Title            = "Project Structure & Solution Architecture",
            Description      = $"Explore repository organization, high-level architecture, build configuration, and core entry points for {repo.Name}.",
            Order            = order++,
            ModuleId         = null,
            StepType         = "overview",
            CreatedAt        = DateTime.UtcNow,
        });

        // Generate role-specific steps by matching actual detected modules
        switch (role)
        {
            case "Backend Developer":
                AddBackendSteps(path.Id, modules, files, ref order, steps);
                break;

            case "Frontend Developer":
                AddFrontendSteps(path.Id, modules, files, ref order, steps);
                break;

            case "Full Stack Developer":
                AddFullStackSteps(path.Id, modules, files, ref order, steps);
                break;

            case "QA Engineer":
                AddQASteps(path.Id, modules, files, ref order, steps);
                break;

            default:
                AddBackendSteps(path.Id, modules, files, ref order, steps);
                break;
        }

        // Final Step: First Contribution Preparation
        steps.Add(new OnboardingStep
        {
            OnboardingPathId = path.Id,
            Title            = "First Contribution & Starter Task Preparation",
            Description      = "Review contribution guidelines, inspect beginner starter tasks, analyze dependency impact, and get ready for your first PR.",
            Order            = order++,
            ModuleId         = null,
            StepType         = "task",
            CreatedAt        = DateTime.UtcNow,
        });

        _db.OnboardingSteps.AddRange(steps);
        await _db.SaveChangesAsync(ct);

        path.Steps = steps;
        return path;
    }

    private static void AddBackendSteps(
        int pathId, List<RepositoryModule> modules, List<RepositoryFile> files,
        ref int order, List<OnboardingStep> steps)
    {
        // 1. API / Server / Controllers (prefer Backend/API/Server, exclude Migrations/Hooks)
        var apiMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Backend" || m.ModuleType == "API" || m.ModuleType == "Controllers" ||
            m.Name.Equals("Server", StringComparison.OrdinalIgnoreCase)) ??
            modules.FirstOrDefault(m =>
            (m.Path.Contains("server", StringComparison.OrdinalIgnoreCase) ||
             m.Path.Contains("api", StringComparison.OrdinalIgnoreCase)) &&
            m.ModuleType != "Migrations" && m.ModuleType != "Hooks");

        if (apiMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"API Layer & Controllers ({apiMod.Name})",
                Description      = $"Understand endpoint routing, request pipelines, and HTTP interfaces defined in {apiMod.Path}.",
                Order            = order++,
                ModuleId         = apiMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 2. Authentication & Authorization (module or files)
        var authMod = modules.FirstOrDefault(m =>
            (m.Name.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
             m.Path.Contains("auth", StringComparison.OrdinalIgnoreCase)) &&
            m.ModuleType != "Hooks");
        var hasAuthFiles = files.Any(f => f.FilePath.Contains("auth", StringComparison.OrdinalIgnoreCase));
        if (authMod != null || hasAuthFiles)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = "Authentication & Security Architecture",
                Description      = "Learn how user identity, token verification, cookies, and endpoint authorization are implemented.",
                Order            = order++,
                ModuleId         = authMod?.Id ?? apiMod?.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 3. Business Logic & Services
        var srvMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Services" || m.ModuleType == "Core" || m.ModuleType == "Business Logic");
        if (srvMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Business Logic & Services ({srvMod.Name})",
                Description      = $"Explore core domain services, workflows, and business rules in {srvMod.Path}.",
                Order            = order++,
                ModuleId         = srvMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 4. Data Access & Persistence
        var dataMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Data Access" || m.ModuleType == "Repositories" || m.ModuleType == "Data");
        if (dataMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Data Access & Repository Layer ({dataMod.Name})",
                Description      = $"Review database contexts, queries, and entity relationships in {dataMod.Path}.",
                Order            = order++,
                ModuleId         = dataMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 5. Migrations & Schema
        var migMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Migrations" || m.Path.Contains("migrations", StringComparison.OrdinalIgnoreCase));
        if (migMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = "Database Migrations & Entity Schema",
                Description      = $"Learn how database migrations, schema history, and table snapshots are managed in {migMod.Path}.",
                Order            = order++,
                ModuleId         = migMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 6. Tests & Validation
        var testMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Tests" || m.Path.Contains("test", StringComparison.OrdinalIgnoreCase));
        if (testMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Automated Testing & Quality ({testMod.Name})",
                Description      = $"Run and understand unit/integration tests, test harnesses, and fixtures in {testMod.Path}.",
                Order            = order++,
                ModuleId         = testMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }
    }

    private static void AddFrontendSteps(
        int pathId, List<RepositoryModule> modules, List<RepositoryFile> files,
        ref int order, List<OnboardingStep> steps)
    {
        // 1. Frontend Core / Client Shell
        var feMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Frontend" || m.Path.Contains("client", StringComparison.OrdinalIgnoreCase) ||
            m.Path.Contains("frontend", StringComparison.OrdinalIgnoreCase) ||
            m.Path.Contains("web", StringComparison.OrdinalIgnoreCase));
        if (feMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Frontend Application Shell ({feMod.Name})",
                Description      = $"Understand application entry points, client bootstrapping, and layout wrappers in {feMod.Path}.",
                Order            = order++,
                ModuleId         = feMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 2. UI Components
        var compMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Components" || m.ModuleType == "Views" || m.ModuleType == "Pages" ||
            m.Path.Contains("components", StringComparison.OrdinalIgnoreCase));
        if (compMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"UI Components & Pages ({compMod.Name})",
                Description      = $"Explore reusable UI components, styling patterns, and page layouts in {compMod.Path}.",
                Order            = order++,
                ModuleId         = compMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 3. Shared Models & Utilities
        var sharedMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Shared" || m.ModuleType == "Utilities" ||
            m.Path.Contains("shared", StringComparison.OrdinalIgnoreCase));
        if (sharedMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Shared Models & Client Contracts ({sharedMod.Name})",
                Description      = $"Learn about shared types, DTO contracts, and client-server models in {sharedMod.Path}.",
                Order            = order++,
                ModuleId         = sharedMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // 4. Client API Integration
        var hasClientApi = files.Any(f => f.FilePath.Contains("Client", StringComparison.OrdinalIgnoreCase) &&
                                          (f.FileName.Contains("Client") || f.FileName.Contains("Api")));
        if (hasClientApi && feMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = "Client API Integration & State",
                Description      = "Understand how the frontend communicates with backend APIs, manages credentials, and binds data.",
                Order            = order++,
                ModuleId         = feMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }
    }

    private static void AddFullStackSteps(
        int pathId, List<RepositoryModule> modules, List<RepositoryFile> files,
        ref int order, List<OnboardingStep> steps)
    {
        // Backend API
        var apiMod = modules.FirstOrDefault(m =>
            m.ModuleType == "API" || m.ModuleType == "Backend" ||
            m.Path.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            m.Path.Contains("api", StringComparison.OrdinalIgnoreCase));
        if (apiMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Backend Services & API Contracts ({apiMod.Name})",
                Description      = $"Inspect API endpoints, controllers, and service contracts in {apiMod.Path}.",
                Order            = order++,
                ModuleId         = apiMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // Data / Migrations
        var dataMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Data Access" || m.ModuleType == "Migrations" ||
            m.Path.Contains("data", StringComparison.OrdinalIgnoreCase));
        if (dataMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Data Persistence & Schema ({dataMod.Name})",
                Description      = $"Review database access patterns and entity models in {dataMod.Path}.",
                Order            = order++,
                ModuleId         = dataMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // Frontend Client
        var feMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Frontend" || m.Path.Contains("client", StringComparison.OrdinalIgnoreCase));
        if (feMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Frontend Application & UI Layer ({feMod.Name})",
                Description      = $"Examine client component hierarchies and state management in {feMod.Path}.",
                Order            = order++,
                ModuleId         = feMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // Shared contracts
        var sharedMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Shared" || m.Path.Contains("shared", StringComparison.OrdinalIgnoreCase));
        if (sharedMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Full-Stack Contracts & Shared Types ({sharedMod.Name})",
                Description      = $"Learn how shared DTOs bridge the backend and frontend in {sharedMod.Path}.",
                Order            = order++,
                ModuleId         = sharedMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }
    }

    private static void AddQASteps(
        int pathId, List<RepositoryModule> modules, List<RepositoryFile> files,
        ref int order, List<OnboardingStep> steps)
    {
        // Automated Test Suites
        var testMod = modules.FirstOrDefault(m =>
            m.ModuleType == "Tests" || m.Path.Contains("test", StringComparison.OrdinalIgnoreCase));
        if (testMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"Automated Test Suites ({testMod.Name})",
                Description      = $"Review unit and integration tests, assertions, and test runners in {testMod.Path}.",
                Order            = order++,
                ModuleId         = testMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // API Endpoint Surface
        var apiMod = modules.FirstOrDefault(m =>
            m.ModuleType == "API" || m.ModuleType == "Controllers" || m.ModuleType == "Backend");
        if (apiMod != null)
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = $"API Surface & Contract Validation ({apiMod.Name})",
                Description      = $"Understand request payloads, response status codes, and error responses in {apiMod.Path}.",
                Order            = order++,
                ModuleId         = apiMod.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }

        // Auth & Security test cases
        if (files.Any(f => f.FilePath.Contains("auth", StringComparison.OrdinalIgnoreCase)))
        {
            steps.Add(new OnboardingStep
            {
                OnboardingPathId = pathId,
                Title            = "Security & Authentication Validation",
                Description      = "Verify authentication workflows, unauthorized response codes, and permission handling.",
                Order            = order++,
                ModuleId         = apiMod?.Id,
                StepType         = "module",
                CreatedAt        = DateTime.UtcNow,
            });
        }
    }

    // ── Helper builders ─────────────────────────────────────────────────────
    private static string NormalizeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return "Backend Developer";
        var r = role.Trim();
        if (r.Contains("Front", StringComparison.OrdinalIgnoreCase)) return "Frontend Developer";
        if (r.Contains("Full", StringComparison.OrdinalIgnoreCase)) return "Full Stack Developer";
        if (r.Contains("QA", StringComparison.OrdinalIgnoreCase) || r.Contains("Test", StringComparison.OrdinalIgnoreCase)) return "QA Engineer";
        return "Backend Developer";
    }

    private static string MapLayer(string moduleType) => moduleType switch
    {
        "Frontend" or "Components" or "Pages" or "Views" => "Presentation",
        "API" or "Controllers" => "API",
        "Backend" or "Server" => "Backend",
        "Services" or "Core" or "Business Logic" => "Business Logic",
        "Data Access" or "Repositories" or "Data" or "Migrations" => "Data Access",
        "Shared" => "Shared",
        "Tests" => "Tests",
        _ => moduleType,
    };

    private static string BuildWhatYouWillLearn(OnboardingStep step, RepositoryModule? mod, List<RepositoryFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- Understand the purpose and architectural role of `{step.Title}`.");
        if (mod != null)
        {
            sb.AppendLine($"- Navigate key files and directories located in `{mod.Path}`.");
            sb.AppendLine($"- Learn conventions for {mod.ModuleType} implementation in this repository.");
        }
        if (files.Count > 0)
        {
            sb.AppendLine($"- Review {files.Count} key files, including {string.Join(", ", files.Take(3).Select(f => $"`{f.FileName}`"))}.");
        }
        sb.AppendLine("- Identify dependencies and callers that rely on this part of the codebase.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildWhyItMatters(OnboardingStep step, RepositoryModule? mod)
    {
        if (step.StepType == "overview")
            return "Understanding the high-level solution architecture and entry points provides the foundation for navigating all subsequent modules without getting lost.";
        if (step.StepType == "task")
            return "Preparing for your first contribution builds confidence by applying existing code patterns with minimal risk to existing functionality.";

        if (mod == null)
            return "This component represents a critical milestone in understanding system workflows and developer practices in this codebase.";

        return mod.ModuleType switch
        {
            "API" or "Controllers" => "The API layer defines the public contract and routing for all client requests; mastering it is essential for developing or modifying endpoints.",
            "Frontend" or "Components" => "The presentation layer directly shapes user interaction; understanding component hierarchy prevents duplicate styling and state bugs.",
            "Data Access" or "Migrations" => "Data models and migrations govern persistence; changes here directly affect database integrity and query performance.",
            "Shared" => "Shared contracts prevent drift between layers and services; adhering to shared models ensures end-to-end consistency.",
            "Tests" => "Test suites document expected behavior and guard against regressions; learning how to run and write tests ensures safe contributions.",
            _ => $"Understanding `{mod.Name}` ensures your changes adhere to repository conventions and maintain system integrity.",
        };
    }

    private async Task<string> GenerateStepExplanationAsync(
        OnboardingStep step, RepositoryModule? mod, List<RepositoryFile> files,
        List<RepositoryDependencyDto> deps, CancellationToken ct)
    {
        if (!_watsonx.IsConfigured())
        {
            var sb = new StringBuilder();
            sb.AppendLine("**⚠ AI not configured.** Grounded repository context summary:\n");
            sb.AppendLine($"### {step.Title}\n");
            sb.AppendLine($"{step.Description}\n");

            if (mod != null)
            {
                sb.AppendLine($"**Module:** `{mod.Name}` [{mod.ModuleType}] at `{mod.Path}`\n");
            }

            if (files.Count > 0)
            {
                sb.AppendLine("**Key Files in this Step:**");
                foreach (var f in files.Take(6))
                {
                    sb.AppendLine($"- `{f.FilePath}` ({f.Language ?? "config"}, {f.FileSize} bytes)");
                }
                sb.AppendLine();
            }

            if (deps.Count > 0)
            {
                sb.AppendLine("**Module Dependencies:**");
                foreach (var d in deps.Take(5))
                {
                    sb.AppendLine($"- `{d.SourceFilePath}` → `{d.TargetFilePath}`");
                }
                sb.AppendLine();
            }

            sb.AppendLine("> **AI Configuration Status:** IBM watsonx is not configured. To enable live AI generation, configure `WatsonX__ApiKey` and `WatsonX__ProjectId` (and optionally `WatsonX__Url`, `WatsonX__ModelId`) as environment variables outside source control.");
            return sb.ToString();
        }

        try
        {
            var systemPrompt = """
You are CodeCompass, an expert developer onboarding mentor.
Explain the current onboarding learning step to a new developer.
Rely strictly on the provided repository module and file context.
Do not invent files, methods, or modules. Keep your explanation concise, friendly, and practical.
""";
            var userPrompt = $"""
Onboarding Step: {step.Title}
Description: {step.Description}
Module: {mod?.Name ?? "General"} ({mod?.Path ?? "Root"})
Files: {string.Join(", ", files.Take(8).Select(f => f.FilePath))}

Please provide:
1. Beginner-friendly overview
2. What to inspect first
3. Key developer tips for this step
""";
            return await _watsonx.ChatAsync(systemPrompt, userPrompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "watsonx explanation failed for step {Id}", step.Id);
            return $"AI service error: {ex.Message}\n\n**Module:** `{mod?.Name}` ({mod?.Path}) with {files.Count} files.";
        }
    }
}
