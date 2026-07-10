using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;

using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace AxialFanMVC.Repositories
{
    public class OllamaChatRepository : IOllamaChatRepository
    {
        private readonly HttpClient _http;
        private readonly IHandbookChunkRepository _handbookRepo;
        private readonly string _model;

        public OllamaChatRepository(HttpClient http, IHandbookChunkRepository handbookRepo, IConfiguration config)
        {
            _http = http;
            _handbookRepo = handbookRepo;

            // Base URL is set on the HttpClient via Program.cs (see AddHttpClient config).
            _model = config["Ollama:Model"] ?? "llama3.1";
        }

        public async Task<string> AskAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a question.";

            // ── Retrieve relevant handbook context (reuses existing full-text search) ──
            // Fewer chunks + truncated text = smaller prompt = much faster inference on CPU.
            var chunks = await _handbookRepo.SearchAsync(userMessage, maxResults: 3);

            const int maxCharsPerChunk = 600;
            var contextBuilder = new StringBuilder();
            foreach (var c in chunks)
            {
                var text = c.Text.Length > maxCharsPerChunk
                    ? c.Text.Substring(0, maxCharsPerChunk) + "..."
                    : c.Text;

                contextBuilder.AppendLine($"[Chapter {c.Chapter}: {c.ChapterTitle}, p.{c.Page}]");
                contextBuilder.AppendLine(text);
                contextBuilder.AppendLine("---");
            }

            var systemPrompt =
                "You are a helpful assistant for an axial fan design tool. " +
                "Prefer the handbook excerpts below when they're relevant — cite chapter/page when you use them. " +
                "If the excerpts don't fully cover the question, use your own general engineering knowledge to fill " +
                "the gaps, but make it clear which parts come from the handbook and which are general knowledge. " +
                "Be concise.\n\n" +
                "HANDBOOK EXCERPTS:\n" + contextBuilder;

            return await CallOllamaAsync(systemPrompt, userMessage);
        }

        /// <summary>
        /// Same RAG pipeline as AskAsync, but with the design's own computed
        /// values injected as a second, separate context block alongside the
        /// retrieved handbook excerpts. The system prompt is instructed to
        /// treat the design block as fixed fact — restate/explain it, never
        /// compute a new number from it.
        /// </summary>
        public async Task<string> AskAboutDesignAsync(string userMessage, DesignResult result)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return "Please type a question.";

            // Retrieval is still driven by the user's question text only —
            // the design values below are injected as context, not used to
            // bias which handbook chunks come back.
            var chunks = await _handbookRepo.SearchAsync(userMessage, maxResults: 3);

            const int maxCharsPerChunk = 600;
            var contextBuilder = new StringBuilder();
            foreach (var c in chunks)
            {
                var text = c.Text.Length > maxCharsPerChunk
                    ? c.Text.Substring(0, maxCharsPerChunk) + "..."
                    : c.Text;

                contextBuilder.AppendLine($"[Chapter {c.Chapter}: {c.ChapterTitle}, p.{c.Page}]");
                contextBuilder.AppendLine(text);
                contextBuilder.AppendLine("---");
            }

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

            return await CallOllamaAsync(systemPrompt, userMessage);
        }

        // Curated, rounded-to-display-precision snapshot of the design —
        // deliberately not a raw dump of the DesignResult/DesignInput objects.
        // Keeping this to the same fields and rounding the Results page
        // already shows means nothing the model repeats back implies more
        // precision than the user sees elsewhere, and keeps the prompt small.
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

        // Shared HTTP call to Ollama — both AskAsync and AskAboutDesignAsync
        // build a system prompt differently but talk to the model identically,
        // so the request/response plumbing lives in exactly one place.
        private async Task<string> CallOllamaAsync(string systemPrompt, string userMessage)
        {
            var requestBody = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                stream = false,
                options = new
                {
                    num_predict = 300 // caps response length so generation doesn't run long
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync("/api/chat", content);
            }
            catch (HttpRequestException)
            {
                return "Couldn't reach Ollama. Make sure it's running locally (`ollama serve`) and the model is pulled (`ollama pull " + _model + "`).";
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return $"Ollama returned an error: {response.StatusCode}. {errorBody}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var reply = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return reply ?? "No response generated.";
        }
    }
}