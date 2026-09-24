using System.Security.Claims;
using AxialFanMVC.Repositories.Inteface;
using AxialFanMVC.Services.AeroAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    [Route("AeroAi/Flow")]
    public class AeroAiFlowController : Controller
    {
        private const int MaxMessageLength = 2000;

        private readonly DesignFlowService _flow;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public AeroAiFlowController(
            DesignFlowService flow,
            IExceptionHandlerRepository exceptionHandlerRepository)
        {
            _flow = flow;
            _exceptionHandlerRepository = exceptionHandlerRepository;
        }

        // GET /AeroAi/Flow/State
        [HttpGet("State")]
        public IActionResult State()
        {
            return Json(new { active = _flow.IsActive(CurrentUserId) });
        }

        // POST /AeroAi/Flow/Reply
        [HttpPost("Reply"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply([FromBody] AeroAiFlowReplyRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "Message is required." });

            if (request.Message.Length > MaxMessageLength)
                return BadRequest(new { error = $"Message must be {MaxMessageLength} characters or fewer." });

            try
            {
                var reply = await _flow.HandleAsync(
                    CurrentUserId,
                    request.Message.Trim(),
                    HttpContext.RequestAborted);

                return Json(reply);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new { error = "Request cancelled." });
            }
            catch (Exception ex)
            {
                LogSafe(nameof(Reply), ex);

                return Json(FlowReply.Say(
                    "Sorry, I hit a problem with that step. Please try again.",
                    _flow.IsActive(CurrentUserId)));
            }
        }

        // POST /AeroAi/Flow/Cancel
        [HttpPost("Cancel"), ValidateAntiForgeryToken]
        public IActionResult Cancel()
        {
            _flow.Cancel(CurrentUserId);
            return Json(new { success = true });
        }

        private void LogSafe(string method, Exception ex)
        {
            try
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AeroAiFlowController),
                    method,
                    ex.ToString(),
                    CurrentUserId);
            }
            catch
            {
                Console.Error.WriteLine($"[AeroAiFlowController.{method}] {ex}");
            }
        }
    }

    public sealed class AeroAiFlowReplyRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}