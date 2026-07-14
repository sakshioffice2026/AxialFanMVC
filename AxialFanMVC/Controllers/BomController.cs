using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AxialFanMVC.Controllers
{
    // ── BomController ────────────────────────────────────────────────
    // Generates and displays a Bill of Materials + cost estimate for a
    // design result (BomCostingEngine), and lets any signed-in user
    // maintain the shared CostRate table those estimates are priced
    // from. Same ownership-check pattern as ResultsController/
    // ExportController: every result lookup goes through
    // DesignInput -> Project -> UserId so one user can never pull or
    // mutate another user's BOM.
    // ────────────────────────────────────────────────────────────────
    [Authorize]
    public class BomController : Controller
    {
        private readonly IDesignResultRepository _repo;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

        public BomController(IDesignResultRepository repo, IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _repo = repo;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Bom/Index/7  — shows whatever lines already exist for this
        // result, if any. Does NOT auto-generate — first visit shows an
        // empty state with a "Generate" button so a user never gets a
        // silent recalculation they didn't ask for.
        [HttpGet]
        public async Task<IActionResult> Index(int resultId)
        {
            try
            {
                var result = await _repo.GetResultForUserAsync(resultId, CurrentUserId);
                if (result == null) return NotFound();

                var lines = await _repo.GetBomLineItemsAsync(resultId);

                var vm = new BomViewModel
                {
                    ResultId = result.Id,
                    ProjectId = result.DesignInput.ProjectId,
                    ProjectName = result.DesignInput.Project.Name,
                    MaterialUsed = result.MaterialUsed,
                    HasBeenGenerated = lines.Any(),
                    Lines = lines.Select(ToLineVm).ToList(),
                    GrandTotal = lines.Sum(l => l.LineTotal)
                };

                return View(vm);
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(Index), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while loading the BOM.";
                return RedirectToAction("Result", "Results", new { resultId });
            }
        }

        // POST /Bom/Generate/7 — (re)computes the Auto lines from the
        // design's own stored inputs + the current CostRate table, and
        // replaces whatever Auto lines existed before. Manual lines are
        // never touched by this — see ReplaceAutoBomLineItemsAsync.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(int resultId)
        {
            try
            {
                var result = await _repo.GetResultForUserAsync(resultId, CurrentUserId);
                if (result == null) return NotFound();

                // StructCalcEngine only needs ChordLengthMm off the aero
                // result for the mass/stress recompute below — reusing the
                // value already stored on this DesignResult instead of
                // re-running the full aero pipeline (profile resolution,
                // calibration lookup, BEM curve sweep) keeps this a cheap,
                // side-effect-free recompute for a costing pass.
                var aero = new AeroCalcResult { ChordLengthMm = result.ChordLengthMm };
                var structResult = StructCalcEngine.Calculate(result.DesignInput, aero);

                var rates = await _repo.GetCostRatesAsync();
                var bom = BomCostingEngine.Calculate(result.DesignInput, structResult, rates);

                var autoLines = bom.Lines.Select(l => new BomLineItem
                {
                    Category = l.Category,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    Unit = l.Unit,
                    UnitCost = l.UnitCost,
                    LineTotal = l.LineTotal
                }).ToList();

                await _repo.ReplaceAutoBomLineItemsAsync(resultId, autoLines);

                if (bom.Warnings.Any())
                    TempData["Warning"] = string.Join(" | ", bom.Warnings);

                return RedirectToAction(nameof(Index), new { resultId });
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(Generate), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while generating the BOM.";
                return RedirectToAction(nameof(Index), new { resultId });
            }
        }

        // POST /Bom/AddLine — one manual line item (e.g. a real vendor
        // quote for the hub casting), added alongside the Auto lines.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLine(int resultId, string category, string description,
            double quantity, string? unit, double unitCost)
        {
            try
            {
                var result = await _repo.GetResultForUserAsync(resultId, CurrentUserId);
                if (result == null) return NotFound();

                if (string.IsNullOrWhiteSpace(description) || quantity <= 0)
                {
                    TempData["Error"] = "Description and a quantity greater than zero are required for a manual line.";
                    return RedirectToAction(nameof(Index), new { resultId });
                }

                await _repo.AddManualBomLineItemAsync(resultId, CurrentUserId, category, description, quantity, unit, unitCost);
                return RedirectToAction(nameof(Index), new { resultId });
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(AddLine), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while adding the line item.";
                return RedirectToAction(nameof(Index), new { resultId });
            }
        }

        // POST /Bom/DeleteLine — Manual lines only; repository enforces
        // both the ownership check and the Manual-only restriction, so a
        // request for someone else's line, or for an Auto line, quietly
        // no-ops (returns false) rather than throwing.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLine(int lineItemId, int resultId)
        {
            try
            {
                bool deleted = await _repo.DeleteManualBomLineItemAsync(lineItemId, CurrentUserId);
                if (!deleted)
                    TempData["Error"] = "Line item not found, already removed, or not eligible for deletion.";

                return RedirectToAction(nameof(Index), new { resultId });
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(DeleteLine), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while deleting the line item.";
                return RedirectToAction(nameof(Index), new { resultId });
            }
        }

        // GET /Bom/CostRates — shared, app-wide rate table (not
        // per-project/per-user — cost_rates has no user_id column, same
        // as BladeProfile's app-wide lookup pattern), editable by any
        // signed-in user. No separate admin role exists in this app yet.
        [HttpGet]
        public async Task<IActionResult> CostRates()
        {
            try
            {
                var rates = await _repo.GetCostRatesAsync();
                var vm = new CostRatesViewModel
                {
                    Rates = rates.Select(r => new CostRateRowViewModel
                    {
                        Id = r.Id,
                        Category = r.Category,
                        RateKey = r.RateKey,
                        UnitLabel = r.UnitLabel,
                        RateValue = r.RateValue
                    }).ToList()
                };
                return View(vm);
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(CostRates), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while loading cost rates.";
                return RedirectToAction("Index", "Projects");
            }
        }

        // POST /Bom/CostRates — bulk update from the editable table.
        // Deliberately does NOT regenerate any existing BOMs — rates
        // changing shouldn't silently rewrite a BOM someone already
        // reviewed/exported. The user re-runs Generate explicitly per
        // result when they want the new rates applied.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCostRates(List<int> id, List<double> rateValue)
        {
            try
            {
                if (id == null || rateValue == null || id.Count != rateValue.Count)
                {
                    TempData["Error"] = "Malformed cost rate submission.";
                    return RedirectToAction(nameof(CostRates));
                }

                var updates = id.Zip(rateValue, (i, v) => (Id: i, RateValue: v)).ToList();
                await _repo.UpdateCostRatesAsync(updates);

                TempData["Success"] = "Cost rates updated.";
                return RedirectToAction(nameof(CostRates));
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(nameof(BomController), nameof(UpdateCostRates), ex.ToString());
                TempData["Error"] = "An unexpected error occurred while updating cost rates.";
                return RedirectToAction(nameof(CostRates));
            }
        }

        private static BomLineItemViewModel ToLineVm(BomLineItem l) => new()
        {
            Id = l.Id,
            Source = l.Source,
            Category = l.Category,
            Description = l.Description,
            Quantity = l.Quantity,
            Unit = l.Unit,
            UnitCost = l.UnitCost,
            LineTotal = l.LineTotal
        };
    }
}