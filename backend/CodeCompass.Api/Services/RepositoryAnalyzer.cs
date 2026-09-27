using System.Diagnostics;
using System.Text.RegularExpressions;
using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeCompass.Api.Services;

public class RepositoryAnalyzer : IRepositoryAnalyzer
{
    private readonly CodeCompassDbContext _db;
    private readonly ILogger<RepositoryAnalyzer> _logger;

    // ── Directories to skip entirely ──────────────────────────────────────
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", "coverage",
        ".vs", ".vscode", ".idea", "__pycache__", ".mypy_cache",
        "target", "out", ".gradle", ".nuget", "packages",
        "vendor", ".terraform", ".next", ".cache"
    };

    // ── Extensions to skip (binary / generated) ───────────────────────────
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".lib", ".a",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".svg", ".woff", ".woff2", ".ttf", ".eot",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".pdf", ".docx", ".xlsx", ".pptx",
        ".mp3", ".mp4", ".avi", ".mov",
        ".db", ".sqlite", ".mdf", ".ldf",
        ".lock", ".snap"
    };

    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    // ── Language map by extension ─────────────────────────────────────────
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".cs",    "C#" },
        { ".csx",   "C#" },
        { ".ts",    "TypeScript" },
        { ".tsx",   "TypeScript" },
        { ".js",    "JavaScript" },
        { ".jsx",   "JavaScript" },
        { ".mjs",   "JavaScript" },
        { ".cjs",   "JavaScript" },
        { ".py",    "Python" },
        { ".java",  "Java" },
        { ".kt",    "Kotlin" },
        { ".go",    "Go" },
        { ".rs",    "Rust" },
        { ".rb",    "Ruby" },
        { ".php",   "PHP" },
        { ".html",  "HTML" },
        { ".htm",   "HTML" },
        { ".css",   "CSS" },
        { ".scss",  "CSS" },
        { ".sass",  "CSS" },
        { ".less",  "CSS" },
        { ".sql",   "SQL" },
        { ".json",  "JSON" },
        { ".xml",   "XML" },
        { ".csproj","XML" },
        { ".props", "XML" },
        { ".targets","XML" },
        { ".yaml",  "YAML" },
        { ".yml",   "YAML" },
        { ".md",    "Markdown" },
        { ".mdx",   "Markdown" },
        { ".sh",    "Shell" },
        { ".bash",  "Shell" },
        { ".ps1",   "PowerShell" },
        { ".psm1",  "PowerShell" },
        { ".tf",    "Terraform" },
        { ".toml",  "TOML" },
        { ".ini",   "INI" },
        { ".env",   "ENV" },
        { ".txt",   "Text" },
        { ".gitignore", "Config" },
        { ".dockerfile", "Dockerfile" },
    };

    // ── Known module directory names and their types ──────────────────────
    private static readonly Dictionary<string, string> KnownModuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "src",          "Source Root" },
        { "source",       "Source Root" },
        { "lib",          "Library" },
        { "libs",         "Library" },
        { "frontend",     "Frontend" },
        { "backend",      "Backend" },
        { "client",       "Frontend" },
        { "server",       "Backend" },
        { "api",          "API" },
        { "web",          "Web" },
        { "app",          "Application" },
        { "core",         "Core" },
        { "common",       "Shared" },
        { "shared",       "Shared" },
        { "utils",        "Utilities" },
        { "helpers",      "Utilities" },
        { "controllers",  "Controllers" },
        { "controller",   "Controllers" },
        { "services",     "Services" },
        { "service",      "Services" },
        { "models",       "Models" },
        { "model",        "Models" },
        { "entities",     "Models" },
        { "repositories", "Repositories" },
        { "repository",   "Repositories" },
        { "data",         "Data" },
        { "database",     "Data" },
        { "migrations",   "Migrations" },
        { "dtos",         "DTOs" },
        { "dto",          "DTOs" },
        { "components",   "Components" },
        { "component",    "Components" },
        { "pages",        "Pages" },
        { "views",        "Views" },
        { "view",         "Views" },
        { "hooks",        "Hooks" },
        { "middleware",   "Middleware" },
        { "tests",        "Tests" },
        { "test",         "Tests" },
        { "specs",        "Tests" },
        { "__tests__",    "Tests" },
        { "infrastructure","Infrastructure" },
        { "config",       "Configuration" },
        { "configs",      "Configuration" },
        { "scripts",      "Scripts" },
        { "docs",         "Documentation" },
        { "documentation","Documentation" },
    };

    public RepositoryAnalyzer(CodeCompassDbContext db, ILogger<RepositoryAnalyzer> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Entry point ────────────────────────────────────────────────────────
    public async Task<AnalysisSummaryDto> AnalyzeAsync(string gitUrl, CancellationToken ct = default)
    {
        ValidateGitHubUrl(gitUrl);

        var repoName = ExtractRepoName(gitUrl);
        var tempDir  = Path.Combine(Path.GetTempPath(), "codecompass_clones", Guid.NewGuid().ToString("N"));

        try
        {
            _logger.LogInformation("Cloning {Url} into {Dir}", gitUrl, tempDir);
            await CloneRepositoryAsync(gitUrl, tempDir, ct);
            _logger.LogInformation("Clone complete for {Name}", repoName);

            return await RunAnalysisAsync(gitUrl, repoName, tempDir, ct);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    // ── URL validation ─────────────────────────────────────────────────────
    private static void ValidateGitHubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Repository URL must not be empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Repository URL is not a valid URI.");

        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only HTTPS GitHub URLs are supported.");

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only github.com repositories are supported in this phase.");

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[0]) || string.IsNullOrWhiteSpace(segments[1]))
            throw new ArgumentException("URL must be in the format https://github.com/owner/repository.");

        // Reject path traversal
        if (url.Contains("..") || url.Contains(';') || url.Contains('&'))
            throw new ArgumentException("URL contains disallowed characters.");
    }

    private static string ExtractRepoName(string url)
    {
        var path = new Uri(url).AbsolutePath.Trim('/');
        var parts = path.Split('/');
        var name = parts[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    // ── Git clone ──────────────────────────────────────────────────────────
    private async Task CloneRepositoryAsync(string gitUrl, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        // Shallow clone (depth=1) — faster, avoids downloading full history
        var psi = new ProcessStartInfo
        {
            FileName  = "git",
            Arguments = $"clone --depth 1 --single-branch \"{gitUrl}\" \"{targetDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process. Is git installed and on PATH?");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("git clone failed: {Error}", stderr);

            if (stderr.Contains("Repository not found") || stderr.Contains("not found"))
                throw new InvalidOperationException("Repository not found or is not public.");

            throw new InvalidOperationException($"git clone failed: {stderr.Trim()}");
        }
    }

    // ── Main analysis orchestration ────────────────────────────────────────
    private async Task<AnalysisSummaryDto> RunAnalysisAsync(
        string gitUrl, string repoName, string cloneDir, CancellationToken ct)
    {
        // Upsert the Repository record
        var repo = await _db.Repositories.FirstOrDefaultAsync(r => r.GitUrl == gitUrl, ct);
        if (repo == null)
        {
            repo = new Repository { GitUrl = gitUrl, Name = repoName, CreatedAt = DateTime.UtcNow };
            _db.Repositories.Add(repo);
        }
        else
        {
            // Remove stale analysis data before re-analysis
            _db.RepositoryFiles.RemoveRange(_db.RepositoryFiles.Where(f => f.RepositoryId == repo.Id));
            _db.RepositoryModules.RemoveRange(_db.RepositoryModules.Where(m => m.RepositoryId == repo.Id));
            _db.RepositoryDependencies.RemoveRange(_db.RepositoryDependencies.Where(d => d.RepositoryId == repo.Id));
        }

        await _db.SaveChangesAsync(ct);

        // ── Files ──────────────────────────────────────────────────────────
        var files = ScanFiles(cloneDir);
        foreach (var f in files)
        {
            f.RepositoryId = repo.Id;
            _db.RepositoryFiles.Add(f);
        }
        await _db.SaveChangesAsync(ct);

        // ── Determine primary language ─────────────────────────────────────
        var langCounts = files
            .Where(f => !f.IsDirectory && !string.IsNullOrEmpty(f.Language))
            .GroupBy(f => f.Language)
            .OrderByDescending(g => g.Count())
            .ToList();

        repo.PrimaryLanguage = langCounts.FirstOrDefault()?.Key ?? "Unknown";

        // ── Modules ────────────────────────────────────────────────────────
        var modules = DetectModules(cloneDir, repo.Id);
        foreach (var m in modules)
            _db.RepositoryModules.Add(m);

        // ── Dependencies ───────────────────────────────────────────────────
        var filePathIndex = files
            .Where(f => !f.IsDirectory)
            .ToDictionary(f => f.FilePath, f => f, StringComparer.OrdinalIgnoreCase);

        var deps = DetectDependencies(cloneDir, files, filePathIndex, repo.Id);
        foreach (var d in deps)
            _db.RepositoryDependencies.Add(d);

        repo.AnalyzedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Analysis complete: {Files} files, {Modules} modules, {Deps} deps",
            files.Count(f => !f.IsDirectory), modules.Count, deps.Count);

        return new AnalysisSummaryDto
        {
            RepositoryId     = repo.Id,
            Name             = repo.Name,
            PrimaryLanguage  = repo.PrimaryLanguage,
            TotalFiles       = files.Count(f => !f.IsDirectory),
            TotalDirectories = files.Count(f => f.IsDirectory),
            TotalModules     = modules.Count,
            TotalDependencies = deps.Count,
            AnalyzedAt       = repo.AnalyzedAt!.Value
        };
    }

    // ── File scanning ──────────────────────────────────────────────────────
    private List<RepositoryFile> ScanFiles(string rootDir)
    {
        var result = new List<RepositoryFile>();
        ScanDirectory(rootDir, rootDir, result);
        return result;
    }

    private void ScanDirectory(string rootDir, string currentDir, List<RepositoryFile> result)
    {
        // Add directory entry
        if (currentDir != rootDir)
        {
            var dirName = Path.GetFileName(currentDir);
            var dirRelPath = GetRelativePath(rootDir, currentDir);
            result.Add(new RepositoryFile
            {
                FilePath    = dirRelPath,
                FileName    = dirName,
                Extension   = string.Empty,
                Language    = string.Empty,
                FileSize    = 0,
                IsDirectory = true,
                CreatedAt   = DateTime.UtcNow
            });
        }

        // Recurse subdirectories
        try
        {
            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                var dirName = Path.GetFileName(subDir);
                if (IgnoredDirs.Contains(dirName)) continue;
                ScanDirectory(rootDir, subDir, result);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied scanning directory {Dir}: {Msg}", currentDir, ex.Message);
        }

        // Files
        try
        {
            foreach (var file in Directory.GetFiles(currentDir))
            {
                var ext = Path.GetExtension(file);
                if (IgnoredExtensions.Contains(ext)) continue;

                FileInfo fi;
                try { fi = new FileInfo(file); }
                catch { continue; }

                if (fi.Length > MaxFileSizeBytes)
                {
                    _logger.LogDebug("Skipping large file {File} ({Size} bytes)", file, fi.Length);
                    continue;
                }

                var relPath = GetRelativePath(rootDir, file);
                var language = DetectLanguage(file);

                result.Add(new RepositoryFile
                {
                    FilePath    = relPath,
                    FileName    = Path.GetFileName(file),
                    Extension   = ext.ToLowerInvariant(),
                    Language    = language,
                    FileSize    = fi.Length,
                    IsDirectory = false,
                    CreatedAt   = DateTime.UtcNow
                });
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied reading files in {Dir}: {Msg}", currentDir, ex.Message);
        }
    }

    private static string GetRelativePath(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        return rel.Replace('\\', '/');
    }

    private static string DetectLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            // Extensionless — check filename
            var name = Path.GetFileName(filePath);
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))     return "Dockerfile";
            if (name.Equals("Makefile", StringComparison.OrdinalIgnoreCase))       return "Makefile";
            if (name.Equals("Vagrantfile", StringComparison.OrdinalIgnoreCase))    return "Ruby";
            if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase))       return "Config";
            return string.Empty;
        }
        return LanguageMap.TryGetValue(ext, out var lang) ? lang : string.Empty;
    }

    // ── Module detection ───────────────────────────────────────────────────
    private List<RepositoryModule> DetectModules(string rootDir, int repositoryId)
    {
        var modules = new List<RepositoryModule>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories))
        {
            var dirName = Path.GetFileName(dir);
            if (IgnoredDirs.Contains(dirName)) continue;

            if (!KnownModuleNames.TryGetValue(dirName, out var moduleType)) continue;

            var relPath = GetRelativePath(rootDir, dir);
            if (seen.Contains(relPath)) continue;
            seen.Add(relPath);

            modules.Add(new RepositoryModule
            {
                RepositoryId = repositoryId,
                Name         = dirName,
                Path         = relPath,
                ModuleType   = moduleType,
                Description  = $"Detected {moduleType} module at {relPath}"
            });
        }

        return modules;
    }

    // ── Dependency detection ───────────────────────────────────────────────
    private List<RepositoryDependency> DetectDependencies(
        string rootDir,
        List<RepositoryFile> files,
        Dictionary<string, RepositoryFile> filePathIndex,
        int repositoryId)
    {
        var deps = new List<RepositoryDependency>();
        var seen = new HashSet<(int, int)>(); // avoid duplicate edges

        foreach (var file in files.Where(f => !f.IsDirectory))
        {
            var language = file.Language;
            if (string.IsNullOrEmpty(language)) continue;

            string[] imports;
            try
            {
                var fullPath = Path.Combine(rootDir, file.FilePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath)) continue;

                var content = File.ReadAllText(fullPath);
                imports = ExtractImports(language, content);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not read {File}: {Msg}", file.FilePath, ex.Message);
                continue;
            }

            foreach (var import in imports)
            {
                var resolved = ResolveImport(import, file.FilePath, filePathIndex, rootDir);
                if (resolved == null) continue;

                var key = (file.Id, resolved.Id);
                if (seen.Contains(key)) continue;
                seen.Add(key);

                deps.Add(new RepositoryDependency
                {
                    RepositoryId    = repositoryId,
                    SourceFileId    = file.Id,
                    TargetFileId    = resolved.Id,
                    DependencyType  = "local",
                    ImportStatement = import.Length > 500 ? import[..500] : import
                });
            }
        }

        return deps;
    }

    // ── Import extractors by language ──────────────────────────────────────
    private static readonly Regex CsUsing   = new(@"^using\s+([\w.]+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex JsImport  = new(@"(?:import\s+.*?from\s+|require\s*\(\s*)[""'](\.{1,2}/[^""']+)[""']", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex PyImport  = new(@"^(?:from\s+([\w./]+)\s+import|import\s+([\w./]+))", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex JavaImport= new(@"^import\s+([\w.]+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

    private static string[] ExtractImports(string language, string content)
    {
        return language switch
        {
            "C#"         => CsUsing.Matches(content).Select(m => m.Groups[1].Value).ToArray(),
            "TypeScript" => JsImport.Matches(content).Select(m => m.Groups[1].Value).ToArray(),
            "JavaScript" => JsImport.Matches(content).Select(m => m.Groups[1].Value).ToArray(),
            "Python"     => PyImport.Matches(content)
                               .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .ToArray(),
            "Java"       => JavaImport.Matches(content).Select(m => m.Groups[1].Value).ToArray(),
            _            => Array.Empty<string>()
        };
    }

    // ── Import resolver ────────────────────────────────────────────────────
    private static RepositoryFile? ResolveImport(
        string import,
        string sourceFilePath,
        Dictionary<string, RepositoryFile> index,
        string rootDir)
    {
        // Only resolve relative paths (JS/TS)
        if (import.StartsWith("./") || import.StartsWith("../"))
        {
            var sourceDir = Path.GetDirectoryName(sourceFilePath)?.Replace('\\', '/') ?? string.Empty;
            var combined  = NormalizePath(sourceDir + "/" + import);

            // Try with common extensions
            foreach (var ext in new[] { "", ".ts", ".tsx", ".js", ".jsx", ".cs", ".py" })
            {
                var candidate = combined + ext;
                if (index.TryGetValue(candidate, out var found)) return found;
                // Also try index file
                var indexCandidate = combined + "/index" + ext;
                if (index.TryGetValue(indexCandidate, out found)) return found;
            }
        }

        // C# — try to match by filename (namespace to file is not deterministic without full project parsing)
        // Only match if the last segment of the namespace matches a file name exactly
        if (import.Contains('.'))
        {
            var className = import.Split('.').Last();
            var candidates = index.Values
                .Where(f => Path.GetFileNameWithoutExtension(f.FileName)
                    .Equals(className, StringComparison.OrdinalIgnoreCase)
                    && f.Language == "C#")
                .ToList();
            if (candidates.Count == 1) return candidates[0];
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        var parts = new LinkedList<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment == ".." && parts.Count > 0) parts.RemoveLast();
            else if (segment != "." && segment != string.Empty) parts.AddLast(segment);
        }
        return string.Join("/", parts);
    }

    // ── Temp directory cleanup ─────────────────────────────────────────────
    private void SafeDeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            // On Windows, git sets some files read-only — force-remove them
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* ignore */ }
            }
            Directory.Delete(dir, recursive: true);
            _logger.LogDebug("Cleaned up temp directory {Dir}", dir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not fully clean temp directory {Dir}: {Msg}", dir, ex.Message);
        }
    }
}
