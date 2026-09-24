using AxialFanMVC.Database;
 
namespace AxialFanMVC.Repositories.Inteface;

public interface IRagChatOrchestrator
{
    Task<string> AskAsync(string userMessage);

    Task<string> AskAboutDesignAsync(
        string userMessage,
        DesignResult result);
}