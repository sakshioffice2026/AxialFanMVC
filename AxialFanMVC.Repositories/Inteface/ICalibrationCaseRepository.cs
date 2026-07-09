using AxialFanMVC.Database;

namespace AxialFanMVC.Repositories.Inteface
{
    public interface ICalibrationCaseRepository
    {
        // Dataset is expected to stay small (manually curated calibration
        // library, not per-design data) — fetching all with points and
        // matching in memory is fine here. If this grows into the
        // thousands, revisit with DB-side pre-filtering by SpecificSpeed
        // range before pulling full records.
        Task<List<CalibrationCase>> GetAllWithPointsAsync();
    }
}