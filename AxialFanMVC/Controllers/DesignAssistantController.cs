using System.Security.Claims;
using AxialFanMVC.Database;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AxialFanMVC.Controllers
{
    // ─────────────────────────────────────────────────────────────────────
    // DesignAssistantController
    //
    // Design-grounded AI Q&A — like /Reference search, but scoped to one
    // specific design result instead of a generic search box. Reuses the
    // existing IOllamaChatRepository/RAG stack; the only difference is the
    // request also carries a resultId, so the model is given this design's
    // actual computed values as context (see OllamaChatRepository.AskAboutDesignAsync).
    //
    // Renamed from CopilotController — "Copilot" is a Microsoft trademark
    // (GitHub Copilot / Microsoft Copilot) and shouldn't be used as a
    // feature name in a commercial product. No behavior changed here,
    // naming only.
    //
    // Route summary
    // ─────────────────────────────────────────────────────────────────────
    //  POST /DesignAssistant/Ask   { resultId, message }  →  { reply }
    // ─────────────────────────────────────────────────────────────────────
    [Authorize]
    [ApiController]
    [Route("DesignAssistant")]
    public class DesignAssistantController : ControllerBase
    {
        private readonly AxialFanDbContext _db;
        private readonly IOllamaChatRepository _chatRepo;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

        public DesignAssistantController(AxialFanDbContext db, IOllamaChatRepository chatRepo, IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _db = db;
            _chatRepo = chatRepo;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public class AskRequest
        {
            public int ResultId { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        [HttpPost("Ask")]
        public async Task<IActionResult> Ask([FromBody] AskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { reply = "Please type a question." });

            // Same ownership check pattern as ExportController.LoadResult —
            // a user can only ask about their own designs.
            var result = await _db.design_results
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.Project)
                .Include(r => r.DesignInput)
                    .ThenInclude(di => di.BladeProfile)
                .FirstOrDefaultAsync(r => r.Id == request.ResultId &&
                    r.DesignInput.Project.UserId == CurrentUserId);

            if (result == null)
                return NotFound(new { reply = "Design result not found." });

            var reply = await _chatRepo.AskAboutDesignAsync(request.Message, result);
            return Ok(new { reply });
        }
    }
}