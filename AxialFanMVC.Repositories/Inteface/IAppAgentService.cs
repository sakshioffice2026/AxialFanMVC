namespace AxialFanMVC.Repositories.Inteface;

public interface IAppAgentService
{
    Task<AppAgentResponse> AskAsync(
        string userMessage,
        AppAgentContext? context = null,
        CancellationToken cancellationToken = default);
}

public sealed class AppAgentContext
{
    public string? Controller { get; init; }

    public string? Action { get; init; }

    public int? Id { get; init; }

    public int? ProjectId { get; init; }

    public int? ResultId { get; init; }

    public int? UserId { get; init; }
}

public sealed class AppAgentPendingAction
{
    public string Id { get; init; } = string.Empty;

    public string ActionType { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}

public sealed class AppAgentResponse
{
    public string Reply { get; init; } = string.Empty;

    public IReadOnlyList<AppAgentPendingAction> PendingActions { get; init; }
        = Array.Empty<AppAgentPendingAction>();
}