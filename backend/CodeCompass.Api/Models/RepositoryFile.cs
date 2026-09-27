using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CodeCompass.Api.Models;

public class RepositoryFile
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    [Required]
    [MaxLength(2000)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Extension { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Language { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public bool IsDirectory { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(RepositoryId))]
    public Repository Repository { get; set; } = null!;
}
