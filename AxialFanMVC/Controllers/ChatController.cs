using AxialFanMVC.Repositories;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFan.Web.Controllers
{
    // ─────────────────────────────────────────────────────────────────────────
    // ChatController
    //
    // Backs the floating handbook assistant widget (site-wide, in _Layout).
    //
    // Route summary
    // ─────────────────────────────────────────────────────────────────────────
    //  POST /Chat/Ask   { message: string }   →  { reply: string }
    // ─────────────────────────────────────────────────────────────────────────
    [Authorize]
    [ApiController]
    [Route("Chat")]
    public class ChatController : ControllerBase
    {
        private readonly IOllamaChatRepository _chatService;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;   

        public ChatController(IOllamaChatRepository chatService, IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _chatService = chatService;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        public class AskRequest
        {
            public string Message { get; set; } = "";
        }

        [HttpPost("Ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            try
            {
                var reply = await _chatService.AskAsync(request.Message);
                return Ok(new { reply });
            }
            catch (Exception ex)
            {
                // Surface the real error instead of a silent 500 — check console/logs too.
                Console.WriteLine("[ChatController.Ask] " + ex);
                return Ok(new { reply = "Error: " + ex.Message });
            }
        }
    }
}