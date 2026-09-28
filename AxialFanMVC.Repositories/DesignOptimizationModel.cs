using System.Collections.Generic;

namespace AxialFanMVC.Repositories
{
    public class OptimizeCommandRequest
    {
        public string Message { get; set; } = "";
        public int? ResultId { get; set; }
    }

    public class DesignFlowSnapshot
    {
        public int ResultId { get; set; }
        public int DesignInputId { get; set; }
        public int ProjectId { get; set; }
        public int UserId { get; set; }
        public string ProjectName { get; set; } = "";
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public int SpeedRpm { get; set; }
        public int BladeCount { get; set; }
        public double TipDiameterMm { get; set; }
        public double HubRatio { get; set; }
        public double BladeAngleDeg { get; set; }
        public string BladeMaterial { get; set; } = "";
        public int? BladeProfileId { get; set; }
        public double? MaxTipDiameterMm { get; set; }
        public double? MinEfficiencyPct { get; set; }
        public double? MaxNoiseDbA { get; set; }
        public double? MaxMotorPowerKw { get; set; }
        public int? MaxSpeedRpm { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public double ShaftPowerKw { get; set; }
        public double SafetyFactor { get; set; }
        public double BladeStressMpa { get; set; }
        public double? OverallNoiseDbA { get; set; }
        public List<string> WarningMessages { get; set; } = new();
        public string Source { get; set; } = "EFCore";
    }

    public class SizingCandidate
    {
        public double BladeAngleDeg { get; set; }
        public int SpeedRpm { get; set; }
        public double TipDiameterMm { get; set; }
        public int BladeCount { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public double ShaftPowerKw { get; set; }
        public double SafetyFactor { get; set; }
        public double BladeStressMpa { get; set; }
        public double NoiseDbA { get; set; }
        public double ChordLengthMm { get; set; }
        public double HubDiameterMm { get; set; }
        public double BladeSpanMm { get; set; }
        public double SpecificSpeed { get; set; }
        public double TipSpeedMs { get; set; }
        public double FlowCoefficient { get; set; }
        public double PressureCoefficient { get; set; }
        public string MaterialUsed { get; set; } = "";
        public double YieldStrengthMpa { get; set; }
        public double SoundPowerLevelDb { get; set; }
        public double BladePassingFrequencyHz { get; set; }
        public double TipMachNumber { get; set; }
        public double? NoiseRatingValue { get; set; }
        public string? NoiseRating { get; set; }
        public string OctaveBandLwJson { get; set; } = "[]";
        public List<string> Warnings { get; set; } = new();
        public bool FeasibleAgainstConstraints { get; set; }
        public bool BetterThanBaseline { get; set; }
    }

    public class DesignDeltaRow
    {
        public string Metric { get; set; } = "";
        public string BaselineValue { get; set; } = "";
        public string OptimizedValue { get; set; } = "";
        public string DeltaValue { get; set; } = "";
        public string Direction { get; set; } = "neutral";
    }

    public class WarningAuditEntry
    {
        public string Message { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class DesignComparativeDiagnostics
    {
        public List<DesignDeltaRow> DeltaTable { get; set; } = new();
        public List<WarningAuditEntry> WarningAudit { get; set; } = new();
        public int ResolvedWarningCount { get; set; }
        public int PersistingWarningCount { get; set; }
        public int NewWarningCount { get; set; }
    }

    public class OptimizeFlowResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int SourceResultId { get; set; }
        public string ProjectName { get; set; } = "";
        public string FlowStoreSource { get; set; } = "";
        public SizingCandidate? Optimized { get; set; }
        public DesignComparativeDiagnostics? Diagnostics { get; set; }
        public string DraftToken { get; set; } = "";
        public string ProposedResultLabel { get; set; } = "";
        public string PromptMessage { get; set; } = "";
    }

    public class SaveDraftRequest
    {
        public string DraftToken { get; set; } = "";
        public bool Confirm { get; set; }
    }

    public class SaveDraftResponse
    {
        public bool Saved { get; set; }
        public int? NewResultId { get; set; }
        public string Message { get; set; } = "";
    }
}
