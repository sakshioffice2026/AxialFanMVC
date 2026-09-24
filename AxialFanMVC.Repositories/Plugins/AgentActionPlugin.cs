using System.ComponentModel;
using System.Text.Json;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;

namespace AxialFanMVC.Repositories.Plugins;

public sealed class AgentActionPlugin
{
    private const string StagedSuffix =
        "This action has NOT been executed. It is waiting for the user to press Confirm in the chat widget. " +
        "Tell the user what is pending and ask them to confirm. Never say it has been started or completed.";

    private readonly IAgentPendingActionStore _store;
    private readonly AxialFanDbContext _db;
    private readonly int _userId;

    public AgentActionPlugin(
        IAgentPendingActionStore store,
        AxialFanDbContext db,
        int userId)
    {
        _store = store;
        _db = db;
        _userId = userId;
    }

    [KernelFunction("TriggerCfdRun")]
    [Description("Requests a CFD run for a design result. Does not start it; the user must confirm first.")]
    public async Task<string> TriggerCfdRun(
        [Description("Design result id to run CFD for")] int resultId)
    {
        var owned = await _db.design_results
            .AnyAsync(r => r.Id == resultId && r.DesignInput.Project.UserId == _userId);

        if (!owned)
            return $"Design result {resultId} was not found for the current user. Nothing was staged.";

        var action = _store.Stage(
            _userId,
            AgentActionTypes.TriggerCfdRun,
            JsonSerializer.Serialize(new { resultId }),
            $"Run CFD for design result #{resultId}");

        return $"Staged action {action.Id}: {action.Summary}. {StagedSuffix}";
    }

    [KernelFunction("TriggerOptimization")]
    [Description("Requests an optimization run (Budget, Silent, Premium candidates) for the project of a design result. Does not start it; the user must confirm first.")]
    public async Task<string> TriggerOptimization(
        [Description("Design result id whose project should be optimized")] int resultId)
    {
        var projectId = await _db.design_results
            .Where(r => r.Id == resultId && r.DesignInput.Project.UserId == _userId)
            .Select(r => (int?)r.DesignInput.ProjectId)
            .FirstOrDefaultAsync();

        if (projectId is null)
            return $"Design result {resultId} was not found for the current user. Nothing was staged.";

        var action = _store.Stage(
            _userId,
            AgentActionTypes.TriggerOptimization,
            JsonSerializer.Serialize(new { resultId, projectId = projectId.Value }),
            $"Optimize project #{projectId.Value} based on design result #{resultId}");

        return $"Staged action {action.Id}: {action.Summary}. {StagedSuffix}";
    }

    [KernelFunction("CreateNewDesign")]
    [Description("Requests creation of a new axial fan design in a project from a duty point. Does not create it; the user must confirm first.")]
    public async Task<string> CreateNewDesign(
        [Description("Project id to create the design in")] int projectId,
        [Description("Flow rate in m3/s")] double flowRateM3s,
        [Description("Total pressure in Pa")] double totalPressurePa,
        [Description("Static pressure in Pa. Use the total pressure value if unknown")] double staticPressurePa = 0,
        [Description("Fan speed in rpm")] int speedRpm = 1450,
        [Description("Number of blades")] int bladeCount = 6,
        [Description("Tip diameter in mm")] double tipDiameterMm = 1000,
        [Description("Air temperature in Celsius")] double temperatureCelsius = 25)
    {
        if (flowRateM3s <= 0 || totalPressurePa <= 0)
            return "Flow rate and total pressure must be greater than zero. Nothing was staged.";

        if (speedRpm < 100 || speedRpm > 10000)
            return "Speed must be between 100 and 10000 rpm. Nothing was staged.";

        if (bladeCount < 2 || bladeCount > 32)
            return "Blade count must be between 2 and 32. Nothing was staged.";

        if (tipDiameterMm < 100 || tipDiameterMm > 10000)
            return "Tip diameter must be between 100 and 10000 mm. Nothing was staged.";

        var owned = await _db.Projects
            .AnyAsync(p => p.Id == projectId && p.UserId == _userId);

        if (!owned)
            return $"Project {projectId} was not found for the current user. Nothing was staged.";

        if (staticPressurePa <= 0)
            staticPressurePa = totalPressurePa;

        var action = _store.Stage(
            _userId,
            AgentActionTypes.CreateNewDesign,
            JsonSerializer.Serialize(new
            {
                projectId,
                flowRateM3s,
                totalPressurePa,
                staticPressurePa,
                speedRpm,
                bladeCount,
                tipDiameterMm,
                temperatureCelsius
            }),
            $"Create new design in project #{projectId}: {flowRateM3s} m3/s, {totalPressurePa} Pa, {speedRpm} rpm, {bladeCount} blades, {tipDiameterMm} mm tip diameter");

        return $"Staged action {action.Id}: {action.Summary}. {StagedSuffix}";
    }
}