using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;

namespace AxialFanMVC.Services.AeroAi
{
    // Parameters for one new design. Serialized into the staged "CreateNewDesign" action, and
    // used directly by the in-memory preview. Property names match the executor's JSON.
    public sealed class DesignRunParameters
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

        public string? BladeMaterial { get; set; }
        public double? DensityKgM3 { get; set; }
        public double? AltitudeM { get; set; }
        public double? AtmosphericPressureKPa { get; set; }
        public double? MaxTipDiameterMm { get; set; }
        public int? PreferredBladeCount { get; set; }
        public string? DriveType { get; set; }

        // Single mapping used by both the preview and the executor.
        public DesignInput ToDesignInput()
        {
            var input = new DesignInput
            {
                ProjectId = ProjectId,
                TemperatureCelsius = TemperatureCelsius,
                FlowRateM3s = FlowRateM3s,
                StaticPressurePa = StaticPressurePa > 0 ? StaticPressurePa : TotalPressurePa,
                TotalPressurePa = TotalPressurePa,
                SpeedRpm = SpeedRpm,
                BladeCount = BladeCount,
                TipDiameterMm = TipDiameterMm
            };

            if (HubRatio is > 0)
                input.HubRatio = HubRatio.Value;

            if (BladeAngleDeg is > 0)
                input.BladeAngleDeg = BladeAngleDeg.Value;

            if (TargetEfficiencyPct is > 0)
                input.TargetEfficiencyPct = TargetEfficiencyPct.Value;

            if (MotorPowerKw is > 0)
                input.MotorPowerKw = MotorPowerKw.Value;

            if (!string.IsNullOrWhiteSpace(BladeMaterial))
                input.BladeMaterial = BladeMaterial.Trim();

            if (DensityKgM3 is > 0)
                input.DensityKgM3 = DensityKgM3.Value;

            if (AltitudeM.HasValue)
                input.AltitudeM = AltitudeM.Value;

            if (AtmosphericPressureKPa is > 0)
            {
                input.AtmosphericPressureKPa = AtmosphericPressureKPa.Value;
                input.InletPressurePa = AtmosphericPressureKPa.Value * 1000.0;
            }

            if (MaxTipDiameterMm is > 0)
                input.MaxTipDiameterMm = MaxTipDiameterMm.Value;

            if (PreferredBladeCount is > 0)
                input.PreferredBladeCount = PreferredBladeCount.Value;

            if (!string.IsNullOrWhiteSpace(DriveType))
                input.DriveType = DriveType.Trim();

            return input;
        }

        // Engine drive names -> the drive names the rest of the app uses.
        public static string AppDriveType(string? engineName)
        {
            return engineName switch
            {
                CustomOptionsParser.DriveVfd => "Variable Speed (VFD)",
                CustomOptionsParser.DriveVBelt => "V-Belt Drive",
                _ => "Direct Drive"
            };
        }
    }

    public sealed class DesignPreviewResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;

        public double? OverallEfficiencyPct { get; init; }
        public double? ShaftPowerKw { get; init; }
        public double? FlowCoefficient { get; init; }
        public double? PressureCoefficient { get; init; }
        public double? TipSpeedMs { get; init; }
        public double? TipMachNumber { get; init; }
        public double? OverallNoiseDbA { get; init; }
        public string? NoiseRating { get; init; }
        public double? BladeStressMpa { get; init; }
        public double? YieldStrengthMpa { get; init; }
        public double? SafetyFactor { get; init; }
        public string? MaterialUsed { get; init; }
        public List<string> Warnings { get; init; } = new();
    }

    // Runs the same physics engines the executor uses, but only in memory:
    // nothing is added to the DbContext and nothing is written to MySQL.
    public sealed class DesignPreviewService
    {
        private readonly ICalibrationCaseRepository _calibrationRepo;

        public DesignPreviewService(ICalibrationCaseRepository calibrationRepo)
        {
            _calibrationRepo = calibrationRepo;
        }

        public async Task<DesignPreviewResult> PreviewAsync(DesignRunParameters p, CancellationToken ct)
        {
            if (p is null || p.ProjectId <= 0 || p.FlowRateM3s <= 0 || p.TotalPressurePa <= 0)
                return new DesignPreviewResult { Success = false, Error = "Invalid design parameters." };

            ct.ThrowIfCancellationRequested();

            var input = p.ToDesignInput();

            var provisionalChordMm = AeroCalcEngine.ComputeMeanChordMm(
                input.TipDiameterMm, input.HubRatio, input.BladeCount);

            var profileData = BladeProfileEngine.ResolveProfileData(null, provisionalChordMm);

            var calibrationCandidates = await _calibrationRepo.GetAllWithPointsAsync();
            var aero = AeroCalcEngine.Calculate(input, profileData, calibrationCandidates);

            var struct_ = StructCalcEngine.Calculate(input, aero);
            var sound = SoundCalcEngine.Calculate(input, aero);

            var warnings = new List<string>();
            warnings.AddRange(aero.Warnings);
            warnings.AddRange(struct_.Warnings);
            warnings.AddRange(sound.Warnings);

            return new DesignPreviewResult
            {
                Success = true,
                OverallEfficiencyPct = Math.Round(aero.OverallEfficiencyPct, 2),
                ShaftPowerKw = Math.Round(aero.ShaftPowerKw, 3),
                FlowCoefficient = Math.Round(aero.FlowCoefficient, 4),
                PressureCoefficient = Math.Round(aero.PressureCoefficient, 4),
                TipSpeedMs = Math.Round(aero.TipSpeedMs, 2),
                TipMachNumber = Math.Round((double)sound.TipMachNumber, 3),
                OverallNoiseDbA = Math.Round((double)sound.LpOverallDba, 1),
                NoiseRating = sound.NoiseRating,
                BladeStressMpa = Math.Round(struct_.TotalStressMpa, 2),
                YieldStrengthMpa = Math.Round(struct_.YieldStrengthMpa, 1),
                SafetyFactor = struct_.SafetyFactor,
                MaterialUsed = struct_.MaterialUsed,
                Warnings = warnings
            };
        }
    }
}