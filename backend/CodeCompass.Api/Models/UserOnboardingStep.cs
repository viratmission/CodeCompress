using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class UserOnboardingStep
{
    public int Id { get; set; }

    public int UserOnboardingId { get; set; }

    public int OnboardingStepId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending | in_progress | completed

    public DateTime? CompletedAt { get; set; }

    // Navigation
    public UserOnboarding UserOnboarding { get; set; } = null!;
    public OnboardingStep OnboardingStep { get; set; } = null!;
}
