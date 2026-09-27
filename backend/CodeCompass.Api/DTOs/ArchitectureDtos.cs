namespace CodeCompass.Api.DTOs;

// ── Graph returned by GET /api/repositories/{id}/architecture ─────────────
public class ArchitectureGraphDto
{
    public int RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public List<ArchitectureNodeDto> Nodes { get; set; } = new();
    public List<ArchitectureEdgeDto> Edges { get; set; } = new();
}

public class ArchitectureNodeDto
{
    public string Id { get; set; } = string.Empty;       // "module-{id}"
    public string Type { get; set; } = string.Empty;     // module | layer | root
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;    // derived architectural layer
    public int FileCount { get; set; }
    public int OutgoingDeps { get; set; }
    public int IncomingDeps { get; set; }
}

public class ArchitectureEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = "depends-on";
    public int Weight { get; set; }  // number of inter-module file dependencies
}

// ── Module detail returned by GET /api/repositories/{id}/modules/{moduleId} ─
public class ModuleDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public List<RepositoryFileDto> Files { get; set; } = new();
    public List<ModuleDependencyDto> OutgoingDependencies { get; set; } = new();
    public List<ModuleDependencyDto> IncomingDependencies { get; set; } = new();
    public List<RepositoryModuleDto> RelatedModules { get; set; } = new();
}

public class ModuleDependencyDto
{
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string ModulePath { get; set; } = string.Empty;
    public int DependencyCount { get; set; }
}

// ── Flow returned by GET /api/repositories/{id}/flow ─────────────────────
public class FlowResultDto
{
    public bool PathFound { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<FlowStepDto> Steps { get; set; } = new();
}

public class FlowStepDto
{
    public int Order { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;   // file | module
}
