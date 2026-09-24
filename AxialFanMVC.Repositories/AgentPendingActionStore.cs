using System.Collections.Concurrent;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Repositories.Models;

namespace AxialFanMVC.Repositories;

public sealed class AgentPendingActionStore : IAgentPendingActionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const int MaxPendingPerUser = 20;

    private readonly ConcurrentDictionary<string, AgentPendingAction> _actions = new();
    private readonly object _gate = new();

    public AgentPendingAction Stage(
        int userId,
        string actionType,
        string parametersJson,
        string summary)
    {
        lock (_gate)
        {
            PurgeExpired();

            var userPending = _actions.Values
                .Where(a => a.UserId == userId && a.Status == AgentActionStatuses.Pending)
                .OrderBy(a => a.CreatedAtUtc)
                .ToList();

            while (userPending.Count >= MaxPendingPerUser)
            {
                _actions.TryRemove(userPending[0].Id, out _);
                userPending.RemoveAt(0);
            }

            var now = DateTime.UtcNow;

            var action = new AgentPendingAction
            {
                UserId = userId,
                ActionType = actionType,
                ParametersJson = parametersJson,
                Summary = summary,
                Status = AgentActionStatuses.Pending,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(Lifetime)
            };

            _actions[action.Id] = action;
            return action;
        }
    }

    public AgentPendingAction? Get(string actionId, int userId)
    {
        lock (_gate)
        {
            PurgeExpired();

            return _actions.TryGetValue(actionId, out var action) && action.UserId == userId
                ? action
                : null;
        }
    }

    public IReadOnlyList<AgentPendingAction> ListPending(int userId)
    {
        lock (_gate)
        {
            PurgeExpired();

            return _actions.Values
                .Where(a => a.UserId == userId && a.Status == AgentActionStatuses.Pending)
                .OrderBy(a => a.CreatedAtUtc)
                .ToList();
        }
    }

    public AgentPendingAction? TryConfirm(string actionId, int userId)
    {
        lock (_gate)
        {
            PurgeExpired();

            if (!_actions.TryGetValue(actionId, out var action))
                return null;

            if (action.UserId != userId || action.Status != AgentActionStatuses.Pending)
                return null;

            action.Status = AgentActionStatuses.Confirmed;
            return action;
        }
    }

    public bool Cancel(string actionId, int userId)
    {
        lock (_gate)
        {
            PurgeExpired();

            if (!_actions.TryGetValue(actionId, out var action))
                return false;

            if (action.UserId != userId || action.Status != AgentActionStatuses.Pending)
                return false;

            action.Status = AgentActionStatuses.Cancelled;
            return true;
        }
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;

        foreach (var pair in _actions)
        {
            var action = pair.Value;

            if (action.Status == AgentActionStatuses.Pending && action.ExpiresAtUtc <= now)
                action.Status = AgentActionStatuses.Expired;

            if (action.Status != AgentActionStatuses.Pending
                && now - action.ExpiresAtUtc > TimeSpan.FromMinutes(30))
            {
                _actions.TryRemove(pair.Key, out _);
            }
        }
    }
}