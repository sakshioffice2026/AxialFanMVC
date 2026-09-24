using System.Text.Json;
using AxialFanMVC.Database;
using AxialFanMVC.Models;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services
{
    public sealed class AgentActionExecutor : IAgentActionExecutor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAgentPendingActionStore _store;
        private readonly AxialFanDbContext _db;
        private readonly ICfdJobSignal _cfdSignal;
        private readonly IOptimizationJobSignal _optimizationSignal;
        private readonly ICalibrationCaseRepository _calibrationRepo;
        private readonly ICurveGeneration _curveService;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
        private readonly IWebHostEnvironment _env;

        public AgentActionExecutor(
            IAgentPendingActionStore store,
            AxialFanDbContext db,
            ICfdJobSignal cfdSignal,
            IOptimizationJobSignal optimizationSignal,
            ICalibrationCaseRepository calibrationRepo,
            ICurveGeneration curveService,
            IExceptionHandlerRepository exceptionHandlerRepository,
            IWebHostEnvironment env)
        {
            _store = store;
            _db = db;
            _cfdSignal = cfdSignal;
            _optimizationSignal = optimizationSignal;
            _calibrationRepo = calibrationRepo;
            _curveService = curveService;
            _exceptionHandlerRepository = exceptionHandlerRepository;
            _env = env;
        }

        public bool Cancel(string actionId, int userId)
            => _store.Cancel(actionId, userId);

        public async Task<AgentActionExecutionResult> ConfirmAndExecuteAsync(string actionId, int userId)
        {
            var action = _store.TryConfirm(actionId, userId);

            if (action is null)
            {
                return Fail(string.Empty, "Action not found, already handled, or expired.");
            }

            try
            {
                return action.ActionType switch
                {
                    AgentActionTypes.TriggerCfdRun => await RunCfdAsync(action, userId),
                    AgentActionTypes.TriggerOptimization => await RunOptimizationAsync(action, userId),
                    AgentActionTypes.CreateNewDesign => await CreateDesignAsync(action, userId),
                    _ => Fail(action.ActionType, "Unknown action type.")
                };
            }
            catch (Exception ex)
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AgentActionExecutor),
                    nameof(ConfirmAndExecuteAsync),
                    ex.ToString(),
                    userId);

                return Fail(action.ActionType, "The action failed while executing. It has been logged.");
            }
        }

        private async Task<AgentActionExecutionResult> RunCfdAsync(AgentPendingAction action, int userId)
        {
            var p = JsonSerializer.Deserialize<CfdParams>(action.ParametersJson, JsonOptions);

            if (p is null || p.ResultId <= 0)
                return Fail(action.ActionType, "Invalid action parameters.");

            var owned = await _db.design_results
                .AnyAsync(r => r.Id == p.ResultId && r.DesignInput.Project.UserId == userId);

            if (!owned)
                return Fail(action.ActionType, "Design result not found for the current user.");

            var job = new CfdJob
            {
                ResultId = p.ResultId,
                UserId = userId,
                Status = "Queued"
            };

            _db.cfd_jobs.Add(job);
            await _db.SaveChangesAsync();

            _cfdSignal.NotifyJobQueued(job.Id);

            return new AgentActionExecutionResult
            {
                Success = true,
                ActionType = action.ActionType,
                Message = $"CFD run queued for design result #{p.ResultId}.",
                JobId = job.Id,
                ResultId = p.ResultId
            };
        }

        private async Task<AgentActionExecutionResult> RunOptimizationAsync(AgentPendingAction action, int userId)
        {
            var p = JsonSerializer.Deserialize<OptimizationParams>(action.ParametersJson, JsonOptions);

            if (p is null || p.ProjectId <= 0)
                return Fail(action.ActionType, "Invalid action parameters.");

            var input = await _db.design_inputs
                .Where(d => d.ProjectId == p.ProjectId && d.Project.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefaultAsync();

            if (input is null)
                return Fail(action.ActionType, "No design found for this project to optimize from.");

            if (input.FlowRateM3s <= 0 || input.TotalPressurePa <= 0)
                return Fail(action.ActionType, "The design needs a valid flow rate and pressure duty point before optimizing.");

            var request = new OptimizeRequestDto
            {
                FlowRateM3s = input.FlowRateM3s,
                TotalPressurePa = input.TotalPressurePa,
                TemperatureCelsius = input.TemperatureCelsius,
                MinEfficiencyPct = input.MinEfficiencyPct,
                MaxNoiseDbA = input.MaxNoiseDbA,
                MaxMotorPowerKw = input.MaxMotorPowerKw,
                MaxTipDiameterMm = input.MaxTipDiameterMm
            };

            var job = new OptimizationJob
            {
                ProjectId = p.ProjectId,
                UserId = userId,
                Status = "Queued",
                RequestJson = JsonSerializer.Serialize(request)
            };

            _db.optimization_jobs.Add(job);
            await _db.SaveChangesAsync();

            _optimizationSignal.NotifyJobQueued(job.Id);

            return new AgentActionExecutionResult
            {
                Success = true,
                ActionType = action.ActionType,
                Message = $"Optimization queued for project #{p.ProjectId}.",
                JobId = job.Id
            };
        }

        private async Task<AgentActionExecutionResult> CreateDesignAsync(AgentPendingAction action, int userId)
        {
            var p = JsonSerializer.Deserialize<NewDesignParams>(action.ParametersJson, JsonOptions);

            if (p is null || p.ProjectId <= 0 || p.FlowRateM3s <= 0 || p.TotalPressurePa <= 0)
                return Fail(action.ActionType, "Invalid action parameters.");

            var project = await _db.Projects
                .FirstOrDefaultAsync(x => x.Id == p.ProjectId && x.UserId == userId);

            if (project is null)
                return Fail(action.ActionType, "Project not found for the current user.");

            project.UpdatedAt = DateTime.UtcNow;

            var input = new DesignInput
            {
                ProjectId = p.ProjectId,
                TemperatureCelsius = p.TemperatureCelsius,
                FlowRateM3s = p.FlowRateM3s,
                StaticPressurePa = p.StaticPressurePa > 0 ? p.StaticPressurePa : p.TotalPressurePa,
                TotalPressurePa = p.TotalPressurePa,
                SpeedRpm = p.SpeedRpm,
                BladeCount = p.BladeCount,
                TipDiameterMm = p.TipDiameterMm
            };

            if (p.HubRatio is > 0)
                input.HubRatio = p.HubRatio.Value;

            if (p.BladeAngleDeg is > 0)
                input.BladeAngleDeg = p.BladeAngleDeg.Value;

            if (p.TargetEfficiencyPct is > 0)
                input.TargetEfficiencyPct = p.TargetEfficiencyPct.Value;

            if (p.MotorPowerKw is > 0)
                input.MotorPowerKw = p.MotorPowerKw.Value;

            await using var transaction = await _db.Database.BeginTransactionAsync();
            DesignResult result;

            try
            {
                _db.design_inputs.Add(input);
                await _db.SaveChangesAsync();

                double provisionalChordMm = AeroCalcEngine.ComputeMeanChordMm(
                    input.TipDiameterMm, input.HubRatio, input.BladeCount);

                var profileData = BladeProfileEngine.ResolveProfileData(null, provisionalChordMm);

                var calibrationCandidates = await _calibrationRepo.GetAllWithPointsAsync();
                var aero = AeroCalcEngine.Calculate(input, profileData, calibrationCandidates);

                await _db.SaveChangesAsync();

                var struct_ = StructCalcEngine.Calculate(input, aero);
                var sound = SoundCalcEngine.Calculate(input, aero);

                var allWarnings = new List<string>();
                allWarnings.AddRange(aero.Warnings);
                allWarnings.AddRange(struct_.Warnings);
                allWarnings.AddRange(sound.Warnings);

                result = new DesignResult
                {
                    DesignInputId = input.Id,

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

                    BladeStressMpa = Math.Round(struct_.TotalStressMpa, 2),
                    SafetyFactor = struct_.SafetyFactor,
                    MaterialUsed = struct_.MaterialUsed,
                    YieldStrengthMpa = Math.Round(struct_.YieldStrengthMpa, 1),

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

                await _curveService.GenerateAndSaveAsync(
                    result.Id, userId, input.BladeAngleDeg, input.SpeedRpm);

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
                        await File.WriteAllBytesAsync(filePath, bytes);

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
                    _exceptionHandlerRepository.SaveException(
                        nameof(AgentActionExecutor),
                        nameof(CreateDesignAsync),
                        ex.ToString(),
                        userId);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return new AgentActionExecutionResult
            {
                Success = true,
                ActionType = action.ActionType,
                Message = $"New design created in project #{p.ProjectId}.",
                ResultId = result.Id,
                DesignInputId = input.Id
            };
        }

        private static AgentActionExecutionResult Fail(string actionType, string message)
            => new()
            {
                Success = false,
                ActionType = actionType,
                Message = message
            };

        private sealed class CfdParams
        {
            public int ResultId { get; set; }
        }

        private sealed class OptimizationParams
        {
            public int ResultId { get; set; }
            public int ProjectId { get; set; }
        }

        private sealed class NewDesignParams
        {
            public int ProjectId { get; set; }
            public double FlowRateM3s { get; set; }
            public double TotalPressurePa { get; set; }
            public double StaticPressurePa { get; set; }
            public int SpeedRpm { get; set; } = 1450;
            public int BladeCount { get; set; } = 6;
            public double TipDiameterMm { get; set; } = 1000;
            public double TemperatureCelsius { get; set; } = 25;
            public double? HubRatio { get; set; }
            public double? BladeAngleDeg { get; set; }
            public double? TargetEfficiencyPct { get; set; }
            public double? MotorPowerKw { get; set; }
            public string? ApplicationDescription { get; set; }
            public string? PressureClass { get; set; }
        }
    }
}