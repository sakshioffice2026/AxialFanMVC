namespace AxialFanMVC.Repositories.Models
{
    public static class AgentActionTypes
    {
        public const string TriggerCfdRun = "TriggerCfdRun";
        public const string TriggerOptimization = "TriggerOptimization";
        public const string CreateNewDesign = "CreateNewDesign";
    }

    public static class AgentActionStatuses
    {
        public const string Pending = "Pending";
        public const string Confirmed = "Confirmed";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
    }

    public class AgentPendingAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public int UserId { get; set; }

        public string ActionType { get; set; } = string.Empty;

        public string ParametersJson { get; set; } = "{}";

        public string Summary { get; set; } = string.Empty;

        public string Status { get; set; } = AgentActionStatuses.Pending;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(10);
    }

    public class AgentActionExecutionResult
    {
        public bool Success { get; set; }

        public string ActionType { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public int? JobId { get; set; }

        public int? ResultId { get; set; }

        public int? DesignInputId { get; set; }
    }
}