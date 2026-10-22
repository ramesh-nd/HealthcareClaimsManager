using Microsoft.AspNetCore.Mvc;
using HealthcareClaimsManager.Data;

namespace HealthcareClaimsManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SupervisorController : ControllerBase
    {
        private readonly ReviewTaskRepository _repository;

        public SupervisorController(ReviewTaskRepository repository)
        {
            _repository = repository;
        }

        [HttpGet("queue-tasks")]
        public IActionResult GetQueueTasks([FromQuery] string? queueCode, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var (tasks, totalCount) = _repository.GetSupervisorQueueTasks(queueCode, page, pageSize);
            return Ok(new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                Data = tasks
            });
        }
    }
}