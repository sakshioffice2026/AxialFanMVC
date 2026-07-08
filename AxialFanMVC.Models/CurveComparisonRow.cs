namespace AxialFanMVC.Models;

public class CurveComparisonRow
{
    public string Label { get; set; } = "";
    public double Angle { get; set; }
    public double Rpm { get; set; }
    public string Color { get; set; } = "#000000";
    public bool IsDesign { get; set; }
}