using AxialFanMVC.Repositories.Inteface;

namespace AxialFanMVC.Repositories;

public sealed class RetrievalService : IRetrievalService
{
    private readonly IQdrantHandbookVectorService _vectorService;
    private readonly ILlamaSharpEmbeddingService _embeddingService;

    public RetrievalService(
        IQdrantHandbookVectorService vectorService,
        ILlamaSharpEmbeddingService embeddingService)
    {
        _vectorService = vectorService;
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<string>> SearchHandbookAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        maxResults = Math.Clamp(maxResults, 1, 20);

        var embedding = await _embeddingService.EmbedAsync(query);

        var results = await _vectorService.SearchAsync(
            embedding,
            maxResults);

        return results
            .Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }
}