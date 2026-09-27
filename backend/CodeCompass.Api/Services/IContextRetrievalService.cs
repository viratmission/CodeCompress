using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

/// <summary>
/// Retrieves repository context relevant to a given question.
/// </summary>
public interface IContextRetrievalService
{
    Task<RetrievedContext> RetrieveAsync(
        int repositoryId,
        string question,
        CancellationToken ct = default);

    Task<RetrievedContext> RetrieveAsync(
        int repositoryId,
        string question,
        List<ConversationHistoryItem>? history,
        CancellationToken ct = default);
}

public class ConversationHistoryItem
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Holds all repository intelligence gathered for a question.
/// </summary>
public class RetrievedContext
{
    public string RepositoryName { get; set; } = string.Empty;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;

    // Modules relevant to the question
    public List<RelevantModule> Modules { get; set; } = new();

    // Files relevant to the question (metadata only — no source code)
    public List<RelevantFile> Files { get; set; } = new();

    // Primary core files vs supporting files
    public List<RelevantFile> PrimaryFiles { get; set; } = new();
    public List<RelevantFile> SupportingFiles { get; set; } = new();

    // Specific target file if asked (e.g., AuthService.cs)
    public string? TargetFileName { get; set; }
    public bool TargetFileExists { get; set; }
    public string? AlternativeExistingFile { get; set; }

    // Dependency relationships that are relevant
    public List<RelevantDependency> Dependencies { get; set; } = new();

    // Source code snippets for the most relevant files (truncated)
    public List<SourceSnippet> SourceSnippets { get; set; } = new();

    // Architectural summary
    public string ArchitectureSummary { get; set; } = string.Empty;

    // Categorized files grouped by architectural layer/role
    public List<CategorizedFileGroup> CategorizedFiles { get; set; } = new();

    // Detected primary question intent (one of the 15 standard mentor intents)
    public string UserIntent { get; set; } = "SIMPLE_EXPLANATION";
    public string QuestionIntent { get; set; } = "General";

    // Detected technical domain (Authentication, TodoManagement, Architecture, etc.)
    public string DetectedDomain { get; set; } = "General";

    // Repository fact audit: verified implemented vs missing features
    public List<string> ImplementedFeatures { get; set; } = new();
    public List<string> PartiallyImplementedFeatures { get; set; } = new();
    public List<string> MissingFeatures { get; set; } = new();

    // Discovered route signatures / endpoints from files
    public List<string> DiscoveredEndpoints { get; set; } = new();

    // Discovered domain models & entities
    public List<string> DiscoveredEntities { get; set; } = new();

    // Prior session history turns
    public List<ConversationHistoryItem> History { get; set; } = new();
}

public class CategorizedFileGroup
{
    public string Category { get; set; } = string.Empty;
    public List<RelevantFile> Files { get; set; } = new();
}

public class RelevantModule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
}

public class RelevantFile
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double RelevanceScore { get; set; }
    public string RelevanceReason { get; set; } = string.Empty;
}

public class RelevantDependency
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string ImportStatement { get; set; } = string.Empty;
}

public class SourceSnippet
{
    public string FilePath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    /// <summary>Truncated source content (max ~2000 chars)</summary>
    public string Content { get; set; } = string.Empty;
}
