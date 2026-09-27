using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Services;

namespace CodeCompass.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RepositoriesController : ControllerBase
{
    private readonly CodeCompassDbContext _db;
    private readonly IRepositoryAnalyzer _analyzer;
    private readonly ILogger<RepositoriesController> _logger;

    public RepositoriesController(
        CodeCompassDbContext db,
        IRepositoryAnalyzer analyzer,
        ILogger<RepositoriesController> logger)
    {
        _db = db;
        _analyzer = analyzer;
        _logger = logger;
    }

    // ── GET /api/repositories ──────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RepositoryDto>>> GetAll()
    {
        var repos = await _db.Repositories
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RepositoryDto
            {
                Id              = r.Id,
                Name            = r.Name,
                GitUrl          = r.GitUrl,
                Description     = r.Description,
                PrimaryLanguage = r.PrimaryLanguage,
                CreatedAt       = r.CreatedAt
            })
            .ToListAsync();

        return Ok(repos);
    }

    // ── POST /api/repositories/analyze ────────────────────────────────────
    [HttpPost("analyze")]
    public async Task<ActionResult<AnalysisSummaryDto>> Analyze(
        [FromBody] AnalyzeRequestDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GitUrl))
            return BadRequest(new { error = "gitUrl is required." });

        try
        {
            var summary = await _analyzer.AnalyzeAsync(request.GitUrl, ct);
            return Ok(summary);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Analysis failed for {Url}", request.GitUrl);
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "Request was cancelled." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error analysing {Url}", request.GitUrl);
            return StatusCode(500, new { error = "An unexpected error occurred during analysis." });
        }
    }

    // ── GET /api/repositories/{id}/analysis ───────────────────────────────
    [HttpGet("{id:int}/analysis")]
    public async Task<ActionResult<RepositoryAnalysisDto>> GetAnalysis(int id)
    {
        var repo = await _db.Repositories.FindAsync(id);
        if (repo == null) return NotFound(new { error = $"Repository {id} not found." });

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == id)
            .ToListAsync();

        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == id)
            .CountAsync();

        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == id)
            .CountAsync();

        // Language stats (files only)
        var langGroups = files
            .Where(f => !f.IsDirectory && !string.IsNullOrEmpty(f.Language))
            .GroupBy(f => f.Language)
            .Select(g => new { Language = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var totalLangFiles = langGroups.Sum(g => g.Count);
        var langStats = langGroups.Select(g => new LanguageStatDto
        {
            Language   = g.Language,
            FileCount  = g.Count,
            Percentage = totalLangFiles > 0 ? Math.Round((double)g.Count / totalLangFiles * 100, 1) : 0
        }).ToList();

        return Ok(new RepositoryAnalysisDto
        {
            Id               = repo.Id,
            Name             = repo.Name,
            GitUrl           = repo.GitUrl,
            Description      = repo.Description,
            PrimaryLanguage  = repo.PrimaryLanguage,
            CreatedAt        = repo.CreatedAt,
            AnalyzedAt       = repo.AnalyzedAt,
            TotalFiles       = files.Count(f => !f.IsDirectory),
            TotalDirectories = files.Count(f => f.IsDirectory),
            TotalModules     = modules,
            TotalDependencies = deps,
            LanguageStats    = langStats
        });
    }

    // ── GET /api/repositories/{id}/files ──────────────────────────────────
    [HttpGet("{id:int}/files")]
    public async Task<ActionResult<IEnumerable<RepositoryFileDto>>> GetFiles(int id)
    {
        if (!await _db.Repositories.AnyAsync(r => r.Id == id))
            return NotFound(new { error = $"Repository {id} not found." });

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == id)
            .OrderBy(f => f.FilePath)
            .Select(f => new RepositoryFileDto
            {
                Id          = f.Id,
                FilePath    = f.FilePath,
                FileName    = f.FileName,
                Extension   = f.Extension,
                Language    = f.Language,
                FileSize    = f.FileSize,
                IsDirectory = f.IsDirectory
            })
            .ToListAsync();

        return Ok(files);
    }

    // ── GET /api/repositories/{id}/modules ────────────────────────────────
    [HttpGet("{id:int}/modules")]
    public async Task<ActionResult<IEnumerable<RepositoryModuleDto>>> GetModules(int id)
    {
        if (!await _db.Repositories.AnyAsync(r => r.Id == id))
            return NotFound(new { error = $"Repository {id} not found." });

        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == id)
            .OrderBy(m => m.Path)
            .Select(m => new RepositoryModuleDto
            {
                Id          = m.Id,
                Name        = m.Name,
                Path        = m.Path,
                ModuleType  = m.ModuleType,
                Description = m.Description
            })
            .ToListAsync();

        return Ok(modules);
    }

    // ── GET /api/repositories/{id}/dependencies ───────────────────────────
    [HttpGet("{id:int}/dependencies")]
    public async Task<ActionResult<IEnumerable<RepositoryDependencyDto>>> GetDependencies(int id)
    {
        if (!await _db.Repositories.AnyAsync(r => r.Id == id))
            return NotFound(new { error = $"Repository {id} not found." });

        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == id)
            .Include(d => d.SourceFile)
            .Include(d => d.TargetFile)
            .Select(d => new RepositoryDependencyDto
            {
                Id               = d.Id,
                SourceFilePath   = d.SourceFile.FilePath,
                TargetFilePath   = d.TargetFile.FilePath,
                DependencyType   = d.DependencyType,
                ImportStatement  = d.ImportStatement
            })
            .ToListAsync();

        return Ok(deps);
    }
}
