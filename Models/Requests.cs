namespace HealthcareClaimsManager.Models
{
    public class LockRequest
    {
        public int TaskId { get; set; }
    }

    public class ReleaseRequest
    {
        public int TaskId { get; set; }
    }

    public class ForwardRequest
    {
        public int TaskId { get; set; }
        public string TargetUsername { get; set; } = string.Empty;
    }

    public class CompleteRequest
    {
        public int TaskId { get; set; }
        public string Outcome { get; set; } = string.Empty; // 'Approve', 'PartialDenial', 'Deny', 'Pend'
        public string? Note { get; set; }
    }
}