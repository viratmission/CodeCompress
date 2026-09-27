namespace CodeCompass.Api.DTOs;

// ── Incoming request ──────────────────────────────────────────────────────
public class AskRequestDto
{
    public string Question { get; set; } = string.Empty;
    /// <summary>ask | change-impact</summary>
    public string QuestionType { get; set; } = "ask";
    public string? SessionId { get; set; }
}

// ── Outgoing answer ───────────────────────────────────────────────────────
public class AssistantAnswerDto
{
    public string Answer { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "ask";
    public List<FileReferenceDto> References { get; set; } = new();
    public List<ModuleReferenceDto> Modules { get; set; } = new();
    public List<ChangeImpactItemDto> ChangeImpact { get; set; } = new();
    public ContextSummaryDto Context { get; set; } = new();
    public string SessionId { get; set; } = string.Empty;
}

public class FileReferenceDto
{
    public string FilePath { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ModuleReferenceDto
{
    public int ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string ModulePath { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
}

public class ChangeImpactItemDto
{
    public string FilePath { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>direct | potential | needs-verification</summary>
    public string Impact { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ContextSummaryDto
{
    public int FilesExamined { get; set; }
    public int ModulesExamined { get; set; }
    public int DependenciesExamined { get; set; }
    public bool SourceCodeRead { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
}

// ── Conversation history ──────────────────────────────────────────────────
public class ConversationDto
{
    public int Id { get; set; }
    public int RepositoryId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "ask";
    public DateTime CreatedAt { get; set; }
}

// ── Suggested questions ───────────────────────────────────────────────────
public class SuggestedQuestionsDto
{
    public List<string> Questions { get; set; } = new();
}
