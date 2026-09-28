using System.Collections.Concurrent;

namespace AxialFanMVC.Services.AeroAi
{
    public enum DesignFlowKind
    {
        Design,
        Optimize
    }

    public enum DesignFlowStep
    {
        AwaitingValues,
        AwaitingPathChoice,
        AwaitingCustomOptions,
        AwaitingSaveConfirm,
        AwaitingRevisionOrDiscard,
        AwaitingOptimizeModify
    }

    // Calculated but not yet written to MySQL. Only "Yes" at the save step persists it.
    public sealed class DesignDraft
    {
        public DesignFlowKind Kind { get; init; } = DesignFlowKind.Design;

        public bool IsCustom { get; init; }

        public DesignSizing Sizing { get; init; } = new();

        public CustomOptions Options { get; init; } = new();

        // Optimization only: the changes, before / after previews and warning deltas.
        public OptimizationOutcome? Outcome { get; init; }

        // Exact parameters handed to the executor if the user approves the save.
        public string ParametersJson { get; init; } = string.Empty;

        public string Summary { get; init; } = string.Empty;

        public FlowCard? Card { get; init; }
    }

    public sealed class DesignFlowSession
    {
        public int UserId { get; init; }

        public int ProjectId { get; init; }

        public string ProjectLabel { get; init; } = string.Empty;

        public DesignFlowKind Kind { get; init; } = DesignFlowKind.Design;

        public DesignFlowStep Step { get; set; } = DesignFlowStep.AwaitingValues;

        public ParsedQuantity? Flow { get; set; }

        public ParsedQuantity? Pressure { get; set; }

        public CustomOptions Options { get; set; } = new();

        // Optimization only: the saved result being optimized and its parameters.
        public int BaseResultId { get; init; }

        public DesignRunParameters? BaseParameters { get; init; }

        // Optimization only: the current optimized parameters, adjusted by "Modify parameters".
        public DesignRunParameters? WorkingParameters { get; set; }

        public DesignDraft? Draft { get; set; }

        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        public SemaphoreSlim Lock { get; } = new(1, 1);
    }

    public sealed class DesignFlowStore
    {
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<int, DesignFlowSession> _sessions = new();

        public DesignFlowSession? Get(int userId)
        {
            if (!_sessions.TryGetValue(userId, out var session))
                return null;

            if (DateTime.UtcNow - session.LastTouchedUtc > IdleTimeout)
            {
                _sessions.TryRemove(userId, out _);
                return null;
            }

            return session;
        }

        public DesignFlowSession Start(int userId, int projectId, string projectLabel)
        {
            var session = new DesignFlowSession
            {
                UserId = userId,
                ProjectId = projectId,
                ProjectLabel = projectLabel,
                Kind = DesignFlowKind.Design
            };

            _sessions[userId] = session;
            return session;
        }

        public DesignFlowSession StartOptimize(
            int userId,
            int projectId,
            string projectLabel,
            int baseResultId,
            DesignRunParameters baseParameters)
        {
            var session = new DesignFlowSession
            {
                UserId = userId,
                ProjectId = projectId,
                ProjectLabel = projectLabel,
                Kind = DesignFlowKind.Optimize,
                Step = DesignFlowStep.AwaitingSaveConfirm,
                BaseResultId = baseResultId,
                BaseParameters = baseParameters
            };

            _sessions[userId] = session;
            return session;
        }

        public void End(int userId)
        {
            _sessions.TryRemove(userId, out _);
        }
    }
}