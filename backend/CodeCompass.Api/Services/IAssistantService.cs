using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

public interface IAssistantService
{
    Task<AssistantAnswerDto> AskAsync(
        int repositoryId,
        AskRequestDto request,
        CancellationToken ct = default,
        int? userId = null);

    Task<SuggestedQuestionsDto> GetSuggestedQuestionsAsync(
        int repositoryId,
        CancellationToken ct = default);
}
