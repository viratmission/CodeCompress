using CodeCompass.Api.Data;
using CodeCompass.Api.DTOs;
using CodeCompass.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CodeCompass.Api.Services;

public class ArchitectureService : IArchitectureService
{
    private readonly CodeCompassDbContext _db;
    private readonly ILogger<ArchitectureService> _logger;

    // ── Architectural layer classification by ModuleType ──────────────────
    private static readonly Dictionary<string, string> LayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Frontend",       "Presentation" },
        { "Components",     "Presentation" },
        { "Pages",          "Presentation" },
        { "Views",          "Presentation" },
        { "Hooks",          "Presentation" },
        { "Controllers",    "API" },
        { "API",            "API" },
        { "Web",            "API" },
        { "Services",       "Business Logic" },
        { "Business Logic", "Business Logic" },
        { "Core",           "Business Logic" },
        { "Repositories",   "Data Access" },
        { "Data",           "Data Access" },
        { "Migrations",     "Data Access" },
        { "Models",         "Domain" },
        { "Domain",         "Domain" },
        { "DTOs",           "Domain" },
        { "Utilities",      "Infrastructure" },
        { "Infrastructure", "Infrastructure" },
        { "Configuration",  "Infrastructure" },
        { "Scripts",        "Infrastructure" },
        { "Middleware",     "Infrastructure" },
        { "Shared",         "Shared" },
        { "Source Root",    "Root" },
        { "Library",        "Library" },
        { "Backend",        "Backend" },
        { "Tests",          "Tests" },
        { "Documentation",  "Documentation" },
    };

    // Layer display order (lower = higher in stack diagram)
    private static readonly Dictionary<string, int> LayerOrder = new()
    {
        { "Presentation",   0 },
        { "API",            1 },
        { "Business Logic", 2 },
        { "Data Access",    3 },
        { "Domain",         4 },
        { "Infrastructure", 5 },
        { "Backend",        1 },
        { "Shared",         6 },
        { "Library",        6 },
        { "Root",           7 },
        { "Tests",          8 },
        { "Documentation",  9 },
    };

    public ArchitectureService(CodeCompassDbContext db, ILogger<ArchitectureService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Architecture graph ─────────────────────────────────────────────────
    public async Task<ArchitectureGraphDto?> GetArchitectureGraphAsync(
        int repositoryId, CancellationToken ct = default)
    {
        var repo = await _db.Repositories.FindAsync(new object[] { repositoryId }, ct);
        if (repo == null) return null;

        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == repositoryId)
            .ToListAsync(ct);

        if (modules.Count == 0)
        {
            _logger.LogInformation("Repository {Id} has no modules — returning empty graph", repositoryId);
            return new ArchitectureGraphDto
            {
                RepositoryId   = repositoryId,
                RepositoryName = repo.Name,
            };
        }

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repositoryId && !f.IsDirectory)
            .Select(f => new { f.Id, f.FilePath })
            .ToListAsync(ct);

        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == repositoryId)
            .Select(d => new { d.SourceFileId, d.TargetFileId })
            .ToListAsync(ct);

        // ── Map each file to its nearest containing module ─────────────────
        // Sort modules by path length descending so the deepest match wins
        var sortedModules = modules
            .OrderByDescending(m => m.Path.Length)
            .ToList();

        var fileToModule = new Dictionary<int, int>(); // fileId → moduleId
        foreach (var f in files)
        {
            foreach (var m in sortedModules)
            {
                if (f.FilePath.StartsWith(m.Path + "/", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.StartsWith(m.Path + "\\", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.Equals(m.Path, StringComparison.OrdinalIgnoreCase))
                {
                    fileToModule[f.Id] = m.Id;
                    break;
                }
            }
        }

        // ── Count files per module ────────────────────────────────────────
        var fileCounts = fileToModule
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // ── Build inter-module dependency edges ───────────────────────────
        // edge = (sourceModuleId, targetModuleId) with weight = file dep count
        var edgeWeights = new Dictionary<(int, int), int>();
        foreach (var dep in deps)
        {
            fileToModule.TryGetValue(dep.SourceFileId, out var srcMod);
            fileToModule.TryGetValue(dep.TargetFileId, out var tgtMod);
            if (srcMod == 0 || tgtMod == 0 || srcMod == tgtMod) continue;

            var key = (srcMod, tgtMod);
            edgeWeights[key] = edgeWeights.TryGetValue(key, out var w) ? w + 1 : 1;
        }

        // ── Compute per-node in/out counts ────────────────────────────────
        var outgoing = new Dictionary<int, int>();
        var incoming = new Dictionary<int, int>();
        foreach (var ((src, tgt), _) in edgeWeights)
        {
            outgoing[src] = outgoing.TryGetValue(src, out var o) ? o + 1 : 1;
            incoming[tgt] = incoming.TryGetValue(tgt, out var i) ? i + 1 : 1;
        }

        // ── Build nodes ───────────────────────────────────────────────────
        var nodes = modules.Select(m =>
        {
            var layer = LayerMap.TryGetValue(m.ModuleType, out var l) ? l : m.ModuleType;
            return new ArchitectureNodeDto
            {
                Id           = $"module-{m.Id}",
                Type         = "module",
                Name         = m.Name,
                Path         = m.Path,
                Description  = m.Description,
                ModuleType   = m.ModuleType,
                Layer        = layer,
                FileCount    = fileCounts.TryGetValue(m.Id, out var fc) ? fc : 0,
                OutgoingDeps = outgoing.TryGetValue(m.Id, out var od) ? od : 0,
                IncomingDeps = incoming.TryGetValue(m.Id, out var id) ? id : 0,
            };
        })
        .OrderBy(n => LayerOrder.TryGetValue(n.Layer, out var lo) ? lo : 99)
        .ThenBy(n => n.Name)
        .ToList();

        // ── Build edges ───────────────────────────────────────────────────
        var edges = edgeWeights.Select(kv => new ArchitectureEdgeDto
        {
            Id               = $"edge-{kv.Key.Item1}-{kv.Key.Item2}",
            Source           = $"module-{kv.Key.Item1}",
            Target           = $"module-{kv.Key.Item2}",
            RelationshipType = "depends-on",
            Weight           = kv.Value,
        }).ToList();

        return new ArchitectureGraphDto
        {
            RepositoryId   = repositoryId,
            RepositoryName = repo.Name,
            Nodes          = nodes,
            Edges          = edges,
        };
    }

    // ── Module detail ──────────────────────────────────────────────────────
    public async Task<ModuleDetailDto?> GetModuleDetailAsync(
        int repositoryId, int moduleId, CancellationToken ct = default)
    {
        var module = await _db.RepositoryModules
            .FirstOrDefaultAsync(m => m.Id == moduleId && m.RepositoryId == repositoryId, ct);
        if (module == null) return null;

        var allModules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == repositoryId)
            .ToListAsync(ct);

        // Files in this module (files whose path starts with module path)
        var allFiles = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repositoryId && !f.IsDirectory)
            .ToListAsync(ct);

        var moduleFiles = allFiles
            .Where(f => f.FilePath.StartsWith(module.Path + "/", StringComparison.OrdinalIgnoreCase)
                     || f.FilePath.StartsWith(module.Path + "\\", StringComparison.OrdinalIgnoreCase)
                     || f.FilePath.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var moduleFileIds = moduleFiles.Select(f => f.Id).ToHashSet();

        // All file IDs per module for inter-module dep resolution
        var sortedModules = allModules.OrderByDescending(m => m.Path.Length).ToList();
        var fileToModule  = new Dictionary<int, int>();
        foreach (var f in allFiles)
        {
            foreach (var m in sortedModules)
            {
                if (f.FilePath.StartsWith(m.Path + "/", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.StartsWith(m.Path + "\\", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.Equals(m.Path, StringComparison.OrdinalIgnoreCase))
                {
                    fileToModule[f.Id] = m.Id;
                    break;
                }
            }
        }

        // Dependencies
        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == repositoryId)
            .Include(d => d.SourceFile)
            .Include(d => d.TargetFile)
            .ToListAsync(ct);

        // Outgoing: source is inside this module, target is outside
        var outgoingEdges = deps
            .Where(d => moduleFileIds.Contains(d.SourceFileId) && !moduleFileIds.Contains(d.TargetFileId))
            .GroupBy(d => fileToModule.TryGetValue(d.TargetFileId, out var tid) ? tid : -1)
            .Where(g => g.Key != -1)
            .Select(g =>
            {
                var targetModule = allModules.FirstOrDefault(m => m.Id == g.Key);
                return new ModuleDependencyDto
                {
                    ModuleId        = g.Key,
                    ModuleName      = targetModule?.Name ?? "Unknown",
                    ModulePath      = targetModule?.Path ?? string.Empty,
                    DependencyCount = g.Count(),
                };
            }).ToList();

        // Incoming: source is outside, target is inside this module
        var incomingEdges = deps
            .Where(d => !moduleFileIds.Contains(d.SourceFileId) && moduleFileIds.Contains(d.TargetFileId))
            .GroupBy(d => fileToModule.TryGetValue(d.SourceFileId, out var sid) ? sid : -1)
            .Where(g => g.Key != -1)
            .Select(g =>
            {
                var srcModule = allModules.FirstOrDefault(m => m.Id == g.Key);
                return new ModuleDependencyDto
                {
                    ModuleId        = g.Key,
                    ModuleName      = srcModule?.Name ?? "Unknown",
                    ModulePath      = srcModule?.Path ?? string.Empty,
                    DependencyCount = g.Count(),
                };
            }).ToList();

        // Related modules = union of modules in outgoing + incoming
        var relatedIds = outgoingEdges.Select(d => d.ModuleId)
            .Union(incomingEdges.Select(d => d.ModuleId))
            .Distinct().ToHashSet();
        var relatedModules = allModules
            .Where(m => relatedIds.Contains(m.Id))
            .Select(m => new RepositoryModuleDto
            {
                Id = m.Id, Name = m.Name, Path = m.Path,
                ModuleType = m.ModuleType, Description = m.Description
            }).ToList();

        var layer = LayerMap.TryGetValue(module.ModuleType, out var la) ? la : module.ModuleType;

        return new ModuleDetailDto
        {
            Id                   = module.Id,
            Name                 = module.Name,
            Path                 = module.Path,
            ModuleType           = module.ModuleType,
            Description          = module.Description,
            Layer                = layer,
            FileCount            = moduleFiles.Count,
            Files                = moduleFiles.Select(f => new RepositoryFileDto
            {
                Id          = f.Id,
                FilePath    = f.FilePath,
                FileName    = f.FileName,
                Extension   = f.Extension,
                Language    = f.Language,
                FileSize    = f.FileSize,
                IsDirectory = f.IsDirectory,
            }).OrderBy(f => f.FilePath).ToList(),
            OutgoingDependencies = outgoingEdges,
            IncomingDependencies = incomingEdges,
            RelatedModules       = relatedModules,
        };
    }

    // ── Dependency flow (BFS over module graph) ────────────────────────────
    public async Task<FlowResultDto> GetFlowAsync(
        int repositoryId, string? from, string? to, CancellationToken ct = default)
    {
        var empty = new FlowResultDto { From = from ?? string.Empty, To = to ?? string.Empty };

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            empty.Message = "Both 'from' and 'to' query parameters are required.";
            return empty;
        }

        // Build a combined search pool: modules + files
        var modules = await _db.RepositoryModules
            .Where(m => m.RepositoryId == repositoryId)
            .ToListAsync(ct);

        var files = await _db.RepositoryFiles
            .Where(f => f.RepositoryId == repositoryId && !f.IsDirectory)
            .ToListAsync(ct);

        var deps = await _db.RepositoryDependencies
            .Where(d => d.RepositoryId == repositoryId)
            .ToListAsync(ct);

        // Map files to modules
        var sortedModules = modules.OrderByDescending(m => m.Path.Length).ToList();
        var fileToModule  = new Dictionary<int, int>();
        foreach (var f in files)
        {
            foreach (var m in sortedModules)
            {
                if (f.FilePath.StartsWith(m.Path + "/", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.StartsWith(m.Path + "\\", StringComparison.OrdinalIgnoreCase)
                    || f.FilePath.Equals(m.Path, StringComparison.OrdinalIgnoreCase))
                {
                    fileToModule[f.Id] = m.Id;
                    break;
                }
            }
        }

        // Find start and end candidates — match by name or path (case-insensitive)
        // Priority: module name > module path segment > file name > file path
        int? fromModuleId = FindModule(modules, from);
        int? toModuleId   = FindModule(modules, to);

        // Fall back to file-level search
        if (fromModuleId == null || toModuleId == null)
        {
            var fromFile = FindFile(files, from);
            var toFile   = FindFile(files, to);

            if (fromFile == null || toFile == null)
            {
                empty.PathFound = false;
                empty.Message   = $"Could not locate '{from}' or '{to}' as a module or file in this repository.";
                return empty;
            }

            // Check direct file dependency
            bool directFileDep = deps.Any(d => d.SourceFileId == fromFile.Id && d.TargetFileId == toFile.Id);
            if (directFileDep)
            {
                return new FlowResultDto
                {
                    PathFound = true,
                    From      = from,
                    To        = to,
                    Message   = "Direct file dependency found.",
                    Steps     = new List<FlowStepDto>
                    {
                        new() { Order = 0, NodeId = $"file-{fromFile.Id}", Name = fromFile.FileName, Path = fromFile.FilePath, NodeType = "file" },
                        new() { Order = 1, NodeId = $"file-{toFile.Id}",   Name = toFile.FileName,   Path = toFile.FilePath,   NodeType = "file" },
                    }
                };
            }

            empty.PathFound = false;
            empty.Message   = $"No local dependency path found from '{from}' to '{to}'.";
            return empty;
        }

        // BFS over module dependency graph
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var dep in deps)
        {
            fileToModule.TryGetValue(dep.SourceFileId, out var src);
            fileToModule.TryGetValue(dep.TargetFileId, out var tgt);
            if (src == 0 || tgt == 0 || src == tgt) continue;
            if (!adjacency.ContainsKey(src)) adjacency[src] = new List<int>();
            if (!adjacency[src].Contains(tgt)) adjacency[src].Add(tgt);
        }

        var path = BfsPath(fromModuleId.Value, toModuleId.Value, adjacency);
        if (path == null)
        {
            return new FlowResultDto
            {
                PathFound = false,
                From      = from,
                To        = to,
                Message   = $"No known local dependency path from '{from}' to '{to}' could be derived from stored dependency relationships.",
            };
        }

        var moduleById = modules.ToDictionary(m => m.Id);
        return new FlowResultDto
        {
            PathFound = true,
            From      = from,
            To        = to,
            Message   = $"Dependency path found ({path.Count} steps).",
            Steps     = path.Select((id, idx) =>
            {
                moduleById.TryGetValue(id, out var mod);
                return new FlowStepDto
                {
                    Order    = idx,
                    NodeId   = $"module-{id}",
                    Name     = mod?.Name ?? id.ToString(),
                    Path     = mod?.Path ?? string.Empty,
                    NodeType = "module",
                };
            }).ToList(),
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static int? FindModule(List<RepositoryModule> modules, string query)
    {
        // Exact name match
        var m = modules.FirstOrDefault(x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (m != null) return m.Id;
        // Path segment match
        m = modules.FirstOrDefault(x =>
            x.Path.Split('/', '\\').Any(seg => seg.Equals(query, StringComparison.OrdinalIgnoreCase)));
        return m?.Id;
    }

    private static RepositoryFile? FindFile(List<RepositoryFile> files, string query)
    {
        return files.FirstOrDefault(f =>
            f.FileName.Equals(query, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(f.FileName).Equals(query, StringComparison.OrdinalIgnoreCase)
            || f.FilePath.EndsWith(query, StringComparison.OrdinalIgnoreCase));
    }

    private static List<int>? BfsPath(int start, int end, Dictionary<int, List<int>> adjacency)
    {
        if (start == end) return new List<int> { start };

        var visited = new HashSet<int> { start };
        var queue   = new Queue<List<int>>();
        queue.Enqueue(new List<int> { start });

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var last = path[^1];
            if (!adjacency.TryGetValue(last, out var neighbours)) continue;
            foreach (var next in neighbours)
            {
                if (visited.Contains(next)) continue;
                var newPath = new List<int>(path) { next };
                if (next == end) return newPath;
                visited.Add(next);
                queue.Enqueue(newPath);
            }
        }
        return null;
    }
}
