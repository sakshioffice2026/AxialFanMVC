using System.Collections.Concurrent;

namespace AxialFanMVC.Services.AeroAi
{
    public enum DesignFlowStep
    {
        AwaitingStart,
        AwaitingFlow,
        AwaitingPressure,
        AwaitingRunConfirm
    }

    public sealed class DesignFlowSession
    {
        public int UserId { get; init; }

        public int ProjectId { get; init; }

        public string ProjectLabel { get; init; } = string.Empty;

        public DesignFlowStep Step { get; set; } = DesignFlowStep.AwaitingStart;

        public ParsedQuantity? Flow { get; set; }

        public ParsedQuantity? Pressure { get; set; }

        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        public SemaphoreSlim Lock { get; } = new(1, 1);
    }

    public sealed class DesignFlowStore
    {
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<int, DesignFlowSession> _sessions = new();
        private readonly ConcurrentDictionary<string, byte> _offered = new();

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

        public bool MarkOffered(int userId, int projectId)
        {
            return _offered.TryAdd(userId + ":" + projectId, 0);
        }
    }
}