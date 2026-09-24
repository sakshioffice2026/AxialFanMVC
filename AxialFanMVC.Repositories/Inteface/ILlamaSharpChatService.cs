namespace AxialFanMVC.Repositories.Inteface;

public interface ILlamaSharpChatService
{
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens = 300);
}