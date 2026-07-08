using AxialFanMVC.Database;
using AxialFanMVC.Services;
using AxialFanMVC.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class ResultsController : Controller
    {
        private readonly AxialFanDbContext _db;
        public ResultsController(AxialFanDbContext db) => _db = db;
           
        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Results/Result/7
        public async Task<IActionResult> Result(int resultId)
        {
            var result = await _db.design_results
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.Project)
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.BladeProfile)
                .Include(r => r.PerformanceCurves)
                .Include(r => r.Drawings)
                .FirstOrDefaultAsync(r => r.Id == resultId &&
                                          r.DesignInput.Project.UserId == CurrentUserId);

            if (result == null) return NotFound();

            var warnings = result.WarningMessages != null
                ? JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new()
                : new List<string>();

            // Build curve JSON for Chart.js

            var baselineCurve = result.PerformanceCurves
                .FirstOrDefault(x => x.Source == "Baseline");

            var correctedCurve = result.PerformanceCurves
                .FirstOrDefault(x => x.Source == "PINN");

            string curveJson = "{}";

            if (baselineCurve != null)
            {
                curveJson = JsonSerializer.Serialize(new
                {
                    baseline = new
                    {
                        q = baselineCurve.QValues.Split(',').Select(double.Parse),
                        dp = baselineCurve.DpValues.Split(',').Select(double.Parse),
                        eta = baselineCurve.EtaValues.Split(',').Select(double.Parse),
                        kw = baselineCurve.KwValues.Split(',').Select(double.Parse)
                    },

                    corrected = correctedCurve == null ? null : new
                    {
                        q = correctedCurve.QValues.Split(',').Select(double.Parse),
                        dp = correctedCurve.DpValues.Split(',').Select(double.Parse),
                        eta = correctedCurve.EtaValues.Split(',').Select(double.Parse),
                        kw = correctedCurve.KwValues.Split(',').Select(double.Parse)
                    }
                });
            }

            var di = result.DesignInput;

            var vm = new DesignResultViewModel
            {
                DesignInputId = di.Id,
                ResultId = result.Id,
                ProjectId = di.ProjectId,
                ProjectName = di.Project.Name,

                FlowRateM3s = di.FlowRateM3s,
                TotalPressurePa = di.TotalPressurePa,
                SpeedRpm = di.SpeedRpm,
                BladeCount = di.BladeCount,
                TipDiameterMm = di.TipDiameterMm,
                BladeAngleDeg = di.BladeAngleDeg,
                BladeProfileName = di.BladeProfile?.Name,

                // Aero
                SpecificSpeed = result.SpecificSpeed,
                TipSpeedMs = result.TipSpeedMs,
                HubDiameterMm = result.HubDiameterMm,
                ChordLengthMm = result.ChordLengthMm,
                BladeSpanMm = result.BladeSpanMm,
                ShaftPowerKw = result.ShaftPowerKw,
                OverallEfficiencyPct = result.OverallEfficiencyPct,
                FlowCoefficient = result.FlowCoefficient,
                PressureCoefficient = result.PressureCoefficient,
                TipClearanceMm = result.TipClearanceMm,

                // Structural
                BladeStressMpa = result.BladeStressMpa,
                SafetyFactor = result.SafetyFactor,

                // Acoustic
                OverallNoiseDbA = result.OverallNoiseDbA,
                SoundPowerLevelDb = result.SoundPowerLevelDb,
                BladePassingFrequencyHz = result.BladePassingFrequencyHz,
                TipMachNumber = result.TipMachNumber,
                NoiseRatingValue = result.NoiseRatingValue,
                NoiseRating = result.NoiseRating,
                OctaveBandLwJson = result.OctaveBandLwJson,

                Status = result.Status,
                Warnings = warnings,
                CalculatedAt = result.CalculatedAt,
                CurveJson = curveJson,

                Drawings = result.Drawings.Select(d => new DrawingViewModel
                {
                    Id = d.Id,
                    DrawingType = d.DrawingType,
                    SvgData = d.SvgData,
                    HasDxf = d.DxfPath != null,
                    HasPdf = d.PdfPath != null,
                    GeneratedAt = d.GeneratedAt
                }).ToList()
            };

            return View(vm);
        }

        // POST /Results/GenerateCurve  — AJAX endpoint
        [HttpPost]
        public async Task<IActionResult> GenerateCurve(int resultId, double bladeAngleDeg, int speedRpm)
        {
            var result = await _db.design_results
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.Project)
                .FirstOrDefaultAsync(r => r.Id == resultId &&
                                          r.DesignInput.Project.UserId == CurrentUserId);
            if (result == null) return NotFound();
            var aero = AeroCalcEngine.Calculate(result.DesignInput);
            var bladeProfile = result.DesignInput.BladeProfileId.HasValue
                ? await _db.blade_profiles.FindAsync(result.DesignInput.BladeProfileId.Value)
                : null;
            var profileData = BladeProfileEngine.ResolveProfileData(bladeProfile, aero.ChordLengthMm);



            // Baseline curve (pure equations)
            var baseline = AeroCalcEngine.GenerateCurves(
                 result.DesignInput,
                 aero,
                 profileData,
                 bladeAngleDeg,
                 speedRpm);

            // PINN / AI corrected curve
            var corrected = AeroCalcEngine.GenerateCorrectedCurves(
                result.DesignInput,
                aero,
                profileData,
                bladeAngleDeg,
                speedRpm);

            _db.performance_curves.Add(new PerformanceCurve
            {
                DesignResultId = resultId,
                BladeAngleDeg = baseline.BladeAngleDeg,
                SpeedRpm = baseline.SpeedRpm,

                Source = "Baseline",

                QValues = string.Join(",", baseline.QValues),
                DpValues = string.Join(",", baseline.DpValues),
                EtaValues = string.Join(",", baseline.EtaValues),
                KwValues = string.Join(",", baseline.KwValues)
            });

            _db.performance_curves.Add(new PerformanceCurve
            {
                DesignResultId = resultId,
                BladeAngleDeg = corrected.BladeAngleDeg,
                SpeedRpm = corrected.SpeedRpm,

                Source = "PINN",

                QValues = string.Join(",", corrected.QValues),
                DpValues = string.Join(",", corrected.DpValues),
                EtaValues = string.Join(",", corrected.EtaValues),
                KwValues = string.Join(",", corrected.KwValues)
            });
            await _db.SaveChangesAsync();

            return Json(new
            {
                baseline = new
                {
                    q = baseline.QValues,
                    dp = baseline.DpValues,
                    eta = baseline.EtaValues,
                    kw = baseline.KwValues
                },

                corrected = new
                {
                    q = corrected.QValues,
                    dp = corrected.DpValues,
                    eta = corrected.EtaValues,
                    kw = corrected.KwValues
                }
            });
        }

        // GET /Results/Download/7/dxf
        public async Task<IActionResult> Download(int drawingId, string format)
        {
            var drawing = await _db.drawings
                .Include(d => d.DesignResult)
                    .ThenInclude(r => r.DesignInput)
                        .ThenInclude(di => di.Project)
                .FirstOrDefaultAsync(d => d.Id == drawingId &&
                                          d.DesignResult.DesignInput.Project.UserId == CurrentUserId);
            if (drawing == null) return NotFound();

            string? path = format.ToLower() switch
            {
                "dxf" => drawing.DxfPath,
                "pdf" => drawing.PdfPath,
                _ => null
            };

            if (path == null || !System.IO.File.Exists(path))
                return NotFound("File not yet generated.");

            var mimeType = format.ToLower() == "pdf" ? "application/pdf" : "application/dxf";
            return PhysicalFile(path, mimeType, Path.GetFileName(path));
        }
    }
}
