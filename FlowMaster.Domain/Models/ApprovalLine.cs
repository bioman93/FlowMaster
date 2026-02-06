using System;

namespace FlowMaster.Domain.Models
{
    public enum ApprovalStepStatus
    {
        Waiting,    // 대기
        Approved,   // 승인
        Rejected    // 반려
    }

    public class ApprovalLine
    {
        public int LineId { get; set; }
        public int DocId { get; set; }
        public string ApproverId { get; set; }
        public string ApproverName { get; set; }
        public int Sequence { get; set; } // 1, 2, 3...
        public ApprovalStepStatus Status { get; set; }
        public DateTime? ActionDate { get; set; }
        public string Comment { get; set; }
    }
}
