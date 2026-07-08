using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Models
{
    public class BladeProfileListViewModel
    {
        public List<BladeProfileSummary> Profiles { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
    public class BladeProfileSummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double MaxCamberPct { get; set; }
        public double MaxCamberPos { get; set; }
        public double MaxThicknessPct { get; set; }
        public bool HasCoordinates { get; set; }
    }
    public class BladeProfileDetailViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double ChordMm { get; set; } = 148.3;
        public int Points { get; set; } = 100;
        public double MaxCamberPct { get; set; }
        public double MaxCamberPos { get; set; }
        public double MaxThicknessPct { get; set; }
        public bool HasCoordinates { get; set; }
        public ProfileDimensionsViewModel Dimensions { get; set; } = new();
        public AeroParamsViewModel AeroParams { get; set; } = new();
        public List<StationRowViewModel> StationTable { get; set; } = new();
        public string? SvgProfile { get; set; }
        public string? SvgStationTable { get; set; }
        public List<double[]> UpperNormalised { get; set; } = new();
        public List<double[]> LowerNormalised { get; set; } = new();
        public List<double[]> CamberNormalised { get; set; } = new();
        public List<double[]> UpperMm { get; set; } = new();
        public List<double[]> LowerMm { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class ProfileDimensionsViewModel
    {
        public double ChordMm { get; set; }
        public double MaxThicknessMm { get; set; }
        public double MaxThicknessPct { get; set; }
        public double MaxThicknessXPct { get; set; }
        public double MaxThicknessXMm { get; set; }
        public double MaxCamberMm { get; set; }
        public double MaxCamberPct { get; set; }
        public double MaxCamberXPct { get; set; }
        public double MaxCamberXMm { get; set; }
        public double LeadingEdgeRadiusMm { get; set; }
        public double LeadingEdgeRadiusPct { get; set; }
        public double TrailingEdgeThickMm { get; set; }
        public double MeanLineAngle { get; set; }
    }

    public class AeroParamsViewModel
    {
        public double DesignLiftCoeff { get; set; }
        public double ThicknessRatio { get; set; }
        public double MaxCamberLocation { get; set; }
        public double LeadingEdgeRadius { get; set; }
        public double LeadingEdgeRadiusPct { get; set; }
        public double TrailingEdgeAngle { get; set; }
        public double LiftCurveSlope { get; set; }
        public double ApproxZeroLiftAngle { get; set; }
        public double ApproxStallAngle { get; set; }
        public double ApproxMaxCl { get; set; }
        public double ApproxMinDrag { get; set; }
        public string ReynoldsRange { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public class StationRowViewModel
    {
        public double XPct { get; set; }
        public double XMm { get; set; }
        public double YUpperMm { get; set; }
        public double YLowerMm { get; set; }
        public double YCamberMm { get; set; }
        public double ThicknessMm { get; set; }
        public double ThicknessPct { get; set; }
    }

    public class CompareViewModel
    {
        public BladeProfileDetailViewModel Profile1 { get; set; } = new();
        public BladeProfileDetailViewModel Profile2 { get; set; } = new();
        public string? OverlaySvg { get; set; }
        public double ChordMm { get; set; } = 148.3;
        public List<BladeProfileSummary> AllProfiles { get; set; } = new();
        public int Profile1Id { get; set; }
        public int Profile2Id { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UploadCustomProfileViewModel
    {
        [Required, MaxLength(100)]
        [Display(Name = "Profile Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Required]
        [Display(Name = "Coordinate Data (JSON)")]
        public string CoordinateJson { get; set; } = string.Empty;

        [Range(10, 2000)]
        [Display(Name = "Chord Length (mm)")]
        public double ChordMm { get; set; } = 148.3;

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public static readonly string ExampleJson =
            "[\n" +
            "  [0,0,0],[1.25,0.0205,-0.0172],[2.5,0.0294,-0.0219],\n" +
            "  [5,0.0412,-0.0274],[10,0.0564,-0.0336],[20,0.0740,-0.0392],\n" +
            "  [30,0.0820,-0.0402],[40,0.0832,-0.0398],[50,0.0802,-0.0384],\n" +
            "  [60,0.0734,-0.0356],[70,0.0621,-0.0306],[80,0.0472,-0.0236],\n" +
            "  [90,0.0282,-0.0140],[95,0.0178,-0.0087],[100,0.0013,-0.0013]\n" +
            "]";
    }
}
