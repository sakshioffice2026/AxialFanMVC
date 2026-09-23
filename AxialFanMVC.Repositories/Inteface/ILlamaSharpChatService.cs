namespace AxialFanMVC.Repositories.Inteface
{
    public interface ILlamaSharpChatService
    {
        Task<string> CompleteAsync(string systemPrompt, string userMessage, int maxTokens = 300);
    }
}