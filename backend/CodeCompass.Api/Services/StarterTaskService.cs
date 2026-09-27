using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeCompass.Api.Services;

public class StarterTaskService : IStarterTaskService
{
    private readonly CodeCompassDbContext _db;
    private readonly IContextRetrievalService _context;
    private readonly ILogger<StarterTaskService> _logger;

    public StarterTaskService(
        CodeCompassDbContext db,
        IContextRetrievalService context,
        ILogger<StarterTaskService> logger)
    {
        _db      = db;
        _context = context;
        _logger  = logger;
    }

    public async Task<List<StarterTaskSummaryDto>> GetStarterTasksAsync(
        int repositoryId, CancellationToken ct = default)
    {
        var repo = await _db.Repositories.FindAsync(new object[] { repositoryId }, ct);
        if (repo == null)
            throw new InvalidOperationException($"Repository {repositoryId} not found.");

        var tasks = await _db.StarterTasks
            .Include(t => t.RelatedModule)
            .Where(t => t.RepositoryId == repositoryId)
            .ToListAsync(ct);

        if (tasks.Count == 0)
        {
            tasks = await GenerateStarterTasksAsync(repo, ct);
        }

        var results = new List<StarterTaskSummaryDto>();
        foreach (var t in tasks)
        {
            var fileCount = 0;
            if (t.RelatedModuleId.HasValue && t.RelatedModule != null)
            {
                fileCount = await _db.RepositoryFiles
                    .CountAsync(f => f.RepositoryId == repositoryId && !f.IsDirectory &&
                                     f.FilePath.StartsWith(t.RelatedModule.Path), ct);
            }
            if (fileCount == 0) fileCount = 3;

            results.Add(new StarterTaskSummaryDto
            {
                Id                = t.Id,
                RepositoryId      = t.RepositoryId,
                Title             = t.Title,
                Description       = t.Description,
                Difficulty        = t.Difficulty,
                Reason            = t.Reason,
                RelatedModuleId   = t.RelatedModuleId,
                RelatedModuleName = t.RelatedModule?.Name ?? "General",
                FileCount         = fileCount,
            });
        }

        return results;
    }

    public async Task<StarterTaskDetailDto?> GetStarterTaskDetailAsync(
        int taskId, CancellationToken ct = default)
    {
        var task = await _db.StarterTasks
            .Include(t => t.RelatedModule)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task == null) return null;

        var repoId = task.RepositoryId;

        // Retrieve relevant files
        List<RepositoryFile> files;
        if (task.RelatedModule != null)
        {
            files = await _db.RepositoryFiles
                .Where(f => f.RepositoryId == repoId && !f.IsDirectory &&
                            f.FilePath.StartsWith(task.RelatedModule.Path))
                .OrderBy(f => f.FilePath)
                .Take(10)
                .ToListAsync(ct);
        }
        else
        {
            files = await _db.RepositoryFiles
                .Where(f => f.RepositoryId == repoId && !f.IsDirectory &&
                            (f.FileName.Contains("User") || f.FileName.Contains("Todo") || f.FileName.Contains("Test")))
                .Take(8)
                .ToListAsync(ct);
        }

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

        // Run Phase 4 Change Impact analysis using existing ContextRetrievalService
        var impactQuery = $"{task.Title} {task.Description}";
        var ctx = await _context.RetrieveAsync(repoId, impactQuery, ct);

        var changeImpact = ctx.Files.Take(10).Select(f => new ChangeImpactItemDto
        {
            FilePath   = f.FilePath,
            ModuleName = ctx.Modules.FirstOrDefault(m => f.FilePath.StartsWith(m.Path, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty,
            Impact     = f.RelevanceScore >= 3.0 ? "direct" : f.RelevanceScore >= 1.5 ? "potential" : "needs-verification",
            Reason     = f.RelevanceReason,
        })
        .OrderBy(x => x.Impact == "direct" ? 0 : x.Impact == "potential" ? 1 : 2)
        .ToList();

        var suggestedFirstStep = BuildSuggestedFirstStep(task, files);
        var existingPattern = BuildExistingPattern(task, files);

        return new StarterTaskDetailDto
        {
            Id                       = task.Id,
            RepositoryId             = task.RepositoryId,
            Title                    = task.Title,
            Description              = task.Description,
            Difficulty               = task.Difficulty,
            ReasonForRecommendation  = task.Reason,
            RelatedModule            = task.RelatedModule != null ? new RepositoryModuleDto
            {
                Id          = task.RelatedModule.Id,
                Name        = task.RelatedModule.Name,
                Path        = task.RelatedModule.Path,
                ModuleType  = task.RelatedModule.ModuleType,
                Description = task.RelatedModule.Description,
            } : null,
            RelevantFiles            = files.Select(f => new RepositoryFileDto
            {
                Id          = f.Id,
                FilePath    = f.FilePath,
                FileName    = f.FileName,
                Extension   = f.Extension,
                Language    = f.Language,
                FileSize    = f.FileSize,
                IsDirectory = f.IsDirectory,
            }).ToList(),
            KnownDependencies        = deps,
            SuggestedFirstStep       = suggestedFirstStep,
            ExistingPattern          = existingPattern,
            ChangeImpact             = changeImpact,
        };
    }

    // ── Generate starter tasks dynamically from repository structure ─────────
    private async Task<List<StarterTask>> GenerateStarterTasksAsync(
        Repository repo, CancellationToken ct)
    {
        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == repo.Id)
            .ToListAsync(ct);

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repo.Id && !f.IsDirectory)
            .ToListAsync(ct);

