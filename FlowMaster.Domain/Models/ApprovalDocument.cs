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

    /// <summary>
    /// ApprovalSystem 동기화 상태
    /// </summary>
    public enum SyncStatus
    {
        Synced = 0,     // 동기화 완료 (ApprovalId 있음)
        Pending = 1,    // 동기화 대기 (ApprovalSystem 연결 실패, 재시도 예정)
        Failed = 2      // 동기화 실패 (재시도 횟수 초과)
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
        public string ApprovalId { get; set; }        // ApprovalService API의 결재 ID (APV-xxx)

        // ApprovalSystem 동기화 추적
        public SyncStatus SyncStatus { get; set; } = SyncStatus.Synced;
        public int SyncRetryCount { get; set; } = 0;
        public string SyncError { get; set; }

        // 외부 DB 연동 필드
        public string IssueKey { get; set; }        // Jira 이슈 키
        public string TableType { get; set; }       // BA1 / BA2
        public string GenType { get; set; }         // 발전기 유형
        public string InjType { get; set; }         // 인젝터 유형
        public string Description { get; set; }     // 설명 (BA2만 사용)
        public string Participants { get; set; }    // 참여자 목록
        public string Version { get; set; }          // 버전 (예: 20260123_v03)
        public string OutputPath { get; set; }      // 산출물 경로
        public string ApproverComment { get; set; } // 결재자 코멘트
        public DateTime? ApprovalTime { get; set; } // 결재/반려 처리 일시

        // Navigation Properties
        public List<ApprovalLine> ApprovalLines { get; set; } = new List<ApprovalLine>();
        public List<TestResult> TestResults { get; set; } = new List<TestResult>();
        public List<ChecklistItem> ChecklistItems { get; set; } = new List<ChecklistItem>();
    }
}
