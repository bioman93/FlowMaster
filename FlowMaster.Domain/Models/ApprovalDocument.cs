using System;
using System.Collections.Generic;

namespace FlowMaster.Domain.Models
{
    public enum ApprovalStatus
    {
        TempSaved,      // 임시저장
        Pending,        // 결재 대기
        Approved,       // 승인
        Rejected,       // 반려
        Canceled        // 취소됨
    }

    public class ApprovalDocument
    {
        public int DocId { get; set; }
        public string Title { get; set; }
        public string WriterId { get; set; }
        public string WriterName { get; set; } // 편의상 이름 포함
        public DateTime CreateDate { get; set; }
        public DateTime? UpdateDate { get; set; }
        public ApprovalStatus Status { get; set; }
        public string CurrentApproverId { get; set; } // 현재 결재 순서인 사람

        // Navigation Properties (No ORM dependencies, just logical)
        public List<ApprovalLine> ApprovalLines { get; set; } = new List<ApprovalLine>();
        public List<TestResult> TestResults { get; set; } = new List<TestResult>();
    }
}
