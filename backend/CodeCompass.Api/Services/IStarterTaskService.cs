using CodeCompass.Api.DTOs;

namespace CodeCompass.Api.Services;

public interface IStarterTaskService
{
    Task<List<StarterTaskSummaryDto>> GetStarterTasksAsync(
        int repositoryId, CancellationToken ct = default);

    Task<StarterTaskDetailDto?> GetStarterTaskDetailAsync(
        int taskId, CancellationToken ct = default);
}
