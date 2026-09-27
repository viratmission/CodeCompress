using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class StarterTask
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Difficulty { get; set; } = "Easy"; // Easy | Medium | Hard

    public string Reason { get; set; } = string.Empty;

    public int? RelatedModuleId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Repository Repository { get; set; } = null!;
    public RepositoryModule? RelatedModule { get; set; }
}
