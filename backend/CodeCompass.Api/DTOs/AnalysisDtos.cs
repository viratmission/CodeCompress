namespace CodeCompass.Api.DTOs;

public class AnalyzeRequestDto
{
    public string GitUrl { get; set; } = string.Empty;
}

public class AnalysisSummaryDto
{
    public int RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int TotalDirectories { get; set; }
    public int TotalModules { get; set; }
    public int TotalDependencies { get; set; }
    public DateTime AnalyzedAt { get; set; }
}

public class RepositoryAnalysisDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public int TotalFiles { get; set; }
    public int TotalDirectories { get; set; }
    public int TotalModules { get; set; }
    public int TotalDependencies { get; set; }
    public List<LanguageStatDto> LanguageStats { get; set; } = new();
}

public class LanguageStatDto
{
    public string Language { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public double Percentage { get; set; }
}

public class RepositoryFileDto
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsDirectory { get; set; }
}

public class RepositoryModuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ModuleType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RepositoryDependencyDto
{
    public int Id { get; set; }
    public string SourceFilePath { get; set; } = string.Empty;
    public string TargetFilePath { get; set; } = string.Empty;
    public string DependencyType { get; set; } = string.Empty;
    public string ImportStatement { get; set; } = string.Empty;
}
