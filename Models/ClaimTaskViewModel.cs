using System;

namespace HealthcareClaimsManager.Models
{
    public class ClaimTaskViewModel
    {
        public int TaskId { get; set; }
        public string ClaimNumber { get; set; } = string.Empty;
        public string QueueCode { get; set; } = string.Empty;
        public string QueueName { get; set; } = string.Empty;
        public byte Priority { get; set; }
        public DateTime DueDate { get; set; }
        public string? AssignedTo { get; set; }
        public string? AssignedBy { get; set; }
        public string? LockedBy { get; set; }
        public DateTime? LockedOn { get; set; }
        public DateTime? LockExpiresOn { get; set; }
        public string? Note { get; set; }
    }

    public class QueueItem
    {
        public int QueueId { get; set; }
        public string QueueCode { get; set; } = string.Empty;
        public string QueueName { get; set; } = string.Empty;
    }

    public class AppUserDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class QueueViewModel
    {
        public string SelectedQueue { get; set; } = "DUP";
        public List<QueueItem> Queues { get; set; } = new();
        public List<AppUserDto> Reviewers { get; set; } = new();
        public ClaimTaskViewModel? CurrentTask { get; set; }
        public string CurrentUserId { get; set; } = "ereyes";
    }
}