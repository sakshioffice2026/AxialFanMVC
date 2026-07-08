using System.ComponentModel.DataAnnotations;

namespace AxialFanMVC.Models
{
    public class CalibrationCaseCreateViewModel
    {
        [Required, Display(Name = "Source Type")]
        public string SourceType { get; set; } = "Manufacturer Curve";

        [Display(Name = "Source Description")]
        [MaxLength(300)]
        public string? SourceDescription { get; set; }

        [Required, Display(Name = "Tip Diameter (mm)")]
        public double TipDiameterMm { get; set; }

        [Required, Range(0.1, 0.9), Display(Name = "Hub Ratio")]
        public double HubRatio { get; set; } = 0.45;

        [Required, Display(Name = "Blade Angle (deg)")]
        public double BladeAngleDeg { get; set; }

        [Required, Range(2, 20), Display(Name = "Blade Count")]
        public int BladeCount { get; set; } = 6;

        [Required, Display(Name = "Speed (RPM)")]
        public int SpeedRpm { get; set; } = 1450;

        [Display(Name = "Air Density (kg/m³)")]
        public double DensityKgM3 { get; set; } = 1.204;

        [Display(Name = "Temperature (°C)")]
        public double TemperatureCelsius { get; set; } = 25;

        [Display(Name = "Blade Profile (e.g. NACA 4412)")]
        public string? BladeProfileDesignation { get; set; }

        [Display(Name = "Max Camber (%)")]
        public double? MaxCamberPct { get; set; }

        [Display(Name = "Max Thickness (%)")]
        public double? MaxThicknessPct { get; set; }

        // Fixed-size row set — plain, no JS required to add/remove rows.
        // Blank rows are simply ignored on submit.
        public List<CalibrationPointRowViewModel> Points { get; set; } =
            Enumerable.Range(0, 15).Select(_ => new CalibrationPointRowViewModel()).ToList();
    }

    public class CalibrationPointRowViewModel
    {
        [Display(Name = "Flow Rate (m³/s)")]
        public double? FlowRateM3s { get; set; }

        [Display(Name = "Pressure Rise (Pa)")]
        public double? PressureRisePa { get; set; }

        [Display(Name = "Efficiency (%)")]
        public double? EfficiencyPct { get; set; }

        [Display(Name = "Power (kW, optional)")]
        public double? PowerKw { get; set; }
    }
}