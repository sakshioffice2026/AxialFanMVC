namespace AxialFanMVC.Repositories;

public interface IDesignOptimizationService
{
    Task<OptimizeFlowResponse> OptimizeAsync(
        int userId,
        int resultId,
        CancellationToken cancellationToken = default);

    Task<SaveDraftResponse> SaveDraftAsync(
        int userId,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default);
}
