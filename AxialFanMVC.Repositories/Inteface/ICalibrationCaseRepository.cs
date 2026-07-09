using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface ICalibrationCaseRepository
    {
        /// <summary>
        /// All calibration cases with their points loaded — the training/
        /// validation set for BladeElementEngine accuracy checks and the
        /// PINN correction model's data-fit loss term.
        /// </summary>
        Task<List<CalibrationCase>> GetAllWithPointsAsync();

        /// <summary>
        /// Cases whose captured geometry is "close enough" to the given
        /// design to be a fair comparison — same blade count, and hub
        /// ratio / tip diameter within the given tolerances. Used to find
        /// relevant calibration cases for a specific design rather than
        /// pulling the entire table every time.
        /// </summary>
        Task<List<CalibrationCase>> GetSimilarAsync(
            double tipDiameterMm, double hubRatio, int bladeCount,
            double tipDiameterTolerancePct = 15.0, double hubRatioTolerance = 0.05);

        Task<CalibrationCase?> GetByIdWithPointsAsync(int id);
    }
}