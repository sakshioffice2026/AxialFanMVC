namespace AxialFanMVC.Repositories.Inteface;

public interface IAppDataQueryService
{
    Task<object?> GetProjectSummaryAsync(int projectId);

    Task<object?> GetDesignInputAsync(int designInputId);

    Task<object?> GetDesignResultAsync(int resultId);

    Task<object> GetBomForProjectAsync(int projectId);

    Task<object> GetBomForResultAsync(int resultId);

    Task<object> ListRecentDesignsAsync(
        int userId,
        int maxResults = 10);

    Task<object?> GetLatestCfdJobAsync(int resultId);

    Task<object> ListCfdJobsAsync(
        int userId,
        int maxResults = 10);

    Task<object?> GetOptimizationJobAsync(int jobId);

    Task<object> ListOptimizationJobsAsync(
        int projectId,
        int maxResults = 10);
}