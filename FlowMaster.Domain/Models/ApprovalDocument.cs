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

        // 외부 DB 연동 필드
        public string IssueKey { get; set; }        // Jira 이슈 키
        public string TableType { get; set; }       // BA1 / BA2
        public string GenType { get; set; }         // 발전기 유형
        public string InjType { get; set; }         // 인젝터 유형
        public string Description { get; set; }     // 설명 (BA2만 사용)
        public string Participants { get; set; }    // 참여자 목록
        public string ApproverComment { get; set; } // 결재자 코멘트
        public DateTime? ApprovalTime { get; set; } // 결재 시간

        // Navigation Properties
        public List<ApprovalLine> ApprovalLines { get; set; } = new List<ApprovalLine>();
        public List<TestResult> TestResults { get; set; } = new List<TestResult>();
        public List<ChecklistItem> ChecklistItems { get; set; } = new List<ChecklistItem>();
    }
}
