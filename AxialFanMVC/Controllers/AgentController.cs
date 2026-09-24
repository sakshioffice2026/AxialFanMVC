using System.Security.Claims;
using AxialFanMVC.Repositories.Inteface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxialFanMVC.Controllers
{
    [Authorize]
    public class AgentController : Controller
    {
        private const int MaxMessageLength = 2000;

        private readonly IAppAgentService _agentService;
        private readonly IAgentActionExecutor _actionExecutor;
        private readonly IAgentPendingActionStore _actionStore;
        private readonly IExceptionHandlerRepository _exceptionHandlerRepository;
        private readonly IWebHostEnvironment _env;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public AgentController(
            IAppAgentService agentService,
            IAgentActionExecutor actionExecutor,
            IAgentPendingActionStore actionStore,
            IExceptionHandlerRepository exceptionHandlerRepository,
            IWebHostEnvironment env)
        {
            _agentService = agentService;
            _actionExecutor = actionExecutor;
            _actionStore = actionStore;
            _exceptionHandlerRepository = exceptionHandlerRepository;
            _env = env;
        }

        private void LogSafe(string method, Exception ex)
        {
            try
            {
                _exceptionHandlerRepository.SaveException(
                    nameof(AgentController),
                    method,
                    ex.ToString(),
                    CurrentUserId);
            }
            catch
            {
                Console.Error.WriteLine($"[AgentController.{method}] {ex}");
            }
        }

        // POST /Agent/Ask
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask([FromBody] AgentAskRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "Message is required." });

            if (request.Message.Length > MaxMessageLength)
                return BadRequest(new { error = $"Message must be {MaxMessageLength} characters or fewer." });

            try
            {
                var context = new AppAgentContext
                {
                    Controller = request.Controller,
                    Action = request.Action,
                    Id = request.Id,
                    ProjectId = request.ProjectId,
                    ResultId = request.ResultId,
                    UserId = CurrentUserId
                };

                var response = await _agentService.AskAsync(
                    request.Message.Trim(),
                    context,
                    HttpContext.RequestAborted);

                return Json(new
                {
                    reply = response.Reply,
                    pendingActions = response.PendingActions.Select(ToDto)
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new { error = "Request cancelled." });
            }
            catch (Exception ex)
            {
                LogSafe(nameof(Ask), ex);

                var detail = _env.IsDevelopment()
                    ? $" [{ex.GetType().Name}: {ex.Message}]"
                    : string.Empty;

                return StatusCode(500, new { error = "The assistant could not answer. Please try again." + detail });
            }
        }

        // GET /Agent/Pending - lets the widget restore pending confirmations after a page navigation
        [HttpGet]
        public IActionResult Pending()
        {
            var pending = _actionStore.ListPending(CurrentUserId)
                .Select(a => new
                {
                    id = a.Id,
                    actionType = a.ActionType,
                    summary = a.Summary,
                    expiresAtUtc = a.ExpiresAtUtc
                });

            return Json(new { pendingActions = pending });
        }

        // POST /Agent/ConfirmAction
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmAction([FromBody] AgentActionRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ActionId))
                return BadRequest(new { error = "ActionId is required." });

            var result = await _actionExecutor.ConfirmAndExecuteAsync(request.ActionId, CurrentUserId);

            string? redirectUrl = null;

            if (result.Success && result.ResultId.HasValue && result.DesignInputId.HasValue)
                redirectUrl = Url.Action("Result", "Results", new { resultId = result.ResultId.Value });

            return Json(new
            {
                success = result.Success,
                actionType = result.ActionType,
                message = result.Message,
                jobId = result.JobId,
                resultId = result.ResultId,
                designInputId = result.DesignInputId,
                redirectUrl
            });
        }

        // POST /Agent/CancelAction
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult CancelAction([FromBody] AgentActionRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ActionId))
                return BadRequest(new { error = "ActionId is required." });

            var cancelled = _actionExecutor.Cancel(request.ActionId, CurrentUserId);

            return Json(new
            {
                success = cancelled,
                message = cancelled ? "Action cancelled." : "Action not found, already handled, or expired."
            });
        }

        private static object ToDto(AppAgentPendingAction a) => new
        {
            id = a.Id,
            actionType = a.ActionType,
            summary = a.Summary,
            expiresAtUtc = a.ExpiresAtUtc
        };
    }

    public sealed class AgentAskRequest
    {
        public string Message { get; set; } = string.Empty;

        public string? Controller { get; set; }

        public string? Action { get; set; }

        public int? Id { get; set; }

        public int? ProjectId { get; set; }

        public int? ResultId { get; set; }
    }

    public sealed class AgentActionRequest
    {
        public string ActionId { get; set; } = string.Empty;
    }
}