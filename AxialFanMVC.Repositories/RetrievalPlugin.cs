using AxialFanMVC.Repositories.Inteface;
using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories;

public sealed class RetrievalPlugin : IRetrievalPlugin
{
    private readonly IRetrievalService _retrievalService;

    public RetrievalPlugin(IRetrievalService retrievalService)
    {
        _retrievalService = retrievalService;
    }

    [KernelFunction("SearchHandbook")]
    public Task<IReadOnlyList<string>> SearchHandbookAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        return _retrievalService.SearchHandbookAsync(
            query,
            maxResults,
            cancellationToken);
    }
}