using AxialFanMVC.Database;
using AxialFanMVC.Models;

namespace AxialFanMVC.Services
{
    public interface ICurveGeneration
    {
        /// <summary>
        /// Generates the Baseline (pure-equation) and PINN (ONNX-corrected)
        /// curves for a design result at a given blade angle/RPM, runs both
        /// through the physics validation layer, persists both (validated
        /// values + flags), and returns the data the results view needs.
        /// This is the single place curves get created and saved — no other
        /// code path should call AeroCalcEngine.GenerateCurves directly and
        /// save the result itself.
        /// </summary>
        Task<CurveGenerationResult> GenerateAndSaveAsync(
            int resultId, int userId, double bladeAngleDeg, int speedRpm);

        /// <summary>
        /// Saves a user-entered curve verbatim — no physics validation is
        /// run against manually-entered data (see rationale below), it's
        /// tagged Source="Manual" and stored as-is.
        /// </summary>
        Task<PerformanceCurve> SaveManualCurveAsync(
            int resultId, int userId, string label, double bladeAngleDeg, int speedRpm,
            List<double> q, List<double> dp, List<double> eta, List<double> kw);
    }

    public class CurveGenerationResult
    {
        public PerformanceCurveData Baseline { get; set; } = new();
        public PerformanceCurveData Corrected { get; set; } = new();
        public List<PhysicsValidationFlag> BaselineFlags { get; set; } = new();
        public List<PhysicsValidationFlag> CorrectedFlags { get; set; } = new();
    }
}