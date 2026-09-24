namespace AxialFanMVC.Repositories.Inteface;

public interface IAppDataQueryService
{
    Task<object?> GetProjectSummaryAsync(int projectId);

    Task<object?> GetDesignResultAsync(int resultId);

    Task<object> GetBomForProjectAsync(int projectId);

    Task<object> ListRecentDesignsAsync(
        int userId,
        int maxResults = 10);
}