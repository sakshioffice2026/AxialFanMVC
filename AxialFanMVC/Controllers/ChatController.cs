using System.Security.Claims;
using AxialFanMVC.Repositories;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using AxialFanMVC.Services.AeroAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFan.Web.Controllers
{
    // ─────────────────────────────────────────────────────────────────────────
    // ChatController
    //
    // Backs the floating handbook assistant widget (site-wide, in _Layout).
    // AeroAi optimize flow ("Optimize design #104" -> diagnostics -> Yes/No save)
    // is handled first; anything else falls through to the handbook assistant.
    //
    // Route summary
    // ─────────────────────────────────────────────────────────────────────────
    //  POST /Chat/Ask   { message: string }
    //       →  { reply: string, awaitingConfirmation: bool, savedResultId: int? }
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [ApiController]
    [Route("Chat")]
    public class ChatController : ControllerBase
    {
        private readonly IOllamaChatRepository _chatService;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
        private readonly DesignFlowService _optimizeFlow;

        public ChatController(
            IOllamaChatRepository chatService,
            IExceptionHandlerRepository exceptionHandlerRepository,
            DesignFlowService optimizeFlow)
        {
            _chatService = chatService;
            _exceptionHandlerRepository = exceptionHandlerRepository;
            _optimizeFlow = optimizeFlow;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public class AskRequest
        {
            public string Message { get; set; } = "";
        }

        [HttpPost("Ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            try
            {
                var flow = await _optimizeFlow.HandleAsync(
                    CurrentUserId,
                    request.Message?.Trim() ?? "",
                    HttpContext.RequestAborted);

                if (flow.Handled)
                {
                    return Ok(new
                    {
                        reply = flow.Message,
                        awaitingConfirmation = flow.FlowActive,
                        savedResultId = (int?)null
                    });
                }

                var reply = await _chatService.AskAsync(request.Message ?? "");
                return Ok(new { reply, awaitingConfirmation = false, savedResultId = (int?)null });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ChatController.Ask] " + ex);
                return Ok(new { reply = "Error: " + ex.Message, awaitingConfirmation = false, savedResultId = (int?)null });
            }
        }
    }
}