using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface IDesignResultRepository
    {
        Task<DesignResult?> GetResultForUserAsync(int resultId, int userId);
        Task<Drawing?> GetDrawingForUserAsync(int drawingId, int userId);
        Task AddPerformanceCurveAsync(PerformanceCurve curve);
        Task SaveChangesAsync();

        Task<PerformanceCurve> AddManualCurveAsync(
            int designResultId, int createdByUserId, string label,
            double bladeAngleDeg, int speedRpm,
            List<double> q, List<double> dp, List<double> eta, List<double> kw);

        Task ApplyValidationResultAsync(int performanceCurveId, PhysicsValidationResult result);

        Task<List<PerformanceCurve>> GetCurvesByOriginAsync(int designResultId, string originType);

        // IDesignResultRepository.cs — add:
        Task<BladeProfile?> GetBladeProfileAsync(int bladeProfileId);

        Task<DesignResult?> GetMostRecentResultForUserAsync(int userId);

        // ── BOM & Costing ──────────────────────────────────────────
        Task<List<CostRate>> GetCostRatesAsync();
        Task UpdateCostRatesAsync(List<(int Id, double RateValue)> updates);

        Task<List<BomLineItem>> GetBomLineItemsAsync(int designResultId);

        // Deletes every existing Auto line for this design result and
        // inserts the freshly computed ones, in one call — Manual lines
        // are never touched.
        Task ReplaceAutoBomLineItemsAsync(int designResultId, List<BomLineItem> autoLines);

        Task<BomLineItem> AddManualBomLineItemAsync(
            int designResultId, int createdByUserId, string category,
            string description, double quantity, string? unit, double unitCost);

        // Ownership-checked via DesignResult -> DesignInput -> Project -> UserId,
        // and only ever deletes Manual lines (Auto lines only go away via Regenerate).
        Task<bool> DeleteManualBomLineItemAsync(int lineItemId, int userId);
    }
}