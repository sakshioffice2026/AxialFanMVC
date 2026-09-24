using AxialFanMVC.Repositories.Inteface;
using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories.Plugins;

public sealed class AppDataQueryPlugin
{
    private readonly IAppDataQueryService _appDataQueryService;

    public AppDataQueryPlugin(IAppDataQueryService appDataQueryService)
    {
        _appDataQueryService = appDataQueryService;
    }

    [KernelFunction("GetProjectSummary")]
    public Task<object?> GetProjectSummary(int projectId)
        => _appDataQueryService.GetProjectSummaryAsync(projectId);

    [KernelFunction("GetDesignResult")]
    public Task<object?> GetDesignResult(int resultId)
        => _appDataQueryService.GetDesignResultAsync(resultId);

    [KernelFunction("GetBomForProject")]
    public Task<object> GetBomForProject(int projectId)
        => _appDataQueryService.GetBomForProjectAsync(projectId);

    [KernelFunction("ListRecentDesigns")]
    public Task<object> ListRecentDesigns(
        int userId,
        int maxResults = 10)
        => _appDataQueryService.ListRecentDesignsAsync(
            userId,
            maxResults);
}