        var tasks = new List<StarterTask>();

        // 1. Validation / DTO Task (if models or DTOs exist)
        var userOrTodoFile = files.FirstOrDefault(f =>
            f.FileName.Contains("User", StringComparison.OrdinalIgnoreCase) ||
            f.FileName.Contains("Todo", StringComparison.OrdinalIgnoreCase) ||
            f.FilePath.Contains("DTO", StringComparison.OrdinalIgnoreCase) ||
            f.FilePath.Contains("Model", StringComparison.OrdinalIgnoreCase));

        var modelModule = modules.FirstOrDefault(m =>
            m.ModuleType == "Models" || m.ModuleType == "DTOs" ||
            (userOrTodoFile != null && userOrTodoFile.FilePath.StartsWith(m.Path)));

        if (userOrTodoFile != null)
        {
            tasks.Add(new StarterTask
            {
                RepositoryId    = repo.Id,
                Title           = $"Add validation attribute to {Path.GetFileNameWithoutExtension(userOrTodoFile.FileName)} model",
                Description     = $"Introduce input validation (such as [MaxLength], [RegularExpression], or custom range check) to `{userOrTodoFile.FileName}` following the project's existing validation patterns.",
                Difficulty      = "Easy",
                Reason          = "Low-risk change that introduces the project's existing validation pattern without altering database schema or breaking consumer dependencies.",
                RelatedModuleId = modelModule?.Id,
                CreatedAt       = DateTime.UtcNow,
            });
        }

        // 2. Unit Testing Task (if test files exist)
        var testFile = files.FirstOrDefault(f =>
            f.FileName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            f.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase));

        var testModule = modules.FirstOrDefault(m =>
            m.ModuleType == "Tests" || (testFile != null && testFile.FilePath.StartsWith(m.Path)));

        if (testFile != null)
        {
            tasks.Add(new StarterTask
            {
                RepositoryId    = repo.Id,
                Title           = "Add unit test for edge-case validation",
                Description     = $"Add an additional unit or integration test in `{testFile.FileName}` asserting expected status codes when invalid or boundary inputs are supplied.",
                Difficulty      = "Easy",
                Reason          = "Safe, isolated test addition that helps you master the test harness and assertion conventions without any risk to production code.",
                RelatedModuleId = testModule?.Id,
                CreatedAt       = DateTime.UtcNow,
            });
        }

        // 3. UI Component / Presentation Task (if UI exists)
        var compFile = files.FirstOrDefault(f =>
            f.Extension.Equals(".razor", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
            f.FilePath.Contains("Component", StringComparison.OrdinalIgnoreCase));

        var compModule = modules.FirstOrDefault(m =>
            m.ModuleType == "Components" || m.ModuleType == "Frontend" ||
            (compFile != null && compFile.FilePath.StartsWith(m.Path)));

        if (compFile != null)
        {
            tasks.Add(new StarterTask
            {
                RepositoryId    = repo.Id,
                Title           = $"Improve empty-state feedback in `{compFile.FileName}`",
                Description     = $"Add or enhance empty-state UI feedback when list or form data is null or empty in `{compFile.FileName}`.",
                Difficulty      = "Easy",
                Reason          = "Visual enhancement localized to a single component without modifying backend APIs or changing database schemas.",
                RelatedModuleId = compModule?.Id,
                CreatedAt       = DateTime.UtcNow,
            });
        }

        // 4. Shared Utilities / Helpers Task (if shared module exists)
        var sharedModule = modules.FirstOrDefault(m =>
            m.ModuleType == "Shared" || m.ModuleType == "Utilities" ||
            m.Name.Contains("Shared", StringComparison.OrdinalIgnoreCase));

        if (sharedModule != null)
        {
            tasks.Add(new StarterTask
            {
                RepositoryId    = repo.Id,
                Title           = $"Add formatting helper method in `{sharedModule.Name}`",
                Description     = "Implement a reusable string or date formatting extension method in the shared library following existing helper conventions.",
                Difficulty      = "Easy",
                Reason          = "Isolated utility enhancement with zero side-effects on existing business logic.",
                RelatedModuleId = sharedModule.Id,
                CreatedAt       = DateTime.UtcNow,
            });
        }

        // Fallback if no specific patterns identified
        if (tasks.Count == 0)
        {
            tasks.Add(new StarterTask
            {
                RepositoryId    = repo.Id,
                Title           = "Review and update project README documentation",
                Description     = "Verify local setup prerequisites and update build or run instructions in README.md based on current repository state.",
                Difficulty      = "Easy",
                Reason          = "Zero risk contribution that improves onboarding experience for future developers.",
                RelatedModuleId = null,
                CreatedAt       = DateTime.UtcNow,
            });
        }

        _db.StarterTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        return tasks;
    }

    private static string BuildSuggestedFirstStep(StarterTask task, List<RepositoryFile> files)
    {
        var targetFile = files.FirstOrDefault()?.FileName ?? "the relevant file";
        return $"1. Locate `{targetFile}` and inspect the existing class definition and imports.\n" +
               "2. Check existing usage in test files to see how this component is exercised.\n" +
               "3. Make your localized change and verify build succeeds locally.";
    }

    private static string BuildExistingPattern(StarterTask task, List<RepositoryFile> files)
    {
        var first = files.FirstOrDefault();
        if (first == null) return "Follow existing code style and formatting in the repository.";

        return $"Inspect `{first.FilePath}` for existing annotations, error handling conventions, and parameter validation patterns.";
    }
}
