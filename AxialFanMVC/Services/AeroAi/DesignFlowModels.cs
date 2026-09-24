namespace AxialFanMVC.Services.AeroAi
{
    public sealed class FlowMetric
    {
        public string Label { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public string? Level { get; init; }
    }

    public sealed class FlowCallout
    {
        public string Level { get; init; } = "good";

        public string Text { get; init; } = string.Empty;
    }

    public sealed class FlowCard
    {
        public string Title { get; init; } = string.Empty;

        public string Status { get; init; } = "ok";

        // True while the design is only calculated in memory (nothing written to the database).
        public bool IsDraft { get; init; }

        public int ResultId { get; init; }

        public string ResultUrl { get; init; } = string.Empty;

        public List<FlowMetric> Headline { get; init; } = new();

        public List<FlowMetric> Details { get; init; } = new();

        public FlowCallout? Stall { get; init; }

        public List<string> Warnings { get; init; } = new();
    }

    public sealed class FlowReply
    {
        public bool Handled { get; init; }

        public bool FlowActive { get; init; }

        public string? Message { get; init; }

        public IReadOnlyList<string> QuickReplies { get; init; } = Array.Empty<string>();

        public FlowCard? Card { get; init; }

        public string? Resume { get; init; }

        public IReadOnlyList<string> ResumeQuickReplies { get; init; } = Array.Empty<string>();

        public static FlowReply Idle => new() { Handled = false, FlowActive = false };

        public static FlowReply Say(string message, bool flowActive, params string[] quickReplies)
            => new()
            {
                Handled = true,
                FlowActive = flowActive,
                Message = message,
                QuickReplies = quickReplies
            };

        // Final card, flow finished.
        public static FlowReply SayWithCard(string message, FlowCard card)
            => new()
            {
                Handled = true,
                FlowActive = false,
                Message = message,
                Card = card
            };

        // Card shown while the flow continues (for example the unsaved draft awaiting Yes / No).
        public static FlowReply SayWithCard(string message, FlowCard card, bool flowActive, params string[] quickReplies)
            => new()
            {
                Handled = true,
                FlowActive = flowActive,
                Message = message,
                Card = card,
                QuickReplies = quickReplies
            };

        public static FlowReply Pass(string resume, params string[] resumeQuickReplies)
            => new()
            {
                Handled = false,
                FlowActive = true,
                Resume = resume,
                ResumeQuickReplies = resumeQuickReplies
            };
    }
}