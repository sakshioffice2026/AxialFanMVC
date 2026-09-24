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

        // Same limits the calculation / validation engines already use.
        private const double StallFlowCoefficientLimit = 0.15;
        private const double TipMachAdvisory = 0.7;

        private const string YesGo = "Yes, go ahead";
        private const string ChangeValues = "Change values";

        private const string AskFlowText =
            "Great! What is your required Volume Flow Rate? (You can just type a number, e.g., 12000 CFM or 10 m³/s)";

        private const string AskFlowShort =
            "What is your required Volume Flow Rate? (e.g., 12000 CFM or 10 m³/s)";

        private const string AskPressureQuestion =
            "What is your required Total Pressure in Pascals or inches WG?";

        private const string StartQuestion =
            "Back to your new project: would you like me to set up a fan design for it?";

        private const string ConfirmQuestion =
            "Back to your design: shall I run the aerodynamic calculations now?";

        private const string FlowRangeText =
            "That flow rate is outside the range I can size an axial fan for (0.05 to 300 m³/s, roughly 100 to 636,000 CFM). Could you double-check the value and unit?";

        private const string PressureRangeText =
            "That pressure is outside the range I can size an axial fan for (5 to 6000 Pa, roughly 0.02 to 24 inches WG). Could you double-check the value and unit?";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly Regex YesRx = new(
            @"^\s*(yes|yep|yeah|yup|y|sure|ok|okay|go ahead|please do|do it|run it|run|proceed|let'?s go|sounds good|set it up|definitely|absolutely|confirm)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoRx = new(
            @"^\s*(no|nope|nah|n|not now|not yet|later|no thanks|no thank you)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CancelRx = new(
            @"^\s*(cancel|stop|exit|quit|never\s*mind|forget it|abort)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ChangeRx = new(
            @"^\s*(change|edit|modify|update)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuestionRx = new(
            @"\?|^\s*(what|why|how|explain|which|when|where|who|can you|could you|tell me|define|difference)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly AxialFanDbContext _db;
        private readonly IAgentPendingActionStore _actionStore;
        private readonly IAgentActionExecutor _executor;
        private readonly DesignFlowStore _store;
        private readonly IExceptionHandlerRepository _exceptions;

        public DesignFlowService(
            AxialFanDbContext db,
            IAgentPendingActionStore actionStore,
            IAgentActionExecutor executor,
            DesignFlowStore store,
            IExceptionHandlerRepository exceptions)
        {
            _db = db;
            _actionStore = actionStore;
            _executor = executor;
            _store = store;
            _exceptions = exceptions;
        }

        // Step 1: proactive offer right after a project has been created.
        public async Task<FlowReply> OfferAsync(int userId, CancellationToken ct)
        {
            if (_store.Get(userId) is not null)
                return FlowReply.Idle;

            var since = DateTime.UtcNow.AddMinutes(-10);

            var project = await _db.Projects
                .AsNoTracking()
                .Where(p => p.UserId == userId && p.CreatedAt >= since && !p.DesignInputs.Any())
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (project is null || !_store.MarkOffered(userId, project.Id))
                return FlowReply.Idle;

            var label = "#" + project.Id.ToString(Inv) + " - " + project.Name;
            _store.Start(userId, project.Id, label);

            return FlowReply.Say(
                "Project '" + label + "' created! Would you like me to set up a new fan design for this project?",
                true,
                "Yes",
                "Not now");
        }

        public bool IsActive(int userId) => _store.Get(userId) is not null;

        public void Cancel(int userId) => _store.End(userId);

        public async Task<FlowReply> HandleAsync(int userId, string message, CancellationToken ct)
        {
            var session = _store.Get(userId);
            if (session is null)
                return FlowReply.Idle;

            await session.Lock.WaitAsync(ct);
            try
            {
                if (!ReferenceEquals(_store.Get(userId), session))
                    return FlowReply.Idle;

                session.LastTouchedUtc = DateTime.UtcNow;
                var text = (message ?? string.Empty).Trim();

                if (CancelRx.IsMatch(text))
                {
                    _store.End(userId);
                    return FlowReply.Say(
                        "No problem, I've stopped the design setup. Just ask me whenever you want to continue.",
                        false);
                }

                switch (session.Step)
                {
                    case DesignFlowStep.AwaitingStart:
                        return HandleStart(session, text);

                    case DesignFlowStep.AwaitingFlow:
                        return HandleFlow(session, text);

                    case DesignFlowStep.AwaitingPressure:
                        return HandlePressure(session, text);

                    case DesignFlowStep.AwaitingRunConfirm:
                        return await HandleConfirmAsync(session, text, userId, ct);

                    default:
                        return FlowReply.Idle;
                }
            }
            finally
            {
                session.Lock.Release();
            }
        }

        // Steps 2-3
        private FlowReply HandleStart(DesignFlowSession session, string text)
        {
            if (YesRx.IsMatch(text))
            {
                session.Step = DesignFlowStep.AwaitingFlow;
                return FlowReply.Say(AskFlowText, true);
            }

            if (NoRx.IsMatch(text))
            {
                _store.End(session.UserId);
                return FlowReply.Say(
                    "No problem. Just say the word whenever you'd like me to set up a design. You can ask me anything else in the meantime.",
                    false);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(StartQuestion, "Yes", "Not now");

            return FlowReply.Say(
                "Would you like me to set up a new fan design for this project? Just reply Yes or Not now.",
                true,
                "Yes",
                "Not now");
        }

        // Steps 4-5
        private FlowReply HandleFlow(DesignFlowSession session, string text)
        {
            var error = ApplyQuantities(session, text, true, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (flowSet)
            {
                if (session.Pressure is not null)
                {
                    session.Step = DesignFlowStep.AwaitingRunConfirm;
                    return FlowReply.Say(ConfirmText(session, FlowAck(session.Flow!)), true, YesGo, ChangeValues);
                }

                session.Step = DesignFlowStep.AwaitingPressure;
                return FlowReply.Say(FlowAck(session.Flow!) + " Now, what is your required Total Pressure in Pascals or inches WG?", true);
            }

            if (pressureSet)
            {
                return FlowReply.Say(
                    "Noted, pressure = " + N1(session.Pressure!.SiValue) + " Pa. I still need the flow rate. " + AskFlowShort,
                    true);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(AskFlowShort);

            return FlowReply.Say(
                "I couldn't read a flow rate from that. Please type a number with a unit, e.g. 12000 CFM, 10 m³/s or 36000 m³/h.",
                true);
        }

        // Steps 6-7
        private FlowReply HandlePressure(DesignFlowSession session, string text)
        {
            var error = ApplyQuantities(session, text, false, true, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (pressureSet)
            {
                session.Step = DesignFlowStep.AwaitingRunConfirm;
                return FlowReply.Say(ConfirmText(session, null), true, YesGo, ChangeValues);
            }

            if (flowSet)
            {
                return FlowReply.Say(
                    "Updated: flow rate = " + N2(session.Flow!.SiValue) + " m³/s. " + AskPressureQuestion,
                    true);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(AskPressureQuestion);

            return FlowReply.Say(
                "I couldn't read a pressure from that. Please type a number with a unit, e.g. 500 Pa or 2 inches WG.",
                true);
        }

        // Steps 7-9
        private async Task<FlowReply> HandleConfirmAsync(DesignFlowSession session, string text, int userId, CancellationToken ct)
        {
            var error = ApplyQuantities(session, text, false, false, out var flowSet, out var pressureSet);
            if (error is not null)
                return error;

            if (flowSet || pressureSet)
                return FlowReply.Say(ConfirmText(session, "Updated."), true, YesGo, ChangeValues);

            if (YesRx.IsMatch(text))
                return await RunAsync(session, userId, ct);

            if (NoRx.IsMatch(text) || ChangeRx.IsMatch(text))
            {
                return FlowReply.Say(
                    "Sure. What would you like to change? Send a new flow rate and/or pressure (for example 8000 CFM or 400 Pa), or say cancel to stop.",
                    true);
            }

            if (IsQuestion(text))
                return FlowReply.Pass(ConfirmQuestion, YesGo, ChangeValues);

            return FlowReply.Say(
                "Shall I run the aerodynamic calculations now? Reply Yes to go ahead, or send new values to change them.",
                true,
                YesGo,
                ChangeValues);
        }

        // Step 9: sizing + the existing physics pipeline (AgentActionExecutor).
        private async Task<FlowReply> RunAsync(DesignFlowSession session, int userId, CancellationToken ct)
        {
            var flow = session.Flow!.SiValue;
            var pressure = session.Pressure!.SiValue;
            var sizing = DesignSizingEngine.Size(flow, pressure);

            var parameters = new
            {
                projectId = session.ProjectId,
                flowRateM3s = Math.Round(flow, 4),
                totalPressurePa = Math.Round(pressure, 1),
                staticPressurePa = sizing.StaticPressurePa,
                speedRpm = sizing.SpeedRpm,
                bladeCount = sizing.BladeCount,
                tipDiameterMm = sizing.TipDiameterMm,
                temperatureCelsius = 25.0,
                hubRatio = sizing.HubRatio,
                bladeAngleDeg = sizing.BladeAngleDeg,
                targetEfficiencyPct = sizing.TargetEfficiencyPct,
                motorPowerKw = sizing.MotorPowerKw
            };

            var summary =
                "Create new '" + sizing.DutyClass + "' duty design in project #" + session.ProjectId.ToString(Inv) + ": " +
                N2(flow) + " m3/s @ " + N1(pressure) + " Pa total, " +
                sizing.TipDiameterMm.ToString("0", Inv) + " mm tip dia, " +
                sizing.SpeedRpm.ToString(Inv) + " rpm";

            try
            {
                var action = _actionStore.Stage(
                    userId,
                    AgentActionTypes.CreateNewDesign,
                    JsonSerializer.Serialize(parameters),
                    summary);

                // The user's explicit "Yes" in the chat is the confirmation.
                var exec = await _executor.ConfirmAndExecuteAsync(action.Id, userId);

                if (!exec.Success || exec.ResultId is null)
                {
                    return FlowReply.Say(
                        "I couldn't finish the calculation: " + exec.Message + " Reply Yes to try again, or cancel to stop.",
                        true,
                        YesGo,
                        "Cancel");
                }

                var card = await BuildCardAsync(exec.ResultId.Value, userId, sizing, ct);
                _store.End(userId);

                if (card is null)
                {
                    return FlowReply.Say(
                        "The design was created (result #" + exec.ResultId.Value.ToString(Inv) + "), but I couldn't load the diagnostics. Open it from the Results page.",
                        false);
                }

                return FlowReply.SayWithCard(
                    "Calculations complete for '" + session.ProjectLabel + "'. Here are the diagnostics for the new design:",
                    card);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    _exceptions.SaveException(nameof(DesignFlowService), nameof(RunAsync), ex.ToString(), userId);
                }
                catch
                {
                    Console.Error.WriteLine("[DesignFlowService.RunAsync] " + ex);
                }

                return FlowReply.Say(
                    "Something went wrong while running the calculations, and it has been logged. Reply Yes to try again, or cancel to stop.",
                    true,
                    YesGo,
                    "Cancel");
            }
        }

        // Step 10: diagnostic card, built only from stored engine results.
        private async Task<FlowCard?> BuildCardAsync(int resultId, int userId, DesignSizing sizing, CancellationToken ct)
        {
            var r = await _db.design_results
                .AsNoTracking()
                .Include(x => x.DesignInput)
                .FirstOrDefaultAsync(x => x.Id == resultId && x.DesignInput.Project.UserId == userId, ct);

            if (r is null)
                return null;

            var input = r.DesignInput;

            var efficiency = D(r.OverallEfficiencyPct);
            var shaftPower = D(r.ShaftPowerKw);
            var phi = D(r.FlowCoefficient);
            var psi = D(r.PressureCoefficient);
            var tipSpeed = D(r.TipSpeedMs);
            var tipMach = D(r.TipMachNumber);
            var noise = D(r.OverallNoiseDbA);
            var stress = D(r.BladeStressMpa);
            var yield = D(r.YieldStrengthMpa);
            var safetyFactor = D(r.SafetyFactor);

            var allMessages = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.WarningMessages))
            {
                try
                {
                    allMessages = JsonSerializer.Deserialize<List<string>>(r.WarningMessages) ?? new List<string>();
                }
                catch (JsonException)
                {
                    allMessages = new List<string>();
                }
            }

            var warnings = allMessages
                .Where(m => !m.StartsWith("Info", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var notes = allMessages
                .Where(m => m.StartsWith("Info", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.StartsWith("Info:", StringComparison.OrdinalIgnoreCase) ? m.Substring(5).Trim() : m)
                .Take(3)
                .ToList();

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

            var headline = new List<FlowMetric>
            {
                new() { Label = "Fan speed", Value = input.SpeedRpm.ToString(Inv) + " RPM" },
                new() { Label = "Efficiency", Value = F(efficiency, "0.0", "%") },
                new() { Label = "Shaft power", Value = F(shaftPower, "0.00", " kW") },
                new() { Label = "Stall risk", Value = stallElevated ? "Elevated" : "Low", Level = stallElevated ? "bad" : "good" }
            };

            var details = new List<FlowMetric>
            {
                new()
                {
                    Label = "Duty point",
                    Value = N2(input.FlowRateM3s) + " m³/s @ " + N1(input.TotalPressurePa) +
                            " Pa total (static assumed " + N1(input.StaticPressurePa) + " Pa)"
                },
                new()
                {
                    Label = "Geometry",
                    Value = input.TipDiameterMm.ToString("0", Inv) + " mm tip | hub ratio " +
                            input.HubRatio.ToString("0.00", Inv) + " | " +
                            input.BladeCount.ToString(Inv) + " blades @ " +
                            input.BladeAngleDeg.ToString("0.#", Inv) + "°"
                },
                new()
                {
                    Label = "Sizing basis",
                    Value = sizing.DutyClass + " duty class; speed chosen so tip speed stays within " +
                            sizing.MaxTipSpeedMs.ToString("0", Inv) + " m/s"
                },
                new() { Label = "Motor size", Value = N2(input.MotorPowerKw) + " kW (includes 15% margin)" },
                new()
                {
                    Label = "Tip speed / Mach",
                    Value = F(tipSpeed, "0.0", " m/s") + " | Mach " + F(tipMach, "0.000"),
                    Level = tipMach.HasValue && tipMach.Value > TipMachAdvisory ? "warn" : null
                },
                new()
                {
                    Label = "Noise",
                    Value = F(noise, "0.0", " dBA") + (string.IsNullOrWhiteSpace(r.NoiseRating) ? string.Empty : " (" + r.NoiseRating + ")")
                },
                new()
                {
                    Label = "Blade stress",
                    Value = F(stress, "0.0", " MPa") + " vs " + F(yield, "0", " MPa") + " yield | safety factor " +
                            F(safetyFactor, "0.00") +
                            (string.IsNullOrWhiteSpace(r.MaterialUsed) ? string.Empty : " | " + r.MaterialUsed)
                },
                new() { Label = "Coefficients", Value = "Flow " + phiText + " | Pressure " + F(psi, "0.000") }
            };

            foreach (var note in notes)
                details.Add(new FlowMetric { Label = "Note", Value = note });

            if (tipMach.HasValue && tipMach.Value > TipMachAdvisory)
            {
                warnings.Add(
                    "Tip Mach number " + tipMach.Value.ToString("0.000", Inv) + " exceeds the " +
                    TipMachAdvisory.ToString("0.0", Inv) +
                    " advisory threshold: expect elevated noise and compressibility losses. Review tip speed and diameter.");
            }

            return new FlowCard
            {
                Title = "Design diagnostics - result #" + resultId.ToString(Inv),
                Status = warnings.Count == 0 ? "ok" : "warning",
                ResultId = resultId,
                ResultUrl = "/Results/Result?resultId=" + resultId.ToString(Inv),
                Headline = headline,
                Details = details,
                Stall = stall,
                Warnings = warnings
            };
        }

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

            if (UnitParser.TryParseFlow(text, bareFlow, out var flow))
            {
                if (flow.SiValue < MinFlowM3s || flow.SiValue > MaxFlowM3s)
                    return FlowReply.Say(FlowRangeText, true);

                session.Flow = flow;
                flowSet = true;
            }

            if (UnitParser.TryParsePressure(text, barePressure, out var pressure))
            {
                if (pressure.SiValue < MinPressurePa || pressure.SiValue > MaxPressurePa)
                {
                    flowSet = false;
                    return FlowReply.Say(PressureRangeText, true);
                }

                session.Pressure = pressure;
                pressureSet = true;
            }

            return null;
        }

        private static string FlowAck(ParsedQuantity flow)
        {
            return flow.IsConverted
                ? "Got it! Converting " + Fmt(flow.Value) + " " + flow.Unit + " to " + N2(flow.SiValue) + " m³/s."
                : "Got it! Flow rate = " + N2(flow.SiValue) + " m³/s.";
        }

        private static string ConfirmText(DesignFlowSession session, string? prefix)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(prefix))
                sb.Append(prefix).Append(' ');

            var pressure = session.Pressure!;

            if (pressure.IsConverted)
            {
                sb.Append("Converting ").Append(Fmt(pressure.Value)).Append(' ').Append(pressure.Unit)
                  .Append(" to ").Append(N1(pressure.SiValue)).Append(" Pa. ");
            }

            sb.Append("Target set: Flow Rate = ").Append(N2(session.Flow!.SiValue))
              .Append(" m³/s | Pressure = ").Append(N1(pressure.SiValue))
              .Append(" Pa. Shall I run the aerodynamic calculations for you now?");

            return sb.ToString();
        }

        private static bool IsQuestion(string text) => QuestionRx.IsMatch(text);

        private static double? D(object? value)
            => value is null ? null : Convert.ToDouble(value, Inv);

        private static string F(double? value, string format, string suffix = "")
            => value.HasValue ? value.Value.ToString(format, Inv) + suffix : "n/a";

        private static string N1(double value) => value.ToString("0.#", Inv);

        private static string N2(double value) => value.ToString("0.##", Inv);

        private static string Fmt(double value)
            => value.ToString(value % 1 == 0 ? "N0" : "#,0.##", Inv);
    }
}