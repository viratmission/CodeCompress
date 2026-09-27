using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CodeCompass.Api.Models;

public class RepositoryModule
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ModuleType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    // Navigation
    [ForeignKey(nameof(RepositoryId))]
    public Repository Repository { get; set; } = null!;
}
