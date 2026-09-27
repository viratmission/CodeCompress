namespace CodeCompass.Api.DTOs;

public class RepositoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
