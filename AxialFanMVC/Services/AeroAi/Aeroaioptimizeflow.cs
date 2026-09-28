using System.Text;
using System.Text.RegularExpressions;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services.AeroAi
{
    public class AeroAiReply
    {
        public bool Handled { get; set; }
        public string Reply { get; set; } = "";
        public bool AwaitingConfirmation { get; set; }
        public int? SavedResultId { get; set; }
    }

    public class AeroAiOptimizeFlow
    {
        private static readonly Regex OptimizeRx = new(
            @"\boptimi[sz]e\b(?:\s+(?:the\s+)?(?:design|result))?\s*#?\s*(\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YesRx = new(
            @"^\s*(yes|y|yeah|yep|sure|ok|okay|save|confirm)\b[\s!.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoRx = new(
            @"^\s*(no|n|nope|cancel|discard|skip)\b[\s!.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly AxialFanDbContext _db;
        private readonly ICalibrationCaseRepository _calibrationRepo;
        private readonly ICurveGeneration _curveService;

        public AeroAiOptimizeFlow(
            AxialFanDbContext db,
            ICalibrationCaseRepository calibrationRepo,
            ICurveGeneration curveService)
        {
            _db = db;
            _calibrationRepo = calibrationRepo;
            _curveService = curveService;
        }

        public async Task<AeroAiReply> HandleAsync(string message, int userId)
        {
            if (DesignFlowStore.HasDraft(userId))
            {
                if (YesRx.IsMatch(message)) return await SaveDraftAsync(userId);
                if (NoRx.IsMatch(message))
                {
                    DesignFlowStore.TakeDraft(userId);
                    return new AeroAiReply { Handled = true, Reply = "Optimized draft discarded. Original design unchanged." };
                }
            }

            var m = OptimizeRx.Match(message ?? "");
            if (!m.Success) return new AeroAiReply { Handled = false };

            int? requestedId = m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
            return await RunAsync(userId, requestedId);
        }

        // Steps 1-4.
        private async Task<AeroAiReply> RunAsync(int userId, int? requestedId)
        {
            // 1. DesignFlowStore check
            int? resultId = await DesignFlowStore.ResolveResultIdAsync(_db, userId, requestedId);
            if (!resultId.HasValue)
                return new AeroAiReply { Handled = true, Reply = "No design found to optimize. Create a design first." };

            var loaded = await DesignFlowStore.GetLoadedResultAsync(_db, userId, resultId.Value);
            if (loaded == null)
                return new AeroAiReply { Handled = true, Reply = $"Result #{resultId.Value} not found." };

            var source = loaded.Result;
            var input = source.DesignInput;

            if (input.FlowRateM3s <= 0 || input.TotalPressurePa <= 0)
                return new AeroAiReply { Handled = true, Reply = $"Result #{source.Id} has no valid flow / pressure duty point." };

            // 2. Run aerodynamic optimization
            var calibration = await _calibrationRepo.GetAllWithPointsAsync();
            var rates = await _db.cost_rates.AsNoTracking().ToListAsync();

            SizingOutcome outcome;
            try
            {
                outcome = DesignSizingEngine.Optimize(input, input.BladeProfile, calibration, rates);
            }
            catch (Exception ex)
            {
                return new AeroAiReply { Handled = true, Reply = "Optimization failed: " + ex.Message };
            }

            // 3. Comparative diagnostics
            var rows = DesignDiagnostics.BuildDeltaTable(source, outcome);
            var audit = DesignDiagnostics.BuildWarningAudit(source, outcome);

            int proposedId = ((await _db.design_results.MaxAsync(r => (int?)r.Id)) ?? 0) + 1;

            var sb = new StringBuilder();
            sb.AppendLine($"Optimize Result #{source.Id} ({(loaded.FromSessionMemory ? "session memory" : "database")})");
            sb.AppendLine();
            sb.AppendLine(DesignDiagnostics.RenderTable(source.Id, proposedId, rows));
            sb.AppendLine();
            sb.AppendLine(DesignDiagnostics.RenderWarningAudit(audit));
            sb.AppendLine();

            if (!outcome.Feasible)
            {
                sb.AppendLine("No fully feasible design found within the stated constraints. Best attempt shown.");
                sb.AppendLine();
            }
            else if (!outcome.Improved)
            {
                sb.AppendLine("No measurable improvement found over the current design.");
                sb.AppendLine();
            }

            // 4. Prompt user to save draft
            DesignFlowStore.PutDraft(userId, new OptimizationDraft
            {
                SourceResultId = source.Id,
                ProjectId = input.ProjectId,
                Input = outcome.Optimized.Input,
                Result = DesignSizingEngine.ToDesignResult(outcome.Optimized),
                ProposedResultId = proposedId
            });

            sb.Append($"Save as Result #{proposedId} (Optimized)? [Yes / No]");

            return new AeroAiReply { Handled = true, Reply = sb.ToString(), AwaitingConfirmation = true };
        }

        private async Task<AeroAiReply> SaveDraftAsync(int userId)
        {
            var draft = DesignFlowStore.TakeDraft(userId);
            if (draft == null)
                return new AeroAiReply { Handled = true, Reply = "The optimized draft expired. Run the optimization again." };

            // Ownership re-check before writing anything.
            bool owns = await _db.Projects.AnyAsync(p => p.Id == draft.ProjectId && p.UserId == userId);
            if (!owns)
                return new AeroAiReply { Handled = true, Reply = "Project not found." };

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var newInput = DesignFlowStore.CloneScalars(draft.Input);
                newInput.Id = 0;
                newInput.ProjectId = draft.ProjectId;
                newInput.CreatedAt = DateTime.UtcNow;
                _db.design_inputs.Add(newInput);
                await _db.SaveChangesAsync();

                var newResult = draft.Result;
                newResult.Id = 0;
                newResult.DesignInputId = newInput.Id;
                newResult.CalculatedAt = DateTime.UtcNow;
                _db.design_results.Add(newResult);
                await _db.SaveChangesAsync();

                await _curveService.GenerateAndSaveAsync(
                    newResult.Id, userId, newInput.BladeAngleDeg, newInput.SpeedRpm);

                await tx.CommitAsync();

                DesignFlowStore.Invalidate(userId, newResult.Id);
                DesignFlowStore.SetLastResult(userId, newResult.Id);

                return new AeroAiReply
                {
                    Handled = true,
                    SavedResultId = newResult.Id,
                    Reply = $"Saved as Result #{newResult.Id} (Optimized). Original Result #{draft.SourceResultId} is unchanged."
                };
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new AeroAiReply { Handled = true, Reply = "Save failed: " + ex.Message };
            }
        }
    }
}