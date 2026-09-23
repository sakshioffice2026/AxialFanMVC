using LLama;
using LLama.Common;

namespace AxialFanMVC.Repositories
{
    public static class LlamaSanityCheck
    {
        public static async Task RunAsync()
        {
            string chatModelPath = @"D:\Office\Models\Phi-3-mini-4k-instruct-q4.gguf";
            string embedModelPath = @"D:\Office\Models\nomic-embed-text-v1.5.Q4_K_M.gguf";

            Console.WriteLine("=== Chat model test ===");
            var chatParams = new ModelParams(chatModelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 0
            };

            using (var chatModel = LLamaWeights.LoadFromFile(chatParams))
            using (var context = chatModel.CreateContext(chatParams))
            {
                var executor = new InteractiveExecutor(context);
                var history = new ChatHistory();
                history.AddMessage(AuthorRole.System, "You are a concise assistant.");

                var session = new ChatSession(executor, history);
                var inferenceParams = new InferenceParams
                {
                    MaxTokens = 100,
                    AntiPrompts = new List<string> { "User:" }
                };

                Console.Write("Model reply: ");
                await foreach (var text in session.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, "In one sentence, what is an axial fan?"),
                    inferenceParams))
                {
                    Console.Write(text);
                }
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("=== Embedding model test ===");
            var embedParams = new ModelParams(embedModelPath)
            {
                ContextSize = 512,
                GpuLayerCount = 0,
                Embeddings = true
            };

            using (var embedModel = LLamaWeights.LoadFromFile(embedParams))
            {
                var embedder = new LLamaEmbedder(embedModel, embedParams);
                var embeddings = await embedder.GetEmbeddings("axial fan blade pitch angle");
                Console.WriteLine($"Embedding vector length: {embeddings[0].Length}");
                Console.WriteLine($"First 5 values: {string.Join(", ", embeddings[0].Take(5))}");
            }

            Console.WriteLine("=== Sanity test complete ===");
        }
    }
}