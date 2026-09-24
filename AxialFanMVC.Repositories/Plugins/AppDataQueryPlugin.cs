using System.ComponentModel;
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
    [Description("Gets summary details of a project: name, client, application, engineer, status and number of designs.")]
    public Task<object?> GetProjectSummary(
        [Description("Project id")] int projectId)
        => _appDataQueryService.GetProjectSummaryAsync(projectId);

    [KernelFunction("GetDesignInput")]
    [Description("Gets the input parameters of a design: duty point, speed, blade count, tip diameter, constraints, motor and drive details.")]
    public Task<object?> GetDesignInput(
        [Description("Design input id")] int designInputId)
        => _appDataQueryService.GetDesignInputAsync(designInputId);

    [KernelFunction("GetDesignResult")]
    [Description("Gets calculated results of a design: efficiency, shaft power, tip speed, hub diameter, blade stress and safety factor.")]
    public Task<object?> GetDesignResult(
        [Description("Design result id")] int resultId)
        => _appDataQueryService.GetDesignResultAsync(resultId);

    [KernelFunction("GetBomForProject")]
    [Description("Gets all bill of material line items across every design result of a project.")]
    public Task<object> GetBomForProject(
        [Description("Project id")] int projectId)
        => _appDataQueryService.GetBomForProjectAsync(projectId);

    [KernelFunction("GetBomForResult")]
    [Description("Gets bill of material line items and the grand total cost for one design result.")]
    public Task<object> GetBomForResult(
        [Description("Design result id")] int resultId)
        => _appDataQueryService.GetBomForResultAsync(resultId);

    [KernelFunction("ListRecentDesigns")]
    [Description("Lists the most recent designs of a user with their duty point and linked result id.")]
    public Task<object> ListRecentDesigns(
        [Description("User id")] int userId,
        [Description("Maximum rows to return, 1 to 50")] int maxResults = 10)
        => _appDataQueryService.ListRecentDesignsAsync(userId, maxResults);

    [KernelFunction("GetLatestCfdJob")]
    [Description("Gets the latest CFD job status and output paths for a design result.")]
    public Task<object?> GetLatestCfdJob(
        [Description("Design result id")] int resultId)
        => _appDataQueryService.GetLatestCfdJobAsync(resultId);

    [KernelFunction("ListCfdJobs")]
    [Description("Lists recent CFD jobs of a user with their status.")]
    public Task<object> ListCfdJobs(
        [Description("User id")] int userId,
        [Description("Maximum rows to return, 1 to 50")] int maxResults = 10)
        => _appDataQueryService.ListCfdJobsAsync(userId, maxResults);

    [KernelFunction("GetOptimizationJob")]
    [Description("Gets an optimization job with its status, constraint request and the Budget, Silent and Premium candidates when completed.")]
    public Task<object?> GetOptimizationJob(
        [Description("Optimization job id")] int jobId)
        => _appDataQueryService.GetOptimizationJobAsync(jobId);

    [KernelFunction("ListOptimizationJobs")]
    [Description("Lists recent optimization jobs of a project with their status.")]
    public Task<object> ListOptimizationJobs(
        [Description("Project id")] int projectId,
        [Description("Maximum rows to return, 1 to 50")] int maxResults = 10)
        => _appDataQueryService.ListOptimizationJobsAsync(projectId, maxResults);
}