using System;
using System.ComponentModel;
using System.Text.Json;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories.Plugins;

public sealed class DesignGenerationPlugin
{
    private const string StagedSuffix =
        "This action has NOT been executed. It is waiting for the user to press Confirm in the chat widget. " +
        "Tell the user what is pending and ask them to confirm. Never say it has been started or completed.";

    private readonly IAgentPendingActionStore _store;
    private readonly AxialFanDbContext _db;
    private readonly int _userId;

    public DesignGenerationPlugin(
        IAgentPendingActionStore store,
        AxialFanDbContext db,
        int userId)
    {
        _store = store;
        _db = db;
        _userId = userId;
    }

    [KernelFunction("CreateNewDesign")]
    [Description("Creates a complete axial fan duty point and geometry from a plain-English design brief (e.g. 'a high-static pressure cooling tower fan at 12000 CFM'). " +
                 "Extracts flow rate, pressure duty class and application, then derives total pressure, tip diameter, hub-to-tip ratio, blade count, blade pitch angle, speed and motor power " +
                 "using standard axial fan laws and engineering heuristics. Does not create the record; the user must confirm first.")]
    public async Task<string> CreateNewDesign(
        [Description("Project id to create the design in")] int projectId,
        [Description("Target volumetric flow rate, in the unit given by flowRateUnit")] double targetFlowRate,
        [Description("Unit of targetFlowRate. One of: CFM, M3H, M3S, CMM (m3/min)")] string flowRateUnit = "CFM",
        [Description("Duty/pressure class of the application. One of: Low, Medium, High. " +
                     "Use 'High' for cooling towers, industrial process draft, dust/fume extraction against ductwork or filters. " +
                     "Use 'Medium' for HVAC AHU/condenser coils, general ventilation with moderate ductwork. " +
                     "Use 'Low' for free-air circulation, wall/roof exhaust with little to no resistance.")]
        string pressureClass = "Medium",
        [Description("Short description of the application or service, e.g. 'cooling tower', 'AHU condenser coil', 'tunnel ventilation'. Used only for labelling.")]
        string applicationDescription = "General purpose axial fan",
        [Description("Air/gas temperature in Celsius")] double temperatureCelsius = 25,
        [Description("Target static-to-total pressure ratio, 0 to 1. Higher for ducted high-resistance applications")]
        double staticPressureRatio = 0.9)
    {
        if (targetFlowRate <= 0)
            return "Target flow rate must be greater than zero. Nothing was staged.";

        if (staticPressureRatio <= 0 || staticPressureRatio > 1)
            staticPressureRatio = 0.9;

        var owned = await _db.Projects
            .AnyAsync(p => p.Id == projectId && p.UserId == _userId);

        if (!owned)
            return $"Project {projectId} was not found for the current user. Nothing was staged.";

        double flowRateM3s = ConvertFlowToM3s(targetFlowRate, flowRateUnit);
        if (flowRateM3s <= 0)
            return $"Unrecognized flow rate unit '{flowRateUnit}'. Use CFM, M3H, M3S or CMM. Nothing was staged.";

        var duty = ClassifyDuty(pressureClass);

        double densityKgM3 = AirDensity(temperatureCelsius);

        double totalPressurePa = duty.TotalPressurePa;
        double staticPressurePa = Math.Round(totalPressurePa * staticPressureRatio, 1);

        double tipDiameterMm = EstimateTipDiameterMm(flowRateM3s, duty.FaceVelocityMs);

        double hubRatio = duty.HubRatio;
        int bladeCount = duty.BladeCount;
        double bladeAngleDeg = duty.BladeAngleDeg;
        double targetEfficiencyPct = duty.TargetEfficiencyPct;

        int speedRpm = EstimateSpeedRpm(tipDiameterMm, duty.MaxTipSpeedMs);

        double motorPowerKw = EstimateMotorPowerKw(flowRateM3s, totalPressurePa, targetEfficiencyPct);

        var action = _store.Stage(
            _userId,
            AgentActionTypes.CreateNewDesign,
            JsonSerializer.Serialize(new
            {
                projectId,
                applicationDescription,
                pressureClass = duty.Name,
                flowRateM3s = Math.Round(flowRateM3s, 4),
                staticPressurePa,
                totalPressurePa,
                densityKgM3,
                temperatureCelsius,
                tipDiameterMm = Math.Round(tipDiameterMm, 0),
                hubRatio,
                bladeCount,
                bladeAngleDeg,
                targetEfficiencyPct,
                speedRpm,
                motorPowerKw = Math.Round(motorPowerKw, 2)
            }),
            $"Create new '{duty.Name}' duty design for '{applicationDescription}' in project #{projectId}: " +
            $"{Math.Round(flowRateM3s, 3)} m3/s @ {totalPressurePa} Pa total, {tipDiameterMm:F0} mm tip dia, " +
            $"hub ratio {hubRatio:F2}, {bladeCount} blades @ {bladeAngleDeg}°, {speedRpm} rpm, ~{motorPowerKw:F1} kW motor");

        return $"Staged action {action.Id}: {action.Summary}. {StagedSuffix}";
    }

