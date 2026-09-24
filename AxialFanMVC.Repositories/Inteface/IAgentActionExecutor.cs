using AxialFanMVC.Repositories.Models;

namespace AxialFanMVC.Repositories.Inteface;

public interface IAgentActionExecutor
{
    Task<AgentActionExecutionResult> ConfirmAndExecuteAsync(
        string actionId,
        int userId);

    bool Cancel(
        string actionId,
        int userId);
}