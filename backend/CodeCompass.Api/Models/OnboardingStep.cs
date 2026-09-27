using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class OnboardingStep
{
    public int Id { get; set; }

    public int OnboardingPathId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Order { get; set; }

    public int? ModuleId { get; set; }

    [MaxLength(50)]
    public string StepType { get; set; } = "module";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public OnboardingPath OnboardingPath { get; set; } = null!;
    public RepositoryModule? Module { get; set; }
}
