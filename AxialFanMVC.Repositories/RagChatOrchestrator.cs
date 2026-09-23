using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Repositories
{
    // Replaces OllamaChatRepository: retrieval now runs against Qdrant
    // (semantic similarity over unstructured handbook text) instead of the
    // MySQL full-text/in-memory-cosine fallback, generation runs through
    // Semantic Kernel's IChatCompletionService backed by LLamaSharp instead
    // of an HTTP call to a local Ollama daemon. MySQL (via DesignResult,
    // passed in by the caller) remains the source of exact, structured
    // per-design values.
    public class RagChatOrchestrator : IRagChatOrchestrator
    {
        private const int MaxChunks = 3;
        private const int MaxCharsPerChunk = 600;

        private readonly IKernelFactory _kernelFactory;
        private readonly ILlamaSharpEmbeddingService _embeddingService;
        private readonly IQdrantHandbookVectorService _vectorService;

        public RagChatOrchestrator(
            IKernelFactory kernelFactory,
            ILlamaSharpEmbeddingService embeddingService,
            IQdrantHandbookVectorService vectorService)
        {
            _kernelFactory = kernelFactory;
            _embeddingService = embeddingService;
            _vectorService = vectorService;
        }

        public async Task<string> AskAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a question.";

            var contextBuilder = await BuildHandbookContextAsync(userMessage);

            var systemPrompt =
                "You are a helpful assistant for an axial fan design tool. " +
                "Prefer the handbook excerpts below when they're relevant — cite chapter/page when you use them. " +
                "If the excerpts don't fully cover the question, use your own general engineering knowledge to fill " +
                "the gaps, but make it clear which parts come from the handbook and which are general knowledge. " +
                "Be concise.\n\n" +
                "HANDBOOK EXCERPTS:\n" + contextBuilder;

            return await CompleteAsync(systemPrompt, userMessage);
        }

        public async Task<string> AskAboutDesignAsync(string userMessage, DesignResult result)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a question.";

            var contextBuilder = await BuildHandbookContextAsync(userMessage);

            var systemPrompt =
                "You are a helpful assistant for an axial fan design tool. " +
                "You are answering a question about ONE SPECIFIC design, whose computed " +
                "values are given below in THIS DESIGN'S RESULTS. Treat every value in that " +
                "block as fixed fact — you may restate, compare, or explain them, but you must " +
                "NEVER calculate, estimate, or infer a new number (a percentage, a margin, a ratio, " +
                "a safety factor, etc.) that isn't already given there. If answering the question " +
                "properly would require a calculation that isn't already provided, say so explicitly " +
                "instead of estimating one.\n\n" +
                "Prefer the handbook excerpts below when relevant — cite chapter/page when you use them. " +
                "If the excerpts don't fully cover the question, use your own general engineering " +
                "knowledge to fill the gaps, but make clear which parts come from the handbook and " +
                "which are general knowledge. Be concise.\n\n" +
                "THIS DESIGN'S RESULTS:\n" + BuildDesignContext(result) + "\n\n" +
                "HANDBOOK EXCERPTS:\n" + contextBuilder;

            return await CompleteAsync(systemPrompt, userMessage);
        }

        // Qdrant retrieval: embed the query locally (LLamaSharp), then do a
        // cosine-similarity vector search — replaces both the MySQL
        // MATCH...AGAINST natural-language path and the in-memory cosine
        // fallback that HandbookChunkRepository used previously.
        private async Task<string> BuildHandbookContextAsync(string query)
        {
            var queryVector = await _embeddingService.GenerateEmbeddingAsync(query);
            var matches = await _vectorService.SearchAsync(queryVector, MaxChunks);

            var sb = new StringBuilder();
            foreach (var chunk in matches)
            {
                var text = chunk.Text.Length > MaxCharsPerChunk
                    ? chunk.Text.Substring(0, MaxCharsPerChunk) + "..."
                    : chunk.Text;

                sb.AppendLine($"[Chapter {chunk.Chapter}: {chunk.ChapterTitle}, p.{chunk.Page}]");
                sb.AppendLine(text);
                sb.AppendLine("---");
            }

            return sb.ToString();
        }

        // Same curated, rounded-to-display-precision snapshot as before —
        // deliberately not a raw dump of the DesignResult/DesignInput objects.
        private static string BuildDesignContext(DesignResult result)
        {
            var di = result.DesignInput;
            var sb = new StringBuilder();

            sb.AppendLine($"Project: {di.Project?.Name ?? "—"}");
            sb.AppendLine($"Flow rate: {di.FlowRateM3s:F3} m³/s");
            sb.AppendLine($"Total pressure: {di.TotalPressurePa:F0} Pa");
            sb.AppendLine($"Speed: {di.SpeedRpm} RPM");
            sb.AppendLine($"Blade count: {di.BladeCount}");
            sb.AppendLine($"Tip diameter: {di.TipDiameterMm:F0} mm");
            sb.AppendLine($"Blade angle: {di.BladeAngleDeg:F1}°");
            sb.AppendLine($"Blade profile: {di.BladeProfile?.Name ?? "—"}");
            sb.AppendLine();
            sb.AppendLine($"Specific speed: {result.SpecificSpeed:F4}");
            sb.AppendLine($"Tip speed: {result.TipSpeedMs:F2} m/s");
            sb.AppendLine($"Shaft power: {result.ShaftPowerKw:F2} kW");
            sb.AppendLine($"Overall efficiency: {result.OverallEfficiencyPct:F1}%");
            sb.AppendLine($"Flow coefficient: {result.FlowCoefficient:F3}");
            sb.AppendLine($"Pressure coefficient: {result.PressureCoefficient:F3}");
            sb.AppendLine($"Blade stress: {result.BladeStressMpa:F1} MPa");
            sb.AppendLine($"Safety factor: {result.SafetyFactor:F2}");

            if (result.OverallNoiseDbA.HasValue)
                sb.AppendLine($"Overall noise: {result.OverallNoiseDbA.Value:F1} dBA");
            if (result.NoiseRating != null)
                sb.AppendLine($"Noise rating: {result.NoiseRating}");

            sb.AppendLine($"Status: {result.Status}");

            if (!string.IsNullOrEmpty(result.WarningMessages))
            {
                var warnings = JsonSerializer.Deserialize<List<string>>(result.WarningMessages) ?? new();
                if (warnings.Count > 0)
                {
                    sb.AppendLine("Warnings:");
                    foreach (var w in warnings)
                        sb.AppendLine($"- {w}");
                }
            }

            return sb.ToString();
        }

        // Generation: routed through the Kernel's IChatCompletionService
        // (LlamaSharpKernelChatCompletionService), which itself serializes
        // access to the native llama.cpp context via SemaphoreSlim.
        private async Task<string> CompleteAsync(string systemPrompt, string userMessage)
        {
            var kernel = _kernelFactory.CreateKernel();
            var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(systemPrompt);
            history.AddUserMessage(userMessage);

            var response = await chatCompletion.GetChatMessageContentsAsync(history, kernel: kernel);
            return response.FirstOrDefault()?.Content ?? "No response generated.";
        }
    }
}