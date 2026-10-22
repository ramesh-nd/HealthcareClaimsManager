using Microsoft.AspNetCore.Mvc;
using HealthcareClaimsManager.Data;
using HealthcareClaimsManager.Models;

namespace HealthcareClaimsManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QueueController : ControllerBase
    {
        private readonly ReviewTaskRepository _repository;

        public QueueController(ReviewTaskRepository repository)
        {
            _repository = repository;
        }

        private string GetHeaderUserId()
        {
            if (Request.Headers.TryGetValue("X-User-Id", out var userId) && !string.IsNullOrEmpty(userId))
            {
                return userId.ToString();
            }
            return "ereyes"; // Default reviewer fallback
        }

        [HttpGet("get-next")]
        public IActionResult GetNextClaim([FromQuery] string queueCode)
        {
            var userId = GetHeaderUserId();
            var task = _repository.GetNextClaimTask(queueCode, userId);
            if (task == null)
            {
                return NotFound(new { message = "No available claims in this queue." });
            }
            return Ok(task);
        }

        [HttpPost("lock")]
        public IActionResult LockClaim([FromBody] LockRequest req)
        {
            var userId = GetHeaderUserId();
            bool success = _repository.AcquireLock(req.TaskId, userId, out string error);
            if (!success)
            {
                return Conflict(new { message = error });
            }
            return Ok(new { message = "Claim locked successfully." });
        }

        [HttpPost("release")]
        public IActionResult ReleaseClaim([FromBody] ReleaseRequest req)
        {
            var userId = GetHeaderUserId();
            bool success = _repository.ReleaseLock(req.TaskId, userId);
            if (!success)
            {
                return BadRequest(new { message = "Unable to release lock. Task not locked by user or already released." });
            }
            return Ok(new { message = "Claim lock released." });
        }

        [HttpPost("forward")]
        public IActionResult ForwardClaim([FromBody] ForwardRequest req)
        {
            var userId = GetHeaderUserId();
            bool success = _repository.ForwardTask(req.TaskId, userId, req.TargetUsername);
            if (!success)
            {
                return BadRequest(new { message = "Failed to forward claim task." });
            }
            return Ok(new { message = $"Claim task forwarded to {req.TargetUsername}." });
        }

        [HttpPost("complete")]
        public IActionResult CompleteReview([FromBody] CompleteRequest req)
        {
            var userId = GetHeaderUserId();
            bool success = _repository.CompleteReview(req.TaskId, userId, req.Outcome, req.Note);
            if (!success)
            {
                return BadRequest(new { message = "Failed to complete review task." });
            }
            return Ok(new { message = "Review completed successfully." });
        }
    }
}