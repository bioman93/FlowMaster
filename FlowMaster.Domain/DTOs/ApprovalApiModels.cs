using System;
using System.Collections.Generic;

namespace FlowMaster.Domain.DTOs
{
    /// <summary>
    /// 결재 생성 요청
    /// POST /api/approvals
    /// </summary>
    public class CreateApprovalRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string RequesterId { get; set; }
        public string RequesterName { get; set; }
        public List<string> ApproverIds { get; set; }
        public string SourceApp { get; set; } = "FlowMaster";
        public string SourceId { get; set; }
        public object Metadata { get; set; }
    }

    /// <summary>
    /// 결재 승인/반려 요청
    /// POST /api/approvals/{id}/decision
    /// </summary>
    public class ApprovalDecisionRequest
    {
        /// <summary>
        /// "approve" 또는 "reject"
        /// </summary>
        public string Decision { get; set; }
        public string ApproverId { get; set; }
        public string Comment { get; set; }
    }

    /// <summary>
    /// 결재 응답 (API 공통)
    /// </summary>
    public class ApprovalResponse
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public string RequesterId { get; set; }
        public string RequesterName { get; set; }
        public List<string> ApproverIds { get; set; }
        public string ApprovedBy { get; set; }
        public string Comment { get; set; }
        public string SourceApp { get; set; }
        public string SourceId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    /// <summary>
    /// 결재 승인/반려 결과 응답
    /// </summary>
    public class ApprovalDecisionResponse
    {
        public string Message { get; set; }
        public string ApprovalId { get; set; }
        public string Status { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    /// <summary>
    /// 페이지네이션 응답
    /// </summary>
    public class PaginationResponse<T>
    {
        public List<T> Data { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// API 에러 응답
    /// </summary>
    public class ApiErrorResponse
    {
        public string Error { get; set; }
        public string Message { get; set; }
    }
}
