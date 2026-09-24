using Google.Protobuf.Reflection;
using System.Collections.Concurrent;

namespace AxialFanMVC.Services.AeroAi
{
    public enum DesignFlowStep
    {
        AwaitingValues,
        AwaitingPathChoice,
        AwaitingCustomOptions,
        AwaitingSaveConfirm,
        AwaitingRevisionOrDiscard
    }

    // Calculated but not yet written to MySQL. Only "Yes" at the save step persists it.
    public sealed class DesignDraft
    {
        public bool IsCustom { get; init; }

        public DesignSizing Sizing { get; init; } = new();

        public CustomOptions Options { get; init; } = new();

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

        public DesignFlowStep Step { get; set; } = DesignFlowStep.AwaitingValues;

        public ParsedQuantity? Flow { get; set; }

        public ParsedQuantity? Pressure { get; set; }

        public CustomOptions Options { get; set; } = new();

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
                ProjectLabel = projectLabel
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