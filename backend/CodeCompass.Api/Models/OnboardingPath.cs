using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class OnboardingPath
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Role { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Repository Repository { get; set; } = null!;
    public ICollection<OnboardingStep> Steps { get; set; } = new List<OnboardingStep>();
}
