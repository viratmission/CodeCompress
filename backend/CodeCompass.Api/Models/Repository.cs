using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class Repository
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string GitUrl { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PrimaryLanguage { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AnalyzedAt { get; set; }

    // Navigation
    public ICollection<RepositoryFile> Files { get; set; } = new List<RepositoryFile>();
    public ICollection<RepositoryModule> Modules { get; set; } = new List<RepositoryModule>();
    public ICollection<RepositoryDependency> Dependencies { get; set; } = new List<RepositoryDependency>();
    public ICollection<OnboardingPath> OnboardingPaths { get; set; } = new List<OnboardingPath>();
    public ICollection<StarterTask> StarterTasks { get; set; } = new List<StarterTask>();
}
