using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace AxialFanMVC.Models
{
    public class GeometryStepViewModel
    {
        // ── Step 5 form fields ────────────────────────────────────────────────
        [Required]
        [Range(2, 20)]
        [Display(Name = "Number of Blades")]
        public int NumberOfBlades { get; set; } = 6;

        [Required]
        [Range(100, 5000)]
        [Display(Name = "Tip Diameter (mm)")]
        public double TipDiameterMm { get; set; } = 800;

        [Required]
        [Range(0.3, 0.7)]
        [Display(Name = "Hub Ratio (0.3–0.7)")]
        public double HubRatio { get; set; } = 0.45;

        [Required]
        [Range(5, 45)]
        [Display(Name = "Blade Angle (°)")]
        public double BladeAngleDeg { get; set; } = 22;

        [Required]
        [Range(50, 99)]
        [Display(Name = "Target Efficiency (%)")]
        public double TargetEfficiencyPct { get; set; } = 82;

        [Required]
        [Range(0.1, 1000)]
        [Display(Name = "Motor Power (kW)")]
        public double MotorPowerKw { get; set; } = 2.2;

        // ── Blade Profile selection ───────────────────────────────────────────
        /// <summary>
        /// The selected blade profile key.
        /// Format: "naca:{designation}"  e.g. "naca:4412"
        ///      or "db:{id}"             e.g. "db:7"  (custom profile from DB)
        /// </summary>
        [Required(ErrorMessage = "Please select a blade profile.")]
        [Display(Name = "Blade Profile")]
        public string? SelectedProfileKey { get; set; }

        /// <summary>Populated from DB + hardcoded NACA list for the dropdown.</summary>
        public List<SelectListItem> ProfileOptions { get; set; } = new();

        // ── Inline preview (loaded via AJAX after selection) ──────────────────
        /// <summary>
        /// Full detail for the currently selected profile.
        /// Null until the user picks a profile (loaded via AJAX or on POST).
        /// </summary>
        //public BladeProfileDetailViewModel? SelectedProfileDetail { get; set; }

        // ── Summary values passed down from earlier steps (read-only display) ──
        public double FlowRateM3s { get; set; }
        public double TotalPressurePa { get; set; }
        public double AirDensityKgM3 { get; set; } = 1.204;
        public double SpeedRpm { get; set; } = 1450;
    }

}
