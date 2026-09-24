using AxialFanMVC.Repositories.Models;

namespace AxialFanMVC.Repositories.Inteface;

public interface IAgentPendingActionStore
{
    AgentPendingAction Stage(
        int userId,
        string actionType,
        string parametersJson,
        string summary);

    AgentPendingAction? Get(string actionId, int userId);

    IReadOnlyList<AgentPendingAction> ListPending(int userId);

    AgentPendingAction? TryConfirm(string actionId, int userId);

    bool Cancel(string actionId, int userId);
}