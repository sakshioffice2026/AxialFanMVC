using AxialFanMVC.Database;
using AxialFanMVC.Services.MLOptimization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
    // Admin/engineering tool — runs the existing deterministic engines
    // (Aero -> Struct -> Sound -> Bom) across a Latin-Hypercube-sampled
    // design space and streams the result back as a CSV. That CSV is
    // what train_surrogate.py (AxialFanMVC.Business/MLOptimization)
    // trains the Random Forest surrogate on — this endpoint is Part 1
    // of the "Optimize for me" pipeline, Parts 2/3 live in the Python
    // optimizer service and OptimizationBackgroundService respectively.
    [Authorize]
    public class DataFactoryController : Controller
    {
        private readonly AxialFanDbContext _db;

        public DataFactoryController(AxialFanDbContext db)
        {
            _db = db;
        }

        // GET /DataFactory/Generate?sampleCount=2000&seed=42
        // Streams a CSV of sampleCount synthetic (geometry+duty point) ->
        // (efficiency, power, noise, safety factor, cost, feasibility) rows.
        // No request body/form needed — bounds default to the surrogate's
        // full valid domain (SyntheticDataFactory.Bounds.Default).
        [HttpGet]
        public async Task<IActionResult> Generate(int sampleCount = 2000, int? seed = null)
        {
            if (sampleCount is < 1 or > 200_000)
                return BadRequest("sampleCount must be between 1 and 200,000.");

            var stream = new MemoryStream();
            var written = await SyntheticDataFactory.GenerateAsync(
                _db, stream, sampleCount, SyntheticDataFactory.Bounds.Default, seed);

            stream.Position = 0;
            var fileName = $"synthetic_training_data_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(stream, "text/csv", fileName);
        }
    }
}
