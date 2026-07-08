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
    public class DesignController : Controller
    {
        private readonly AxialFanDbContext _db;
        private readonly IWebHostEnvironment _env;
        public DesignController(AxialFanDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Design/Wizard?projectId=5&step=1
        public async Task<IActionResult> Wizard(int projectId, int step = 1)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            var profiles = await _db.blade_profiles.OrderBy(b => b.Name).ToListAsync();

            // Load from TempData if navigating between steps
            var vm = TempData.ContainsKey("WizardData")
                ? System.Text.Json.JsonSerializer
                    .Deserialize<DesignWizardViewModel>(TempData["WizardData"]!.ToString()!)
                  ?? new DesignWizardViewModel()
                : new DesignWizardViewModel();

            // Backfill standard Air Input defaults if a stale/older TempData
            // session left these fields null (e.g. before defaults existed).
            // Backfill standard Air Input defaults if a stale/older TempData
            // session left these fields null (e.g. before defaults existed).
            vm.AltitudeM ??= 0;
            vm.AtmosphericPressureKPa ??= 101.325;
            vm.RelativeHumidityPct ??= 50;

            // Backfill standard Fan Constraints defaults, same reasoning.
            vm.MaxTipDiameterMm ??= 1000;
            vm.MinEfficiencyPct ??= 65;
            vm.MaxNoiseDbA ??= 85;
            vm.MaxMotorPowerKw ??= 15;
            vm.PreferredBladeCount ??= 8;
            vm.MaxSpeedRpm ??= 1450;

            vm.ProjectId = projectId;
            vm.ProjectName = project.Name;
            vm.CurrentStep = Math.Clamp(step, 1, 9);
            vm.BladeProfiles = profiles;
            // Keep TempData alive for next request
            TempData.Keep("WizardData");

            return View("Wizard", vm);
        }


        // POST /Design/Wizard — handles Next, Back and Calculate
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Wizard(DesignWizardViewModel vm, string action)
        {
            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == vm.ProjectId && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            // Save current form data into TempData so it survives the redirect
            vm.BladeProfiles = new(); // don't serialize the list
            TempData["WizardData"] = System.Text.Json.JsonSerializer.Serialize(vm);

            // Next button — go to next step
            if (action == "next" && vm.CurrentStep < 9)
            {
                return RedirectToAction(nameof(Wizard), new
                {
                    projectId = vm.ProjectId,
                    step = vm.CurrentStep + 1
                });
            }

            // Back button — go to previous step
            if (action == "back" && vm.CurrentStep > 1)
            {
                return RedirectToAction(nameof(Wizard), new
                {
                    projectId = vm.ProjectId,
                    step = vm.CurrentStep - 1
                });
            }

            // Calculate button (step 8 — Output & Save — submit)
            if (action == "calculate")
            {
                // Reload blade profiles for validation display
                vm.BladeProfiles = await _db.blade_profiles.OrderBy(b => b.Name).ToListAsync();

                // Basic validation — both live on step 2 (Air Input) now
                if (vm.FlowRateM3s <= 0)
                {
                    ModelState.AddModelError("FlowRateM3s", "Flow rate must be greater than 0.");
                    vm.ProjectName = project.Name;
                    vm.CurrentStep = 2;
                    TempData["WizardData"] = System.Text.Json.JsonSerializer.Serialize(vm);
                    return RedirectToAction(nameof(Wizard), new { projectId = vm.ProjectId, step = 2 });
                }

                if (vm.TotalPressurePa <= 0)
                {
                    vm.ProjectName = project.Name;
                    vm.CurrentStep = 2;
                    TempData["WizardData"] = System.Text.Json.JsonSerializer.Serialize(vm);
                    return RedirectToAction(nameof(Wizard), new { projectId = vm.ProjectId, step = 2 });
                }

                // Clear TempData
                TempData.Remove("WizardData");

                // Project Info (step 1) — written onto the Project entity, not DesignInput
                project.Client = vm.Client;
                project.Application = vm.Application;
                project.Engineer = vm.Engineer;
                project.JobDate = vm.JobDate;
                project.UpdatedAt = DateTime.UtcNow;

                var input = new DesignInput
                {
                    ProjectId = vm.ProjectId,
                    BladeProfileId = vm.BladeProfileId,
                    MediaType = vm.MediaType,
                    TemperatureCelsius = vm.TemperatureCelsius,
                    InletPressurePa = vm.InletPressurePa,
                    DensityKgM3 = vm.DensityKgM3,
                    AltitudeM = vm.AltitudeM,
                    AtmosphericPressureKPa = vm.AtmosphericPressureKPa,
                    RelativeHumidityPct = vm.RelativeHumidityPct,
                    Direction = vm.Direction,
                    InstallationType = vm.InstallationType,
                    Duty = vm.Duty,
                    FrequencyHz = vm.FrequencyHz,
                    MaxTipDiameterMm = vm.MaxTipDiameterMm,
                    MinEfficiencyPct = vm.MinEfficiencyPct,
                    MaxNoiseDbA = vm.MaxNoiseDbA,
                    MaxMotorPowerKw = vm.MaxMotorPowerKw,
                    PreferredBladeCount = vm.PreferredBladeCount,
                    MaxSpeedRpm = vm.MaxSpeedRpm,
                    FlowRateM3s = vm.FlowRateM3s,
                    StaticPressurePa = vm.StaticPressurePa,
                    TotalPressurePa = vm.TotalPressurePa,
                    SpeedRpm = vm.SpeedRpm,
                    MotorPoles = vm.MotorPoles,
                    MotorType = vm.MotorType,
                    VoltageSpec = vm.VoltageSpec,
                    InsulationClass = vm.InsulationClass,
                    StartingMethod = vm.StartingMethod,
                    AccInletGuard = vm.AccInletGuard,
                    AccOutletGuard = vm.AccOutletGuard,
                    AccVibrationIsolators = vm.AccVibrationIsolators,
                    AccFlexibleConnector = vm.AccFlexibleConnector,
                    AccSilencer = vm.AccSilencer,
                    AccBackdraftDamper = vm.AccBackdraftDamper,
                    AccessoryNotes = vm.AccessoryNotes,
                    BladeCount = vm.BladeCount,
                    TipDiameterMm = vm.TipDiameterMm,
                    HubRatio = vm.HubRatio,
                    BladeAngleDeg = vm.BladeAngleDeg,
                    TargetEfficiencyPct = vm.TargetEfficiencyPct,
                    MotorPowerKw = vm.MotorPowerKw,
                    HubDiameterMm = vm.HubDiameterMm,
                    RangeMinFlowM3h = vm.RangeMinFlowM3h,
                    RangeMaxFlowM3h = vm.RangeMaxFlowM3h,
                    RangeMinPressurePa = vm.RangeMinPressurePa,
                    RangeMaxPressurePa = vm.RangeMaxPressurePa,
                    RangeMinSpeedRpm = vm.RangeMinSpeedRpm,
                    RangeMaxSpeedRpm = vm.RangeMaxSpeedRpm,
                    DriveType = vm.DriveType,
                    MotorRpm = vm.MotorRpm,
                    FanRpm = vm.FanRpm,
                    BeltType = vm.BeltType,
                    PulleyRatio = vm.PulleyRatio,
                    NumberOfBelts = vm.NumberOfBelts,
                    CentreDistanceMm = vm.CentreDistanceMm,
                    VfdMinRpm = vm.VfdMinRpm,
                    VfdMaxRpm = vm.VfdMaxRpm,
                    VfdSpeedPct = vm.VfdSpeedPct,
                    CapacityFlowM3h = vm.CapacityFlowM3h,
                    CapacityStaticPa = vm.CapacityStaticPa,
                    CapacitySpeedRpm = vm.CapacitySpeedRpm,
                    CapacityMotorKw = vm.CapacityMotorKw,
                    CapacityEfficiencyPct = vm.CapacityEfficiencyPct,
                    DrawingTagNo = vm.DrawingTagNo,
                    NameplateText = vm.NameplateText,
                    ReceiverDistanceM = vm.ReceiverDistanceM,
                    AcousticEnvironment = vm.AcousticEnvironment,
                    DirectivityIndexDb = vm.DirectivityIndexDb,
                    InletAttenuationDb = vm.InletAttenuationDb,
                    OutletAttenuationDb = vm.OutletAttenuationDb,
                    CasingTransmissionLossDb = vm.CasingTransmissionLossDb,
                    SilencerAttenuationDb = vm.SilencerAttenuationDb,
                    RoomCorrectionDb = vm.RoomCorrectionDb,
                    BackgroundNoiseDbA = vm.BackgroundNoiseDbA,
                    SafetyMarginDb = vm.SafetyMarginDb,
                };
                // ── Everything from here down is one unit of work: if any
                // DB write fails, nothing partial is left behind. ─────────
                await using var transaction = await _db.Database.BeginTransactionAsync();
                DesignResult result;
                try
                {
                    _db.design_inputs.Add(input);

                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        return Content(ex.ToString(), "text/plain");
                    }

                    // Run calculations
                    var aero = AeroCalcEngine.Calculate(input);
                    var struct_ = StructCalcEngine.Calculate(input, aero);
                    var sound = SoundCalcEngine.Calculate(input, aero);

                    var allWarnings = new List<string>();

                    allWarnings.AddRange(aero.Warnings);
                    allWarnings.AddRange(struct_.Warnings);
                    allWarnings.AddRange(sound.Warnings);

                    result = new DesignResult
                    {
                        DesignInputId = input.Id,

                        // Aerodynamic
                        SpecificSpeed = Math.Round(aero.SpecificSpeed, 4),
                        TipSpeedMs = Math.Round(aero.TipSpeedMs, 2),
                        HubDiameterMm = Math.Round(aero.HubDiameterMm, 1),
                        ChordLengthMm = Math.Round(aero.ChordLengthMm, 1),
                        BladeSpanMm = Math.Round(aero.BladeSpanMm, 1),
                        ShaftPowerKw = Math.Round(aero.ShaftPowerKw, 3),
                        OverallEfficiencyPct = Math.Round(aero.OverallEfficiencyPct, 2),
                        FlowCoefficient = Math.Round(aero.FlowCoefficient, 4),
                        PressureCoefficient = Math.Round(aero.PressureCoefficient, 4),
                        TipClearanceMm = aero.TipClearanceMm,

                        // Structural
                        BladeStressMpa = Math.Round(struct_.TotalStressMpa, 2),
                        SafetyFactor = struct_.SafetyFactor,

                        // ===============================
                        // NEW SOUND RESULTS
                        // ===============================

                        OverallNoiseDbA = (float)sound.LpOverallDba,
                        SoundPowerLevelDb = (float)sound.LwOverallDb,
                        BladePassingFrequencyHz = (float)sound.BpfHz,
                        TipMachNumber = (float)sound.TipMachNumber,
                        NoiseRatingValue = sound.NrValue,
                        NoiseRating = sound.NoiseRating,
                        OctaveBandLwJson = JsonSerializer.Serialize(sound.OctaveBandLwDb),

                        Status = allWarnings.Count == 0 ? "ok" : "warning",
                        WarningMessages = JsonSerializer.Serialize(allWarnings)
                    };
                    _db.design_results.Add(result);
                    await _db.SaveChangesAsync();

                    var bladeProfile = input.BladeProfileId.HasValue
                    ? await _db.blade_profiles.FindAsync(input.BladeProfileId.Value)
                    : null;
                    var profileData = BladeProfileEngine.ResolveProfileData(bladeProfile, aero.ChordLengthMm);

                    var curveData = AeroCalcEngine.GenerateCurves(input, aero, profileData, input.BladeAngleDeg, input.SpeedRpm);

                   
                    _db.performance_curves.Add(new PerformanceCurve
                    {
                        DesignResultId = result.Id,
                        BladeAngleDeg = curveData.BladeAngleDeg,
                        SpeedRpm = curveData.SpeedRpm,
                        QValues = string.Join(",", curveData.QValues),
                        DpValues = string.Join(",", curveData.DpValues),
                        EtaValues = string.Join(",", curveData.EtaValues),
                        KwValues = string.Join(",", curveData.KwValues)
                    });
                    await _db.SaveChangesAsync();

                    // ── Generate all 7 DXF drawings automatically ─────────
                    // Note: file writes here are not covered by the DB transaction
                    // (disk I/O can't be rolled back). If a drawing throws, we log it
                    // and continue — the design/result/curve are still committed.
                    // A drawing failure does not currently retry or clean up any
                    // files already written; that remains a known limitation.
                    try
                    {
                        var exportDir = Path.Combine(_env.ContentRootPath, "wwwroot", "exports");
                        Directory.CreateDirectory(exportDir);

                        var drawings = new[]
                        {
                            ("front_elevation",     $"DWG001_FrontElev_{result.Id}.dxf",     (Func<byte[]>)(() => AxialFanDrawingService.FrontElevationDxf(input, result))),
                            ("cross_section",       $"DWG002_CrossSection_{result.Id}.dxf",  () => AxialFanDrawingService.CrossSectionDxf(input, result)),
                            ("blade_profile",       $"DWG003_BladeProfile_{result.Id}.dxf",  () => AxialFanDrawingService.BladeProfileDxf(input, result)),
                            ("blade_angle",         $"DWG004_BladeAngle_{result.Id}.dxf",    () => AxialFanDrawingService.BladeAngleDxf(input, result)),
                            ("hub_detail",          $"DWG005_HubDetail_{result.Id}.dxf",     () => AxialFanDrawingService.HubDetailDxf(input, result)),
                            ("casing_detail",       $"DWG006_CasingDetail_{result.Id}.dxf",  () => AxialFanDrawingService.CasingDetailDxf(input, result)),
                            ("general_arrangement", $"DWG007_GenArrangement_{result.Id}.dxf",() => AxialFanDrawingService.GeneralArrangementDxf(input, result)),
                        };

                        foreach (var (drawingType, fileName, generate) in drawings)
                        {
                            var bytes = generate();
                            var filePath = Path.Combine(exportDir, fileName);
                            await System.IO.File.WriteAllBytesAsync(filePath, bytes);
                            _db.drawings.Add(new Drawing
                            {
                                DesignResultId = result.Id,
                                DrawingType = drawingType,
                                DxfPath = filePath,
                                SvgData = null
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Drawing generation failed: {ex.Message}");
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    var message = ex.ToString();

                    if (ex.InnerException != null)
                    {
                        message += Environment.NewLine;
                        message += "================ INNER EXCEPTION ================";
                        message += Environment.NewLine;
                        message += ex.InnerException.ToString();
                    }

                    Console.WriteLine(message);
                    System.Diagnostics.Debug.WriteLine(message);

                    TempData["Error"] = message;

                    return Content(message, "text/plain");
                }

                TempData["Success"] = "Design calculated successfully!";
                return RedirectToAction("Result", "Results", new { resultId = result.Id });
            }

            return RedirectToAction(nameof(Wizard), new { projectId = vm.ProjectId, step = vm.CurrentStep });
        }

        // GET /Design/History/5

        public async Task<IActionResult> History(int projectId)
        {
            if (projectId <= 0)
                return BadRequest("Invalid projectId");

            var project = await _db.Projects
                .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == CurrentUserId);
            if (project == null) return NotFound();

            var designs = await _db.design_inputs
                .AsNoTracking()
                .Include(d => d.BladeProfile)
                .Include(d => d.DesignResult)
                .Where(x => x.ProjectId == projectId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            var vm = new DesignHistoryViewModel
            {
                ProjectId = projectId,
                ProjectName = project.Name,
                Designs = designs.Select(d => new DesignSummaryViewModel
                {
                    DesignInputId = d.Id,
                    ResultId = d.DesignResult?.Id,
                    MediaType = d.MediaType,
                    FlowRateM3s = d.FlowRateM3s,
                    TotalPressurePa = d.TotalPressurePa,
                    SpeedRpm = d.SpeedRpm,
                    TipDiameterMm = d.TipDiameterMm,
                    BladeCount = d.BladeCount,
                    BladeProfileName = d.BladeProfile?.Name,
                    Status = d.DesignResult?.Status ?? "pending",
                    CreatedAt = d.CreatedAt,
                    OverallEfficiencyPct = d.DesignResult?.OverallEfficiencyPct,
                    ShaftPowerKw = d.DesignResult?.ShaftPowerKw,
                    HubRatio = d.HubRatio,
                    BladeAngleDeg = d.BladeAngleDeg,
                    MotorType = d.MotorType,
                    DriveType = d.DriveType ?? "Direct Drive"
                }).ToList()
            };

            return View(vm);
        }

        // GET /Design/EditInput/5 — reopen an existing design for editing.
        // Submitting the wizard afterwards creates a NEW DesignInput + DesignResult
        // (this does not modify or delete the original record).
        public async Task<IActionResult> EditInput(int designInputId)
        {
            var input = await _db.design_inputs
                .Include(d => d.Project)
                .FirstOrDefaultAsync(d => d.Id == designInputId && d.Project.UserId == CurrentUserId);
            if (input == null) return NotFound();

            var vm = new DesignWizardViewModel
            {
                ProjectId = input.ProjectId,
                ProjectName = input.Project.Name,
                CurrentStep = 1,
                MediaType = input.MediaType,
                TemperatureCelsius = input.TemperatureCelsius,
                InletPressurePa = input.InletPressurePa,
                DensityKgM3 = input.DensityKgM3,
                FlowRateM3s = input.FlowRateM3s,
                StaticPressurePa = input.StaticPressurePa,
                TotalPressurePa = input.TotalPressurePa,
                SpeedRpm = input.SpeedRpm,
                MotorPoles = input.MotorPoles,
                BladeCount = input.BladeCount,
                TipDiameterMm = input.TipDiameterMm,
                HubRatio = input.HubRatio,
                BladeAngleDeg = input.BladeAngleDeg,
                TargetEfficiencyPct = input.TargetEfficiencyPct,
                MotorPowerKw = input.MotorPowerKw,
                BladeProfileId = input.BladeProfileId,
                // Step 6 fields below (Motor Type onward) were previously
                // missing from this reopen mapping, so re-editing a saved
                // design silently reset them to blank/default — the same
                // class of data-loss bug fixed earlier for MotorPowerKw/
                // SpeedRpm/MotorPoles. Preserving them here now that the
                // Drive Configuration UI actually uses them.
                MotorType = input.MotorType,
                VoltageSpec = input.VoltageSpec,
                InsulationClass = input.InsulationClass,
                StartingMethod = input.StartingMethod,
                DriveType = input.DriveType,
                MotorRpm = input.MotorRpm,
                FanRpm = input.FanRpm,
                BeltType = input.BeltType,
                PulleyRatio = input.PulleyRatio,
                NumberOfBelts = input.NumberOfBelts,
                CentreDistanceMm = input.CentreDistanceMm,
                ReceiverDistanceM = input.ReceiverDistanceM ?? 1.0,
                AcousticEnvironment = input.AcousticEnvironment,
                DirectivityIndexDb = input.DirectivityIndexDb ?? 3,
                InletAttenuationDb = input.InletAttenuationDb ?? 0,
                OutletAttenuationDb = input.OutletAttenuationDb ?? 0,
                CasingTransmissionLossDb = input.CasingTransmissionLossDb ?? 18,
                SilencerAttenuationDb = input.SilencerAttenuationDb ?? 0,
                RoomCorrectionDb = input.RoomCorrectionDb ?? 0,
                BackgroundNoiseDbA = input.BackgroundNoiseDbA,
                SafetyMarginDb = input.SafetyMarginDb ?? 3,
                VfdMinRpm = input.VfdMinRpm,
                VfdMaxRpm = input.VfdMaxRpm,
                //VfdSpeedPct = input.VfdSpeedPct
            };

            TempData["WizardData"] = System.Text.Json.JsonSerializer.Serialize(vm);
            TempData["Success"] = "Design loaded for editing — submitting will save it as a new design revision.";

            return RedirectToAction(nameof(Wizard), new { projectId = input.ProjectId, step = 1 });
        }
    }
}