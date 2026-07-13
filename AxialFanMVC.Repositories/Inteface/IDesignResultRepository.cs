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


    }
}