    private static double ConvertFlowToM3s(double value, string unit)
    {
        return unit.Trim().ToUpperInvariant() switch
        {
            "CFM" => value * 0.0004719474432,
            "M3H" or "M3/H" or "CMH" => value / 3600.0,
            "M3S" or "M3/S" => value,
            "CMM" or "M3MIN" or "M3/MIN" => value / 60.0,
            _ => -1
        };
    }

    private static double AirDensity(double temperatureCelsius)
    {
        const double p0 = 101325.0;
        const double r = 287.05;
        double tKelvin = temperatureCelsius + 273.15;
        return Math.Round(p0 / (r * tKelvin), 4);
    }

    private static double EstimateTipDiameterMm(double flowRateM3s, double faceVelocityMs)
    {
        double area = flowRateM3s / faceVelocityMs;
        double diameterM = Math.Sqrt(4.0 * area / Math.PI);
        double diameterMm = diameterM * 1000.0;

        double[] standardSizes = { 300, 350, 400, 450, 500, 560, 630, 710, 800, 900,
                                    1000, 1120, 1250, 1400, 1600, 1800, 2000, 2240, 2500, 2800, 3150 };

        foreach (var size in standardSizes)
        {
            if (size >= diameterMm)
                return size;
        }

        return Math.Round(diameterMm / 50.0) * 50.0;
    }

    private static int EstimateSpeedRpm(double tipDiameterMm, double maxTipSpeedMs)
    {
        double diameterM = tipDiameterMm / 1000.0;
        double maxRpmForTipSpeed = (maxTipSpeedMs * 60.0) / (Math.PI * diameterM);

        int[] standardSpeeds = { 960, 1000, 1450, 1500, 1750, 2900, 2950, 3000, 3500 };

        int best = standardSpeeds[0];
        foreach (var s in standardSpeeds)
        {
            if (s <= maxRpmForTipSpeed && s > best)
                best = s;
        }

        return best;
    }

    private static double EstimateMotorPowerKw(double flowRateM3s, double totalPressurePa, double targetEfficiencyPct)
    {
        double efficiency = Math.Clamp(targetEfficiencyPct / 100.0, 0.35, 0.95);
        double airPowerW = flowRateM3s * totalPressurePa;
        double shaftPowerW = airPowerW / efficiency;
        double motorPowerW = shaftPowerW * 1.15;
        return motorPowerW / 1000.0;
    }

    private static DutyProfile ClassifyDuty(string pressureClass)
    {
        return (pressureClass ?? "Medium").Trim().ToUpperInvariant() switch
        {
            "HIGH" => new DutyProfile
            {
                Name = "High",
                TotalPressurePa = 1100,
                FaceVelocityMs = 7.5,
                MaxTipSpeedMs = 65,
                HubRatio = 0.58,
                BladeCount = 12,
                BladeAngleDeg = 34,
                TargetEfficiencyPct = 72
            },
            "LOW" => new DutyProfile
            {
                Name = "Low",
                TotalPressurePa = 180,
                FaceVelocityMs = 11.5,
                MaxTipSpeedMs = 80,
                HubRatio = 0.35,
                BladeCount = 4,
                BladeAngleDeg = 16,
                TargetEfficiencyPct = 78
            },
            _ => new DutyProfile
            {
                Name = "Medium",
                TotalPressurePa = 450,
                FaceVelocityMs = 9.5,
                MaxTipSpeedMs = 75,
                HubRatio = 0.45,
                BladeCount = 8,
                BladeAngleDeg = 24,
                TargetEfficiencyPct = 82
            }
        };
    }

    private sealed class DutyProfile
    {
        public string Name { get; init; } = "Medium";
        public double TotalPressurePa { get; init; }
        public double FaceVelocityMs { get; init; }
        public double MaxTipSpeedMs { get; init; }
        public double HubRatio { get; init; }
        public int BladeCount { get; init; }
        public double BladeAngleDeg { get; init; }
        public double TargetEfficiencyPct { get; init; }
    }
}