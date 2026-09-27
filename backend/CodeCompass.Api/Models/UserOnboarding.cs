using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class UserOnboarding
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int RepositoryId { get; set; }

    public int OnboardingPathId { get; set; }

    public double ProgressPercentage { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    // Navigation
    public User? User { get; set; }
    public Repository Repository { get; set; } = null!;
    public OnboardingPath OnboardingPath { get; set; } = null!;
    public ICollection<UserOnboardingStep> UserSteps { get; set; } = new List<UserOnboardingStep>();
}
