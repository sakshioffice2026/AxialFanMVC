using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AxialFanMVC.Models
{
    // ─────────────────────────────────────────────────────────────
    // BladeProfileModels.cs
    // Place in:  Models/BladeProfileModels.cs
    //
    // These are in-memory calculation models — NOT database entities.
    // Produced by BladeProfileEngine, consumed by the controller.
    // ─────────────────────────────────────────────────────────────

    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Point2D(double x, double y) { X = x; Y = y; }
    }

    public class AerofoilCoords
    {
        public List<Point2D> Upper { get; set; } = new();
        public List<Point2D> Lower { get; set; } = new();
        public List<Point2D> Camber { get; set; } = new();
    }

    public class ProfileDimensions
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

    public class AeroParameters
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

    public class StationRow
    {
        public double XPct { get; set; }
        public double XMm { get; set; }
        public double YUpperMm { get; set; }
        public double YLowerMm { get; set; }
        public double YCamberMm { get; set; }
        public double ThicknessMm { get; set; }
        public double ThicknessPct { get; set; }
    }

    public class BladeProfileData
    {
        public string Designation { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double ChordMm { get; set; }
        public double MaxCamberPct { get; set; }
        public double MaxCamberPos { get; set; }
        public double MaxThicknessPct { get; set; }
        public List<Point2D> UpperSurface { get; set; } = new();
        public List<Point2D> LowerSurface { get; set; } = new();
        public List<Point2D> CamberLine { get; set; } = new();
        public ProfileDimensions Dimensions { get; set; } = new();
        public AeroParameters AeroParams { get; set; } = new();
        public List<StationRow> StationTable { get; set; } = new();
    }
}

