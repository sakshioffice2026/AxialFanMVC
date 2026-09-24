using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Services.AeroAi
{
    public sealed class DesignFlowService
    {
        private const double MinFlowM3s = 0.05;
        private const double MaxFlowM3s = 300.0;
        private const double MinPressurePa = 5.0;
        private const double MaxPressurePa = 6000.0;

        private const double StallFlowCoefficientLimit = 0.15;
        private const double TipMachAdvisory = 0.7;

        private const string Yes = "Yes";
        private const string No = "No";
        private const string DefaultChip = "Default";
        private const string CustomChip = "Custom";
        private const string SkipChip = "Skip";
        private const string ModifyChip = "Modify parameters";
        private const string RecalcChip = "Recalculate with Default";
        private const string DiscardChip = "Discard and exit";

        private const string AskValuesShort =
            "Please enter your required Volume Flow Rate (e.g., 10 m³/s or 12,000 CFM) and Total Pressure (e.g., 600 Pa).";

        private const string PathMenu =
            "How would you like to proceed with the fan sizing?\n\n" +
            "[1] Default Path (Fast)\n" +
            "    AeroAI automatically selects optimal diameter, RPM, odd blade count, and standard Aluminum 6061-T6 material based on turbomachinery physics.\n\n" +
            "[2] Custom Path (Advanced)\n" +
            "    Specify your own geometric limits, blade count, material, drive type, or ambient site conditions.";

        private const string CustomOptionsList =
            "1. Blade Count (e.g., 5, 7, 9)\n" +
            "2. Material (Aluminum 6061-T6, Aluminum 5052-H32, Mild Steel A36, Stainless Steel 304, Stainless Steel 316, FRP / Composite, PAG)\n" +
            "3. Maximum Tip Diameter Limit (e.g., Max 1000 mm casing limit)\n" +
            "4. Drive / Speed Type (Direct Drive 1450 RPM, VFD, V-Belt)\n" +
            "5. Ambient Conditions (e.g., Altitude 1500m, Temp 40°C)";

        private const string PathQuestion =
            "Back to your design: would you like the [1] Default path or the [2] Custom path?";

        private const string SaveQuestion =
            "Back to your design: shall I save it to the project?";

        private const string RevisionQuestion =
            "Back to your design: would you like to modify parameters, recalculate with default settings, or discard it?";

        private const string FlowRangeText =
            "That flow rate is outside the range I can size an axial fan for (0.05 to 300 m³/s, roughly 100 to 636,000 CFM). Could you double-check the value and unit?";

        private const string PressureRangeText =
            "That pressure is outside the range I can size an axial fan for (5 to 6000 Pa, roughly 0.02 to 24 inches WG). Could you double-check the value and unit?";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly Regex StartRx = new(
            @"\bdesign\s+for\s+project\s*(?:id)?\s*[=:#]?\s*(?<id>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YesRx = new(
            @"^\s*(yes|yep|yeah|yup|y|sure|ok|okay|go ahead|yes,? go ahead|please do|do it|save|save it|proceed|confirm)\s*[.!]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoRx = new(
            @"^\s*(no|nope|nah|n|not now|not yet|no thanks|no thank you|don'?t|do not|don'?t save)\s*[.!]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CancelRx = new(
            @"^\s*(cancel|stop|exit|quit|never\s*mind|forget it|abort)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DefaultPathRx = new(
            @"^\s*(?:\[?1\]?|default(?:\s*path)?(?:\s*\(fast\))?|fast|automatic|auto)\s*[.!)]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CustomPathRx = new(
            @"^\s*(?:\[?2\]?|custom(?:\s*path)?(?:\s*\(advanced\))?|advanced|manual)\s*[.!)]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ModifyRx = new(
            @"^\s*(?:\[?1\]?|modify|tweak|change|edit|adjust|custom)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RecalcDefaultRx = new(
            @"^\s*(?:\[?2\]?|recalculate|recalc|default|reset)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DiscardRx = new(
            @"^\s*(?:\[?3\]?|discard|delete|drop|exit)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuestionRx = new(
            @"\?|^\s*(what|why|how|explain|which|when|where|who|can you|could you|tell me|define|difference)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly AxialFanDbContext _db;
        private readonly IAgentPendingActionStore _actionStore;
        private readonly IAgentActionExecutor _executor;
        private readonly DesignPreviewService _preview;
        private readonly DesignFlowStore _store;
        private readonly IExceptionHandlerRepository _exceptions;

        public DesignFlowService(
            AxialFanDbContext db,
            IAgentPendingActionStore actionStore,
            IAgentActionExecutor executor,
            DesignPreviewService preview,
            DesignFlowStore store,
            IExceptionHandlerRepository exceptions)
        {
            _db = db;
            _actionStore = actionStore;
            _executor = executor;
            _preview = preview;
            _store = store;
            _exceptions = exceptions;
        }

        public bool IsActive(int userId) => _store.Get(userId) is not null;

        public void Cancel(int userId) => _store.End(userId);

        public async Task<FlowReply> HandleAsync(int userId, string message, CancellationToken ct)
        {
            var text = (message ?? string.Empty).Trim();

            // "Design for projectId=7" starts (or restarts) the flow.
            var start = StartRx.Match(text);
            if (start.Success)
            {
                if (!int.TryParse(start.Groups["id"].Value, NumberStyles.None, Inv, out var projectId) || projectId <= 0)
                    return FlowReply.Say("That project id doesn't look valid. Please try again, e.g. \"Design for projectId=7\".", false);

                return await StartAsync(userId, projectId, ct);
            }

            var session = _store.Get(userId);
            if (session is null)
                return FlowReply.Idle;

            await session.Lock.WaitAsync(ct);
            try
            {
                if (!ReferenceEquals(_store.Get(userId), session))
                    return FlowReply.Idle;

                session.LastTouchedUtc = DateTime.UtcNow;

                if (CancelRx.IsMatch(text) && session.Step != DesignFlowStep.AwaitingRevisionOrDiscard)
                {
                    var hadDraft = session.Draft is not null;
                    _store.End(userId);

                    return FlowReply.Say(
                        hadDraft
                            ? "Draft discarded. Nothing was saved to Project " + session.ProjectLabel + "."
                            : "No problem, I've stopped the design setup for Project " + session.ProjectLabel + ".",
                        false);
                }

                switch (session.Step)
                {
                    case DesignFlowStep.AwaitingValues:
                        return HandleValues(session, text);

                    case DesignFlowStep.AwaitingPathChoice:
                        return await HandlePathChoiceAsync(session, text, ct);

                    case DesignFlowStep.AwaitingCustomOptions:
                        return await HandleCustomOptionsAsync(session, text, ct);

                    case DesignFlowStep.AwaitingSaveConfirm:
                        return await HandleSaveConfirmAsync(session, text, userId, ct);

                    case DesignFlowStep.AwaitingRevisionOrDiscard:
                        return await HandleRevisionAsync(session, text, ct);

                    default:
                        return FlowReply.Idle;
                }
            }
            finally
            {
                session.Lock.Release();
            }
        }

        // ------------------------------------------------------------------ start

        private async Task<FlowReply> StartAsync(int userId, int projectId, CancellationToken ct)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Id == projectId && p.UserId == userId)
                .Select(p => new { p.Id, p.Name })
                .FirstOrDefaultAsync(ct);

            if (project is null)
            {
                _store.End(userId);
                return FlowReply.Say(
                    "I couldn't find Project #" + projectId.ToString(Inv) + " in your account. Please check the project id and try again.",
                    false);
            }

            var label = "#" + project.Id.ToString(Inv) + " (" + project.Name + ")";
            _store.Start(userId, project.Id, label);

            return FlowReply.Say("Project " + label + " loaded. " + AskValuesShort, true);
        }

        // ------------------------------------------------------- phase 1: duty point

        private FlowReply HandleValues(DesignFlowSession session, string text)
        {
            if (NoRx.IsMatch(text))
            {
                _store.End(session.UserId);
                return FlowReply.Say("Okay, I've stopped the design setup for Project " + session.ProjectLabel + ".", false);
            }

            var error = ApplyQuantities(
                session,
                text,
                session.Flow is null && session.Pressure is not null,
                session.Pressure is null && session.Flow is not null,
                out var flowSet,
                out var pressureSet);

            if (error is not null)
                return error;

            if (session.Flow is not null && session.Pressure is not null && (flowSet || pressureSet))
            {
                session.Step = DesignFlowStep.AwaitingPathChoice;
                return FlowReply.Say(DutyText(session, null) + "\n\n" + PathMenu, true, DefaultChip, CustomChip);
            }

            if (session.Flow is not null && session.Pressure is null)
            {
                return FlowReply.Say(
                    "Flow Rate noted: " + session.Flow.SiValue.ToString("0.00", Inv) + " m³/s. Please also enter your required Total Pressure (e.g., 600 Pa or 2 inches WG).",
                    true);
            }

            if (session.Pressure is not null && session.Flow is null)
            {
                return FlowReply.Say(
                    "Total Pressure noted: " + N1(session.Pressure.SiValue) + " Pa. Please also enter your required Volume Flow Rate (e.g., 10 m³/s or 12,000 CFM).",
                    true);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(AskValuesShort);

            return FlowReply.Say(
                "I couldn't read those values. Please type both with units, e.g. \"Flow rate is 10 m³/s, Pressure is 600 Pa\" or \"12000 CFM and 2 inches WG\".",
                true);
        }

        // ------------------------------------------------------ phase 2: path choice

        private async Task<FlowReply> HandlePathChoiceAsync(DesignFlowSession session, string text, CancellationToken ct)
        {
            var error = ApplyQuantities(session, text, false, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (flowSet || pressureSet)
                return FlowReply.Say(DutyText(session, "Updated.") + "\n\n" + PathMenu, true, DefaultChip, CustomChip);

            if (NoRx.IsMatch(text))
            {
                _store.End(session.UserId);
                return FlowReply.Say("Okay, I've stopped the design setup for Project " + session.ProjectLabel + ".", false);
            }

            if (DefaultPathRx.IsMatch(text))
            {
                session.Options = new CustomOptions();
                return await RunDraftAsync(session, false, ct);
            }

            if (CustomPathRx.IsMatch(text))
            {
                session.Step = DesignFlowStep.AwaitingCustomOptions;
                return FlowReply.Say(CustomPrompt(session), true, SkipChip);
            }

            // Options typed straight away ("9 blades, SS316, 1000mm") imply the custom path.
            var parsed = CustomOptionsParser.Parse(text);

            if (parsed.Error is not null)
                return FlowReply.Say(parsed.Error + " Please try again.", true, DefaultChip, CustomChip);

            if (parsed.Options.HasAny)
            {
                session.Options = new CustomOptions();
                session.Options.MergeFrom(parsed.Options);
                return await RunDraftAsync(session, true, ct);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(PathQuestion, DefaultChip, CustomChip);

            return FlowReply.Say("Please choose [1] Default Path or [2] Custom Path.", true, DefaultChip, CustomChip);
        }

        // ---------------------------------------------- phase 2B: custom parameters

        private async Task<FlowReply> HandleCustomOptionsAsync(DesignFlowSession session, string text, CancellationToken ct)
        {
            var error = ApplyQuantities(session, text, false, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            var parsed = CustomOptionsParser.Parse(text);

            if (parsed.IsSkip)
                return await RunDraftAsync(session, session.Options.HasAny, ct);

            if (parsed.Error is not null)
                return FlowReply.Say(parsed.Error + " Please try again, or type 'Skip' to keep the defaults.", true, SkipChip);

            if (parsed.Options.HasAny)
            {
                session.Options.MergeFrom(parsed.Options);
                return await RunDraftAsync(session, true, ct);
            }

            if (flowSet || pressureSet)
                return FlowReply.Say(DutyText(session, "Updated.") + "\n\n" + CustomPromptShort(), true, SkipChip);

            if (IsQuestion(text))
                return FlowReply.Pass(CustomPromptShort(), SkipChip);

            return FlowReply.Say(
                "I couldn't read any custom settings from that. " + CustomPromptShort(),
                true,
                SkipChip);
        }

        // ----------------------------------------------- phase 3: save or keep draft

        private async Task<FlowReply> HandleSaveConfirmAsync(DesignFlowSession session, string text, int userId, CancellationToken ct)
        {
            var error = ApplyQuantities(session, text, false, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (flowSet || pressureSet)
                return await RunDraftAsync(session, session.Options.HasAny, ct);

            if (YesRx.IsMatch(text))
                return await SaveAsync(session, userId, ct);

            if (NoRx.IsMatch(text))
            {
                session.Step = DesignFlowStep.AwaitingRevisionOrDiscard;
                return FlowReply.Say(RevisionMenu(session), true, ModifyChip, RecalcChip, DiscardChip);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(SaveQuestion, Yes, No);

            var parsed = CustomOptionsParser.Parse(text);

            if (parsed.Error is not null)
                return FlowReply.Say(parsed.Error + " Please try again.", true, Yes, No);

            if (parsed.Options.HasAny)
            {
                session.Options.MergeFrom(parsed.Options);
                return await RunDraftAsync(session, true, ct);
            }

            return FlowReply.Say(
                "Shall I save this design to Project #" + session.ProjectId.ToString(Inv) +
                "? Reply Yes or No, or send new values (for example \"Change material to FRP\").",
                true,
                Yes,
                No);
        }

        private async Task<FlowReply> HandleRevisionAsync(DesignFlowSession session, string text, CancellationToken ct)
        {
            var error = ApplyQuantities(session, text, false, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (flowSet || pressureSet)
                return await RunDraftAsync(session, session.Options.HasAny, ct);

            var parsed = CustomOptionsParser.Parse(text);

            if (parsed.Error is not null)
                return FlowReply.Say(parsed.Error + " Please try again.", true, ModifyChip, RecalcChip, DiscardChip);

            if (parsed.Options.HasAny)
            {
                session.Options.MergeFrom(parsed.Options);
                return await RunDraftAsync(session, true, ct);
            }

            if (DiscardRx.IsMatch(text) || CancelRx.IsMatch(text))
            {
                _store.End(session.UserId);
                return FlowReply.Say(
                    "Draft discarded. Nothing was saved to Project " + session.ProjectLabel + ". Say \"Design for projectId=" +
                    session.ProjectId.ToString(Inv) + "\" whenever you want to start again.",
                    false);
            }

            if (ModifyRx.IsMatch(text))
            {
                session.Step = DesignFlowStep.AwaitingCustomOptions;

                var sb = new StringBuilder();
                sb.Append("Custom mode for ")
                  .Append(session.Flow!.SiValue.ToString("0.00", Inv)).Append(" m³/s @ ")
                  .Append(N1(session.Pressure!.SiValue)).Append(" Pa.\n");

                if (session.Options.HasAny)
                {
                    sb.Append("\nCurrent custom settings:\n");
                    foreach (var line in session.Options.Describe())
                        sb.Append("• ").Append(line).Append('\n');
                }

                sb.Append("\nSend only what you want to change (e.g., \"Change material to FRP\" or \"Max diameter 900 mm\"), or type 'Skip' to recalculate as is.");

                return FlowReply.Say(sb.ToString(), true, SkipChip);
            }

            if (RecalcDefaultRx.IsMatch(text))
            {
                session.Options = new CustomOptions();
                return await RunDraftAsync(session, false, ct);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(RevisionQuestion, ModifyChip, RecalcChip, DiscardChip);

            return FlowReply.Say(
                "Please choose: [1] Modify parameters, [2] Recalculate with Default settings, or [3] Discard and exit.",
                true,
                ModifyChip,
                RecalcChip,
                DiscardChip);
        }

        // ---------------------------------------------- calculate (memory only) / save

        // Calculates in memory. Nothing is written to the database here.
        private async Task<FlowReply> RunDraftAsync(DesignFlowSession session, bool custom, CancellationToken ct)
        {
            try
            {
                var flow = session.Flow!.SiValue;
                var pressure = session.Pressure!.SiValue;
                var options = custom ? session.Options : null;

                var sizing = DesignSizingEngine.Size(flow, pressure, options);
                var parameters = BuildParameters(session, sizing, options);

                var preview = await _preview.PreviewAsync(parameters, ct);

                if (!preview.Success)
                {
                    session.Step = DesignFlowStep.AwaitingPathChoice;
                    return FlowReply.Say(
                        "I couldn't finish the calculation: " + preview.Error + " Choose a path to try again.",
                        true,
                        DefaultChip,
                        CustomChip);
                }

                var card = BuildCard(preview, sizing, parameters, true, 0);

                session.Draft = new DesignDraft
                {
                    IsCustom = sizing.IsCustom,
                    Sizing = sizing,
                    Options = session.Options.Clone(),
                    ParametersJson = JsonSerializer.Serialize(parameters),
                    Summary = BuildSummary(session, sizing),
                    Card = card
                };

                session.Step = DesignFlowStep.AwaitingSaveConfirm;

                var sb = new StringBuilder();

                if (sizing.IsCustom)
                {
                    sb.Append("Custom constraints applied:\n");
                    foreach (var line in AppliedLines(session.Options, sizing))
                        sb.Append(line).Append('\n');

                    sb.Append("\nCustom Design Summary for Project ").Append(session.ProjectLabel)
                      .Append(" (not saved yet). Shall I save this custom design record to Project #")
                      .Append(session.ProjectId.ToString(Inv)).Append('?');
                }
                else
                {
                    sb.Append("Running aerodynamic calculations using optimal physics defaults...\n\n")
                      .Append("Calculations complete for Project ").Append(session.ProjectLabel)
                      .Append(" (not saved yet). Shall I save this design record to Project #")
                      .Append(session.ProjectId.ToString(Inv)).Append('?');
                }

                return FlowReply.SayWithCard(sb.ToString(), card, true, Yes, No);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSafe(nameof(RunDraftAsync), ex, session.UserId);
                session.Step = DesignFlowStep.AwaitingPathChoice;

                return FlowReply.Say(
                    "Something went wrong while running the calculations, and it has been logged. Choose a path to try again.",
                    true,
                    DefaultChip,
                    CustomChip);
            }
        }

        // The only place that writes to MySQL: the user said Yes to the unsaved draft.
        private async Task<FlowReply> SaveAsync(DesignFlowSession session, int userId, CancellationToken ct)
        {
            var draft = session.Draft;
            if (draft is null)
            {
                session.Step = DesignFlowStep.AwaitingPathChoice;
                return FlowReply.Say("There is no calculated design to save yet. Choose a path to calculate one.", true, DefaultChip, CustomChip);
            }

            try
            {
                var action = _actionStore.Stage(
                    userId,
                    AgentActionTypes.CreateNewDesign,
                    draft.ParametersJson,
                    draft.Summary);

                // The user's explicit "Yes" in the chat is the confirmation.
                var exec = await _executor.ConfirmAndExecuteAsync(action.Id, userId);

                if (!exec.Success || exec.ResultId is null)
                {
                    return FlowReply.Say(
                        "I couldn't save the design: " + exec.Message + " Reply Yes to try again, or No to keep it as a draft.",
                        true,
                        Yes,
                        No);
                }

                var resultId = exec.ResultId.Value;
                var source = draft.Card;

                var saved = new FlowCard
                {
                    Title = "Design diagnostics - result #" + resultId.ToString(Inv),
                    Status = source?.Status ?? "ok",
                    IsDraft = false,
                    ResultId = resultId,
                    ResultUrl = "/Results/Result?resultId=" + resultId.ToString(Inv),
                    Headline = source?.Headline ?? new List<FlowMetric>(),
                    Details = source?.Details ?? new List<FlowMetric>(),
                    Stall = source?.Stall,
                    Warnings = source?.Warnings ?? new List<string>()
                };

                _store.End(userId);

                return FlowReply.SayWithCard(
                    "Design saved to Project " + session.ProjectLabel + " as Result #" + resultId.ToString(Inv) + ".",
                    saved);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSafe(nameof(SaveAsync), ex, userId);

                return FlowReply.Say(
                    "Something went wrong while saving, and it has been logged. Reply Yes to try again, or No to keep it as a draft.",
                    true,
                    Yes,
                    No);
            }
        }

        // ---------------------------------------------------------------- builders

        private static DesignRunParameters BuildParameters(DesignFlowSession session, DesignSizing sizing, CustomOptions? options)
        {
            return new DesignRunParameters
            {
                ProjectId = session.ProjectId,
                FlowRateM3s = Math.Round(session.Flow!.SiValue, 4),
                TotalPressurePa = Math.Round(session.Pressure!.SiValue, 1),
                StaticPressurePa = sizing.StaticPressurePa,
                SpeedRpm = sizing.SpeedRpm,
                BladeCount = sizing.BladeCount,
                TipDiameterMm = sizing.TipDiameterMm,
                TemperatureCelsius = sizing.TemperatureC,
                HubRatio = sizing.HubRatio,
                BladeAngleDeg = sizing.BladeAngleDeg,
                TargetEfficiencyPct = sizing.TargetEfficiencyPct,
                MotorPowerKw = sizing.MotorPowerKw,
                BladeMaterial = sizing.Material,
                DensityKgM3 = sizing.DensityKgM3,
                AltitudeM = sizing.AltitudeM,
                AtmosphericPressureKPa = sizing.AtmosphericPressureKPa,
                MaxTipDiameterMm = options?.MaxTipDiameterMm,
                PreferredBladeCount = options?.BladeCount
            };
        }

        private static string BuildSummary(DesignFlowSession session, DesignSizing sizing)
        {
            return
                "Create new '" + sizing.DutyClass + "' duty " + (sizing.IsCustom ? "custom " : string.Empty) +
                "design in project #" + session.ProjectId.ToString(Inv) + ": " +
                N2(session.Flow!.SiValue) + " m3/s @ " + N1(session.Pressure!.SiValue) + " Pa total, " +
                sizing.TipDiameterMm.ToString("0", Inv) + " mm tip dia, " +
                sizing.SpeedRpm.ToString(Inv) + " rpm, " +
                sizing.BladeCount.ToString(Inv) + " blades, " + sizing.Material;
        }

        private static FlowCard BuildCard(
            DesignPreviewResult r,
            DesignSizing z,
            DesignRunParameters p,
            bool isDraft,
            int resultId)
        {
            var allMessages = r.Warnings ?? new List<string>();

            var warnings = allMessages
                .Where(m => !m.StartsWith("Info", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var notes = allMessages
                .Where(m => m.StartsWith("Info", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.StartsWith("Info:", StringComparison.OrdinalIgnoreCase) ? m.Substring(5).Trim() : m)
                .Take(3)
                .ToList();

            var phi = r.FlowCoefficient;
            var tipMach = r.TipMachNumber;

            var stallElevated =
                (phi.HasValue && phi.Value < StallFlowCoefficientLimit) ||
                warnings.Any(w => w.Contains("stall", StringComparison.OrdinalIgnoreCase));

            var phiText = phi.HasValue ? phi.Value.ToString("0.000", Inv) : "n/a";

            var stall = stallElevated
                ? new FlowCallout
                {
                    Level = "bad",
                    Text = "Stall risk is elevated: flow coefficient " + phiText + " is below the " +
                           StallFlowCoefficientLimit.ToString("0.00", Inv) +
                           " limit. Increasing the flow rate or reducing the fan speed lowers the risk."
                }
                : new FlowCallout
                {
                    Level = "good",
                    Text = "Stall risk is low: flow coefficient " + phiText + " is above the " +
                           StallFlowCoefficientLimit.ToString("0.00", Inv) + " limit."
                };

            var tipSpeed = r.TipSpeedMs ?? z.TipSpeedMs;

            var headline = new List<FlowMetric>
            {
                new() { Label = "Fan speed", Value = z.SpeedRpm.ToString(Inv) + " RPM" },
                new() { Label = "Efficiency", Value = F(r.OverallEfficiencyPct, "0.0", "%") },
                new() { Label = "Shaft power", Value = F(r.ShaftPowerKw, "0.00", " kW") },
                new() { Label = "Stall risk", Value = stallElevated ? "Elevated" : "Low", Level = stallElevated ? "bad" : "good" }
            };

            var details = new List<FlowMetric>
            {
                new()
                {
                    Label = "Duty point",
                    Value = N2(p.FlowRateM3s) + " m³/s @ " + N1(p.TotalPressurePa) +
                            " Pa total (static assumed " + N1(p.StaticPressurePa) + " Pa)"
                },
                new()
                {
                    Label = "Tip diameter",
                    Value = z.TipDiameterMm.ToString("0", Inv) + " mm" +
                            (z.DiameterConstrained ? " (casing limit met)" : string.Empty) +
                            " | hub ratio " + z.HubRatio.ToString("0.00", Inv)
                },
                new()
                {
                    Label = "Speed / drive",
                    Value = z.SpeedRpm.ToString(Inv) + " RPM (" + z.DriveType + ") | tip speed " +
                            tipSpeed.ToString("0.0", Inv) + " m/s | Mach " + F(tipMach, "0.000"),
                    Level = (tipMach.HasValue && tipMach.Value > TipMachAdvisory) || tipSpeed > z.MaxTipSpeedMs * 1.0001
                        ? "warn"
                        : null
                },
                new()
                {
                    Label = "Geometry",
                    Value = z.BladeCount.ToString(Inv) + " blades @ " + z.BladeAngleDeg.ToString("0.#", Inv) + "°" +
                            (z.BladeCount % 2 == 1 ? " (odd count for low noise)" : string.Empty)
                },
                new()
                {
                    Label = "Material",
                    Value = string.IsNullOrWhiteSpace(r.MaterialUsed) ? z.Material : r.MaterialUsed
                },
                new()
                {
                    Label = "Sizing basis",
                    Value = z.DutyClass + " duty class; air density " + z.DensityKgM3.ToString("0.000", Inv) + " kg/m³"
                },
                new() { Label = "Motor size", Value = N2(z.MotorPowerKw) + " kW (includes 15% margin)" },
                new()
                {
                    Label = "Noise",
                    Value = F(r.OverallNoiseDbA, "0.0", " dBA") +
                            (string.IsNullOrWhiteSpace(r.NoiseRating) ? string.Empty : " (" + r.NoiseRating + ")")
                },
                new()
                {
                    Label = "Blade stress",
                    Value = F(r.BladeStressMpa, "0.0", " MPa") + " vs " + F(r.YieldStrengthMpa, "0", " MPa") +
                            " yield | safety factor " + F(r.SafetyFactor, "0.0")
                },
                new() { Label = "Coefficients", Value = "Flow " + phiText + " | Pressure " + F(r.PressureCoefficient, "0.000") }
            };

            foreach (var note in notes)
                details.Add(new FlowMetric { Label = "Note", Value = note });

            var audit = new List<string>();
            audit.AddRange(z.Notes);
            audit.AddRange(warnings);

            if (tipMach.HasValue && tipMach.Value > TipMachAdvisory)
            {
                audit.Add(
                    "Tip Mach number " + tipMach.Value.ToString("0.000", Inv) + " exceeds the " +
                    TipMachAdvisory.ToString("0.0", Inv) +
                    " advisory threshold: expect elevated noise and compressibility losses. Review tip speed and diameter.");
            }

            return new FlowCard
            {
                Title = isDraft
                    ? "Design draft - not saved"
                    : "Design diagnostics - result #" + resultId.ToString(Inv),
                Status = audit.Count == 0 ? "ok" : "warning",
                IsDraft = isDraft,
                ResultId = resultId,
                ResultUrl = resultId > 0 ? "/Results/Result?resultId=" + resultId.ToString(Inv) : string.Empty,
                Headline = headline,
                Details = details,
                Stall = stall,
                Warnings = audit
            };
        }

        private static List<string> AppliedLines(CustomOptions o, DesignSizing z)
        {
            var lines = new List<string>();

            if (o.BladeCount.HasValue)
                lines.Add("✓ Blade Count: " + o.BladeCount.Value.ToString(Inv) + " blades");

            if (!string.IsNullOrEmpty(o.Material))
            {
                var mat = MaterialLibrary.Get(o.Material);

                lines.Add(
                    "✓ Material: " + o.Material + " (Yield Strength: " +
                    (mat.YieldStrengthPa / 1e6).ToString("0", Inv) + " MPa, Density: " +
                    mat.DensityKgM3.ToString("0", Inv) + " kg/m³)");
            }

            if (o.MaxTipDiameterMm.HasValue)
            {
                lines.Add(
                    z.DiameterConstrained
                        ? "✓ Max Tip Diameter: constrained to " + z.TipDiameterMm.ToString("0", Inv) +
                          " mm (speed recalculated to " + z.SpeedRpm.ToString(Inv) + " RPM via " + z.DriveType +
                          " to maintain " + N1(z.TotalPressurePa) + " Pa)"
                        : "✓ Max Tip Diameter: " + o.MaxTipDiameterMm.Value.ToString("0.#", Inv) +
                          " mm (the sized " + z.TipDiameterMm.ToString("0", Inv) + " mm fan is already within the limit)");
            }

            if (!string.IsNullOrEmpty(o.DriveType) || o.SpeedRpm.HasValue)
                lines.Add("✓ Drive / Speed: " + z.DriveType + " at " + z.SpeedRpm.ToString(Inv) + " RPM");

            if (o.AltitudeM.HasValue || o.TemperatureC.HasValue)
            {
                lines.Add(
                    "✓ Ambient: altitude " + z.AltitudeM.ToString("0.#", Inv) + " m, " +
                    z.TemperatureC.ToString("0.#", Inv) + " °C (air density " +
                    z.DensityKgM3.ToString("0.000", Inv) + " kg/m³)");
            }

            return lines;
        }

        private static string DutyText(DesignFlowSession session, string? prefix)
        {
            var flow = session.Flow!;
            var pressure = session.Pressure!;

            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(prefix))
                sb.Append(prefix).Append(' ');

            sb.Append("Target duty point set:\n");

            sb.Append("• Flow Rate: ").Append(flow.SiValue.ToString("0.00", Inv)).Append(" m³/s");
            if (flow.IsConverted)
                sb.Append(" (from ").Append(Fmt(flow.Value)).Append(' ').Append(flow.Unit).Append(')');
            sb.Append('\n');

            sb.Append("• Total Pressure: ").Append(N1(pressure.SiValue)).Append(" Pa");
            if (pressure.IsConverted)
                sb.Append(" (from ").Append(Fmt(pressure.Value)).Append(' ').Append(pressure.Unit).Append(')');

            return sb.ToString();
        }

        private static string CustomPrompt(DesignFlowSession session)
        {
            return
                "Custom configuration mode activated for " + session.Flow!.SiValue.ToString("0.00", Inv) +
                " m³/s @ " + N1(session.Pressure!.SiValue) + " Pa.\n\n" +
                "Please specify any of the following parameters (you can answer all at once or type 'Skip' to leave any setting as default):\n\n" +
                CustomOptionsList;
        }

        private static string CustomPromptShort()
        {
            return
                "Send any of: blade count, material, max tip diameter, drive / speed, or ambient conditions " +
                "(e.g., \"9 blades, SS316, 1000 mm\"), or type 'Skip'.";
        }

        private static string RevisionMenu(DesignFlowSession session)
        {
            return
                "Design not saved to Project #" + session.ProjectId.ToString(Inv) + ".\n\n" +
                "What would you like to do next?\n" +
                "• [1] Modify parameters (Switch to Custom / tweak values)\n" +
                "• [2] Recalculate with Default settings\n" +
                "• [3] Discard and exit";
        }

        // -------------------------------------------------------------- utilities

        // Parses flow and/or pressure from one message, validates both, then stores them.
        private FlowReply? ApplyQuantities(
            DesignFlowSession session,
            string text,
            bool bareFlow,
            bool barePressure,
            out bool flowSet,
            out bool pressureSet)
        {
            flowSet = false;
            pressureSet = false;

            ParsedQuantity? flow = null;
            ParsedQuantity? pressure = null;

            if (UnitParser.TryParseFlow(text, bareFlow, out var f))
            {
                if (f.SiValue < MinFlowM3s || f.SiValue > MaxFlowM3s)
                    return FlowReply.Say(FlowRangeText, true);

                flow = f;
            }

            if (UnitParser.TryParsePressure(text, barePressure, out var p))
            {
                if (p.SiValue < MinPressurePa || p.SiValue > MaxPressurePa)
                    return FlowReply.Say(PressureRangeText, true);

                pressure = p;
            }

            if (flow is not null)
            {
                session.Flow = flow;
                flowSet = true;
            }

            if (pressure is not null)
            {
                session.Pressure = pressure;
                pressureSet = true;
            }

            return null;
        }

        private void LogSafe(string method, Exception ex, int userId)
        {
            try
            {
                _exceptions.SaveException(nameof(DesignFlowService), method, ex.ToString(), userId);
            }
            catch
            {
                Console.Error.WriteLine("[DesignFlowService." + method + "] " + ex);
            }
        }

        private static bool IsQuestion(string text) => QuestionRx.IsMatch(text);

        private static string F(double? value, string format, string suffix = "")
            => value.HasValue ? value.Value.ToString(format, Inv) + suffix : "n/a";

        private static string N1(double value) => value.ToString("0.#", Inv);

        private static string N2(double value) => value.ToString("0.##", Inv);

        private static string Fmt(double value)
            => value.ToString(value % 1 == 0 ? "N0" : "#,0.##", Inv);
    }
}