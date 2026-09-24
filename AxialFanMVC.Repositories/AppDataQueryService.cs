using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Repositories;

public sealed class AppDataQueryService : IAppDataQueryService
{
    private readonly AxialFanDbContext _db;

    public AppDataQueryService(AxialFanDbContext db)
    {
        _db = db;
    }

    public async Task<object?> GetProjectSummaryAsync(int projectId)
    {
        return await _db.Projects
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Client,
                x.Application,
                x.Engineer,
                x.JobDate,
                x.Description,
                x.Status,
                x.CreatedAt,
                x.UpdatedAt,
                DesignCount = x.DesignInputs.Count()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<object?> GetDesignResultAsync(int resultId)
    {
        return await _db.design_results
            .Where(x => x.Id == resultId)
            .Select(x => new
            {
                x.Id,
                x.DesignInputId,
                x.MaterialUsed,
                x.SpecificSpeed,
                x.TipSpeedMs,
                x.HubDiameterMm,
                x.ChordLengthMm,
                x.BladeSpanMm,
                x.ShaftPowerKw,
                x.OverallEfficiencyPct,
                x.FlowCoefficient,
                x.PressureCoefficient,
                x.TipClearanceMm,
                x.BladeStressMpa,
                x.SafetyFactor,
                x.Status,
                x.CalculatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<object> GetBomForProjectAsync(int projectId)
    {
        return await _db.bom_line_items
            .Where(x => x.DesignResult.DesignInput.ProjectId == projectId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                x.Id,
                x.DesignResultId,
                x.Source,
                x.Category,
                x.Description,
                x.Quantity,
                x.Unit,
                x.UnitCost,
                x.LineTotal
            })
            .ToListAsync();
    }

    public async Task<object> ListRecentDesignsAsync(
        int userId,
        int maxResults = 10)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);

        return await _db.design_inputs
            .Where(x => x.Project.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(maxResults)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                ProjectName = x.Project.Name,
                x.FlowRateM3s,
                x.StaticPressurePa,
                x.TotalPressurePa,
                x.SpeedRpm,
                x.BladeCount,
                x.TipDiameterMm,
                x.CreatedAt,
                DesignResultId = x.DesignResult != null
                    ? x.DesignResult.Id
                    : (int?)null
            })
            .ToListAsync();
    }
}