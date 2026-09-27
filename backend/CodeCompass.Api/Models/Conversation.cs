using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class Conversation
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }

    public int? UserId { get; set; }

    [MaxLength(200)]
    public string SessionId { get; set; } = string.Empty;

    [Required]
    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    /// <summary>ask | change-impact</summary>
    [MaxLength(50)]
    public string QuestionType { get; set; } = "ask";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Repository Repository { get; set; } = null!;
    public User? User { get; set; }
}
