using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CodeCompass.Api.Models;

public class RepositoryDependency
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    public int SourceFileId { get; set; }

    public int TargetFileId { get; set; }

    /// <summary>local | external</summary>
    [MaxLength(50)]
    public string DependencyType { get; set; } = "local";

    [MaxLength(500)]
    public string ImportStatement { get; set; } = string.Empty;

    // Navigation
    [ForeignKey(nameof(RepositoryId))]
    public Repository Repository { get; set; } = null!;

    [ForeignKey(nameof(SourceFileId))]
    public RepositoryFile SourceFile { get; set; } = null!;

    [ForeignKey(nameof(TargetFileId))]
    public RepositoryFile TargetFile { get; set; } = null!;
}
