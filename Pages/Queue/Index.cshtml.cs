using Microsoft.AspNetCore.Mvc.RazorPages;
using HealthcareClaimsManager.Data;
using HealthcareClaimsManager.Models;

namespace HealthcareClaimsManager.Pages.Queue
{
    public class IndexModel : PageModel
    {
        private readonly ReviewTaskRepository _repository;

        public IndexModel(ReviewTaskRepository repository)
        {
            _repository = repository;
        }

        public List<QueueItem> Queues { get; set; } = new();
        public List<AppUserDto> Reviewers { get; set; } = new();

        public void OnGet()
        {
            Queues = _repository.GetQueues();
            Reviewers = _repository.GetReviewers();
        }
    }
}