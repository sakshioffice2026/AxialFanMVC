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