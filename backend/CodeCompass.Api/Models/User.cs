using System.ComponentModel.DataAnnotations;

namespace CodeCompass.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Alias for DisplayName to maintain backwards compatibility with existing schema/migrations.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Name
    {
        get => DisplayName;
        set => DisplayName = value;
    }

    [Required]
    [MaxLength(200)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Role { get; set; } = "Developer";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
