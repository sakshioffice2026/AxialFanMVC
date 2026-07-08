using AxialFanMVC.Database;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AxialFanMVC.ViewModels
{
    // ─── Auth ───────────────────────────────────────────────────────
    public class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // ─── Projects ───────────────────────────────────────────────────
    public class ProjectListViewModel
    {
        public List<ProjectSummaryViewModel> Projects { get; set; } = new();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int Total { get; set; }
    }

    public class ProjectSummaryViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = "draft";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int DesignCount { get; set; }
    }

    public class CreateProjectViewModel
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }
    }

    public class EditProjectViewModel
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public string Status { get; set; } = "draft";
    }

    // ─── Design Wizard ──────────────────────────────────────────────
    public class DesignWizardViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int CurrentStep { get; set; } = 1;
        public List<BladeProfile> BladeProfiles { get; set; } = new();


        [Display(Name = "Hub Diameter (mm)")]
        [Range(10, 4000)]
        public double? HubDiameterMm { get; set; }

        // Project Info (maps to Project entity, not DesignInput)
        [Display(Name = "Client")]
        [MaxLength(200)]
        public string? Client { get; set; }

        [Display(Name = "Application")]
        [MaxLength(100)]
        public string? Application { get; set; }

        [Display(Name = "Engineer")]
        [MaxLength(100)]
        public string? Engineer { get; set; }

        [Display(Name = "Date")]
        public DateOnly? JobDate { get; set; }

        // Step 1 — media
        [Display(Name = "Media Type")]
        public string MediaType { get; set; } = "Air (standard)";

        [Display(Name = "Temperature (°C)")]
        [Range(-50, 500)]
        public double TemperatureCelsius { get; set; } = 20;

        [Display(Name = "Inlet Pressure (Pa)")]
        [Range(1000, 200000)]
        public double InletPressurePa { get; set; } = 101325;

        [Display(Name = "Air Density (kg/m³)")]
        [Range(0.1, 5)]
        public double DensityKgM3 { get; set; } = 1.2;

        [Display(Name = "Altitude (m)")]
        [Range(0, 6000)]
        public double? AltitudeM { get; set; } = 0;

        [Display(Name = "Atmospheric Pressure (kPa)")]
        [Range(50, 110)]
        public double? AtmosphericPressureKPa { get; set; } = 101.325;

        [Display(Name = "Relative Humidity (%)")]
        [Range(0, 100)]
        public double? RelativeHumidityPct { get; set; } = 50;

        [Display(Name = "Direction")]
        public string? Direction { get; set; }

        [Display(Name = "Installation Type")]
        public string? InstallationType { get; set; }

        [Display(Name = "Duty")]
        public string? Duty { get; set; }

        [Display(Name = "Frequency (Hz)")]
        public int? FrequencyHz { get; set; }

        // Step 2 — flow
        [Display(Name = "Flow Rate (m³/s)")]
        [Range(0.01, 1000)]
        public double FlowRateM3s { get; set; } = 10.0;

        // Step 3 — pressure
        [Display(Name = "Static Pressure (Pa)")]
        public double StaticPressurePa { get; set; } = 500;

        [Display(Name = "Total Pressure (Pa)")]
        public double TotalPressurePa { get; set; } = 650;

        // Fan Constraints
        [Display(Name = "Max Tip Diameter (mm)")]
        public double? MaxTipDiameterMm { get; set; } = 1000;

        [Display(Name = "Min Required Efficiency (%)")]
        [Range(0, 100)]
        public double? MinEfficiencyPct { get; set; } = 65;

        [Display(Name = "Max Noise Level (dB(A))")]
        public double? MaxNoiseDbA { get; set; } = 85;

        [Display(Name = "Max Motor Power (kW)")]
        public double? MaxMotorPowerKw { get; set; } = 15;

        [Display(Name = "Preferred Blade Count")]
        [Range(2, 24)]
        public int? PreferredBladeCount { get; set; } = 8;

        [Display(Name = "Max Speed (RPM)")]
        public int? MaxSpeedRpm { get; set; } = 1450;
        // Step 4 — speed
        [Display(Name = "Fan Speed (RPM)")]
        [Range(100, 10000)]
        public int SpeedRpm { get; set; } = 1450;

        [Display(Name = "Motor Poles")]
        public string MotorPoles { get; set; } = "4-pole / 50 Hz";

        [Display(Name = "Motor Type")]
        public string? MotorType { get; set; }

        [Display(Name = "Voltage")]
        public string? VoltageSpec { get; set; }

        [Display(Name = "Insulation Class")]
        public string? InsulationClass { get; set; }

        [Display(Name = "Starting Method")]
        public string? StartingMethod { get; set; }

        // Step 5 — geometry
        [Display(Name = "Number of Blades")]
        [Range(2, 24)]
        public int BladeCount { get; set; } = 6;

        [Display(Name = "Tip Diameter (mm)")]
        [Range(100, 5000)]
        public double TipDiameterMm { get; set; } = 800;

        [Display(Name = "Hub Ratio")]
        [Range(0.1, 0.9)]
        public double HubRatio { get; set; } = 0.45;

        [Display(Name = "Blade Angle (°)")]
        [Range(5, 50)]
        public double BladeAngleDeg { get; set; } = 22;

        [Display(Name = "Target Efficiency (%)")]
        [Range(30, 99)]
        public double TargetEfficiencyPct { get; set; } = 82;

        [Display(Name = "Motor Power (kW)")]
        [Range(0.01, 1000)]
        public double MotorPowerKw { get; set; } = 2.2;

        [Display(Name = "Blade Profile")]
        public int? BladeProfileId { get; set; }


        // Accessories
        [Display(Name = "Inlet Safety Guard")]
        public bool AccInletGuard { get; set; }

        [Display(Name = "Outlet Safety Guard")]
        public bool AccOutletGuard { get; set; }

        [Display(Name = "Vibration Isolators")]
        public bool AccVibrationIsolators { get; set; }

        [Display(Name = "Flexible Duct Connector")]
        public bool AccFlexibleConnector { get; set; }

        [Display(Name = "Inlet/Outlet Silencer")]
        public bool AccSilencer { get; set; }

        [Display(Name = "Backdraft Damper")]
        public bool AccBackdraftDamper { get; set; }

        [Display(Name = "Accessory Notes")]
        [MaxLength(1000)]
        public string? AccessoryNotes { get; set; }

        // ── Drive Configuration ───────────────────────────────────────
        [MaxLength(50), Column("drive_type")]
        public string? DriveType { get; set; }

        // V-Belt fields (shown only when DriveType = "V-Belt Drive")
        [Display(Name = "Motor RPM")]
        [Range(100, 10000)]
        public int? MotorRpm { get; set; }

        [Display(Name = "Fan RPM")]
        [Range(100, 10000)]
        public int? FanRpm { get; set; }

        [Display(Name = "Belt Type")]
        public string BeltType { get; set; } = "B";

        [Display(Name = "Pulley Ratio")]
        public double? PulleyRatio { get; set; }  // auto-calculated

        [Display(Name = "Number of Belts")]
        [Range(1, 20)]
        public int? NumberOfBelts { get; set; }

        [Display(Name = "Centre Distance (mm)")]
        [Range(100, 5000)]
        public double? CentreDistanceMm { get; set; }

        // VFD fields (shown only when DriveType = "Variable Speed (VFD)")
        [Display(Name = "Min RPM")]
        [Range(0, 10000)]
        public int? VfdMinRpm { get; set; }

        [Display(Name = "Max RPM")]
        [Range(0, 10000)]
        public int? VfdMaxRpm { get; set; }

        [Display(Name = "Operating Speed (%)")]
        [Range(0, 100)]
        public double VfdSpeedPct { get; set; } = 100;
        // ── Capacity Marking ──────────────────────────────────────────
        [Display(Name = "Flow Rate (m³/h)")]
        public double CapacityFlowM3h { get; set; }

        [Display(Name = "Static Pressure (Pa)")]
        public double CapacityStaticPa { get; set; } = 500;

        [Display(Name = "Fan Speed (RPM)")]
        public int CapacitySpeedRpm { get; set; }

        [Display(Name = "Motor Power (kW)")]
        public double CapacityMotorKw { get; set; }

        [Display(Name = "Efficiency (%)")]
        public double CapacityEfficiencyPct { get; set; }

        [Display(Name = "Drawing Tag No.")]
        [MaxLength(50)]
        public string? DrawingTagNo { get; set; }

        [Display(Name = "Nameplate Text")]
        [MaxLength(200)]
        public string? NameplateText { get; set; }
        // ── Operating Range / Zone ────────────────────────────────────
        [Display(Name = "Min Flow Rate (m³/h)")]
        [Range(0, 100000)]
        public double? RangeMinFlowM3h { get; set; }

        [Display(Name = "Max Flow Rate (m³/h)")]
        [Range(0, 100000)]
        public double? RangeMaxFlowM3h { get; set; }

        [Display(Name = "Min Pressure (Pa)")]
        [Range(0, 100000)]
        public double? RangeMinPressurePa { get; set; }

        [Display(Name = "Max Pressure (Pa)")]
        [Range(0, 100000)]
        public double? RangeMaxPressurePa { get; set; }

        [Display(Name = "Min Speed (RPM)")]
        [Range(0, 10000)]
        public int? RangeMinSpeedRpm { get; set; }

        [Display(Name = "Max Speed (RPM)")]
        [Range(0, 10000)]
        public int? RangeMaxSpeedRpm { get; set; }

        // ===================================================
        // Step 9 - Acoustic Data
        // ===================================================

        [Display(Name = "Receiver Distance (m)")]
        [Range(0.5, 100)]
        public double ReceiverDistanceM { get; set; } = 1.0;

        [Display(Name = "Environment")]
        public string AcousticEnvironment { get; set; } = "Free Field";

        [Display(Name = "Directivity Index (dB)")]
        public double DirectivityIndexDb { get; set; } = 3;

        [Display(Name = "Inlet Duct Attenuation (dB)")]
        public double InletAttenuationDb { get; set; } = 0;

        [Display(Name = "Outlet Duct Attenuation (dB)")]
        public double OutletAttenuationDb { get; set; } = 0;

        [Display(Name = "Casing Transmission Loss (dB)")]
        public double CasingTransmissionLossDb { get; set; } = 18;

        [Display(Name = "Silencer Attenuation (dB)")]
        public double SilencerAttenuationDb { get; set; } = 0;

        [Display(Name = "Room Correction (dB)")]
        public double RoomCorrectionDb { get; set; } = 0;

        [Display(Name = "Background Noise (dB(A))")]
        public double? BackgroundNoiseDbA { get; set; }

        [Display(Name = "Acoustic Safety Margin (dB)")]
        public double SafetyMarginDb { get; set; } = 3;
    }

    // ─── Design Results ──────────────────────────────────────────────
    public class DesignResultViewModel
    {
        public int DesignInputId { get; set; }
        public int ResultId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;

        // Inputs summary
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public int SpeedRpm { get; set; }
        public int BladeCount { get; set; }
        public double TipDiameterMm { get; set; }
        public double BladeAngleDeg { get; set; }
        public string? BladeProfileName { get; set; }

        // Aerodynamic outputs
        public double SpecificSpeed { get; set; }
        public double TipSpeedMs { get; set; }
        public double HubDiameterMm { get; set; }
        public double ChordLengthMm { get; set; }
        public double BladeSpanMm { get; set; }
        public double ShaftPowerKw { get; set; }
        public double OverallEfficiencyPct { get; set; }
        public double FlowCoefficient { get; set; }
        public double PressureCoefficient { get; set; }

        // Structural outputs
        public double TipClearanceMm { get; set; }
        public double BladeStressMpa { get; set; }
        public double SafetyFactor { get; set; }

        public string Status { get; set; } = "ok";
        public List<string> Warnings { get; set; } = new();
        public DateTime CalculatedAt { get; set; }

        // Performance curve data (JSON for Chart.js)
        public string CurveJson { get; set; } = "{}";

        // Drawings
        public List<DrawingViewModel> Drawings { get; set; } = new();
        // ===== Acoustic Results =====
        public double? OverallNoiseDbA { get; set; }
        public double? SoundPowerLevelDb { get; set; }
        public double? SpecificSoundLevelKs { get; set; }
        public double? BladePassingFrequencyHz { get; set; }
        public double? TipMachNumber { get; set; }
        public double? NoiseRatingValue { get; set; }
        public string? NoiseRating { get; set; }
        public string? OctaveBandLwJson { get; set; }


        public CurveComparisonViewModel BaselineComparison { get; set; }
        public CurveComparisonViewModel PinnComparison { get; set; }

    }

    public class DrawingViewModel
    {
        public int Id { get; set; }
        public string DrawingType { get; set; } = string.Empty;
        public string? SvgData { get; set; }
        public bool HasDxf { get; set; }
        public bool HasPdf { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    // ─── Design History ──────────────────────────────────────────────
    public class DesignHistoryViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public List<DesignSummaryViewModel> Designs { get; set; } = new();
    }

    public class DesignSummaryViewModel
    {
        public int DesignInputId { get; set; }
        public int? ResultId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public int SpeedRpm { get; set; }
        public double TipDiameterMm { get; set; }
        public int BladeCount { get; set; }
        public string? BladeProfileName { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime CreatedAt { get; set; }
        public double? OverallEfficiencyPct { get; set; }
        public double? ShaftPowerKw { get; set; }
        public double HubRatio { get; set; }
        public double BladeAngleDeg { get; set; }
        public string? MotorType { get; set; }
        [MaxLength(50), Column("drive_type")]
        public string? DriveType { get; set; }
    }
    // ─── Design Series ──────────────────────────────────────────────
    public class DesignSeriesCreateViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        [Display(Name = "Series Name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Base Design")]
        public int BaseDesignInputId { get; set; }

        public List<BaseDesignOptionViewModel> AvailableBaseDesigns { get; set; } = new();

        [Display(Name = "Sizes to Generate")]
        public List<int> SelectedDiametersMm { get; set; } = new();

        public int[] CatalogDiametersMm { get; set; } = Array.Empty<int>();
    }

    public class BaseDesignOptionViewModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty; // e.g. "800mm, 6 blades, 25 m³/s (12 Jul 2026)"
    }

    public class DesignSeriesListViewModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public List<DesignSeriesSummaryViewModel> Series { get; set; } = new();
    }

    public class DesignSeriesSummaryViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string BaseDesignLabel { get; set; } = string.Empty;

        public int VariantCount { get; set; }

        public double MinFlowM3s { get; set; }

        public double MaxFlowM3s { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class DesignSeriesDetailsViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string BaseDesignLabel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<DesignSeriesVariantViewModel> Variants { get; set; } = new();
    }

    public class DesignSeriesVariantViewModel
    {
        public int DesignInputId { get; set; }
        public double TipDiameterMm { get; set; }
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public double MotorPowerKw { get; set; }
        public bool HasBeenCalculated { get; set; } // true if a DesignResult already exists
        public int? ResultId { get; set; }
    }

    // ─── Reports (plain data report — no charts) ───────────────────
    public class ReportsViewModel
    {
        // Filter
        public int? SelectedProjectId { get; set; }
        public string? SelectedStatus { get; set; } // null/"" = all, "ok", "warning", "pending"
        public List<ProjectFilterOption> ProjectFilterOptions { get; set; } = new();

        // Plain summary line — not chart-backed
        public int TotalDesigns { get; set; }
        public int CalculatedCount { get; set; }
        public int WarningCount { get; set; }
        public int PendingCount { get; set; }

        // The report content
        public List<DesignReportRow> Designs { get; set; } = new();
    }

    public class ProjectFilterOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class DesignReportRow
    {
        public int DesignInputId { get; set; }
        public int? ResultId { get; set; }

        public string ProjectName { get; set; } = string.Empty;
        public string? Client { get; set; }
        public string? Application { get; set; }
        public string? Engineer { get; set; }

        public string MediaType { get; set; } = string.Empty;
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public int SpeedRpm { get; set; }
        public int BladeCount { get; set; }
        public double TipDiameterMm { get; set; }

        public double? OverallEfficiencyPct { get; set; }
        public double? ShaftPowerKw { get; set; }
        public double? SafetyFactor { get; set; }

        public string Status { get; set; } = "pending"; // ok | warning | pending
        public DateTime CreatedAt { get; set; }
    }

 

public class CurveComparisonViewModel
    {
        public double PressurePa { get; set; }

        public double PeakPressurePa { get; set; }

        public double EfficiencyPct { get; set; }

        public double PeakEfficiencyPct { get; set; }

        public double PowerKw { get; set; }

    }

}

