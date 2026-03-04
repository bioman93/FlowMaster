using System;
using System.Collections.Generic;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// ApprovalSystem /api/fm/* 엔드포인트와 주고받는 DTO.
    /// FlowMaster.Domain.Models과 1:1 매핑되며, ApiApprovalRepository에서 변환합니다.
    /// </summary>

    public class FmDocumentDto
    {
        public int DocId { get; set; }
        public string Title { get; set; }
        public string WriterId { get; set; }
        public string WriterName { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime? UpdateDate { get; set; }
        public int Status { get; set; }
        public string CurrentApproverId { get; set; }
        public string CurrentApproverName { get; set; }
        public string ApprovalId { get; set; }
        public string IssueKey { get; set; }
        public string TableType { get; set; }
        public string GenType { get; set; }
        public string InjType { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string OutputPath { get; set; }
        public string ApproverComment { get; set; }
        public DateTime? ApprovalTime { get; set; }
        public int SyncStatus { get; set; }
        public int SyncRetryCount { get; set; }
        public string SyncError { get; set; }
    }

    public class FmChecklistItemDto
    {
        public int ItemId { get; set; }
        public int DocId { get; set; }
        public string RowNo { get; set; }
        public string CheckItem { get; set; }
        public string OutputContent { get; set; }
        public string EvaluationCode { get; set; }
        public string Remarks { get; set; }
        public int DisplayOrder { get; set; }
    }

    public class FmApprovalLineDto
    {
        public int LineId { get; set; }
        public int DocId { get; set; }
        public string ApproverId { get; set; }
        public string ApproverName { get; set; }
        public int Sequence { get; set; }
        public int Status { get; set; }
        public DateTime? ActionDate { get; set; }
        public string Comment { get; set; }
    }

    public class FmTestResultDto
    {
        public int ResultId { get; set; }
        public int DocId { get; set; }
        public string ProjectName { get; set; }
        public string Version { get; set; }
        public DateTime TestDate { get; set; }
        public string TestCaseName { get; set; }
        public bool IsPass { get; set; }
        public string FailureReason { get; set; }
        public string Details { get; set; }
        public string BackupDbSource { get; set; }
    }

    public class FmParticipantDto
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
    }

    public class FmStatusUpdateRequest
    {
        public int Status { get; set; }
    }

    public class FmLineStatusUpdateRequest
    {
        public int Status { get; set; }
        public string Comment { get; set; }
    }

    public class FmSyncStatusUpdateRequest
    {
        public int SyncStatus { get; set; }
        public int RetryCount { get; set; }
        public string Error { get; set; }
    }

    // ─── 앱 사용자/그룹 DTO ──────────────────────────────────────────

    /// <summary>앱 등록 사용자 DTO (FM_AppUsers)</summary>
    public class FmAppUserDto
    {
        public string UserId      { get; set; }
        public string DisplayName { get; set; }
        public string Email       { get; set; }
        public int    IsEnabled   { get; set; }   // 1=활성, 0=비활성
        public string CreatedAt   { get; set; }
        public string UpdatedAt   { get; set; }
        /// <summary>소속 그룹명 목록. 서버에서 FM_AppGroupMembers JOIN으로 채워짐.</summary>
        public System.Collections.Generic.List<string> Groups { get; set; }
            = new System.Collections.Generic.List<string>();
    }

    /// <summary>앱 그룹 DTO (FM_AppGroups)</summary>
    public class FmAppGroupDto
    {
        public int    GroupId     { get; set; }
        public string GroupName   { get; set; }
        public string Description { get; set; }
        /// <summary>GeneralUser/Approver/Admin 기본 그룹 여부. true 이면 삭제 불가.</summary>
        public bool   IsDefault   { get; set; }
        public string CreatedAt   { get; set; }
        public System.Collections.Generic.List<FmAppGroupMemberDto> Members { get; set; }
            = new System.Collections.Generic.List<FmAppGroupMemberDto>();
    }

    /// <summary>앱 그룹 멤버 DTO</summary>
    public class FmAppGroupMemberDto
    {
        public string UserId      { get; set; }
        public string DisplayName { get; set; }
        public string Email       { get; set; }
    }

    /// <summary>그룹 멤버 추가 요청</summary>
    public class FmAddGroupMemberRequest
    {
        public string UserId { get; set; }
    }

    // ─── 체크리스트 템플릿 DTO ──────────────────────────────────────

    /// <summary>체크리스트 템플릿 DTO</summary>
    public class FmChecklistTemplateDto
    {
        public int TemplateId { get; set; }
        public string TemplateCode { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public bool IsLatest { get; set; }
        public string CreatedAt { get; set; }
        /// <summary>문서 작성 시 설명(Description) 입력란 표시 여부</summary>
        public bool HasDescription { get; set; }
        public List<FmChecklistTemplateItemDto> Items { get; set; }
            = new List<FmChecklistTemplateItemDto>();
    }

    /// <summary>체크리스트 템플릿 항목 DTO</summary>
    public class FmChecklistTemplateItemDto
    {
        public int ItemId { get; set; }
        public int TemplateId { get; set; }
        public string RowNo { get; set; }
        public string CheckItem { get; set; }
        public string OutputContent { get; set; }
        /// <summary>평가 기본값 (+, (+), (-), -, nb). 문서 작성 시 초기값.</summary>
        public string EvaluationCode { get; set; }
        /// <summary>비고 기본값 (MPI/GDI 무관 공통)</summary>
        public string Remarks { get; set; }
        /// <summary>MPI 전용 비고 기본값. 값이 있으면 InjType=MPI 문서에서 Remarks 대신 사용.</summary>
        public string RemarksMpi { get; set; }
        /// <summary>GDI 전용 비고 기본값. 값이 있으면 InjType=GDI 문서에서 Remarks 대신 사용.</summary>
        public string RemarksGdi { get; set; }
        public int DisplayOrder { get; set; }

        /// <summary>RowNo에 '.'이 없으면 메인(헤더) 항목 (예: "1", "2")</summary>
        public bool IsHeader => !string.IsNullOrEmpty(RowNo) && !RowNo.Contains(".");
    }

    /// <summary>템플릿 저장 요청</summary>
    public class FmSaveTemplateRequest
    {
        public FmChecklistTemplateDto Template { get; set; }
        public bool CreateNewVersion { get; set; }
    }

    // ─── 산출물 경로 설정 DTO ────────────────────────────────────────

    /// <summary>산출물 버튼 경로 설정 DTO (FM_OutputPathConfig)</summary>
    public class FmOutputPathConfigDto
    {
        public int Id { get; set; }
        public string TableType { get; set; }
        public string ButtonName { get; set; }
        public string Path { get; set; }
        /// <summary>"SVN" 또는 "Explorer"</summary>
        public string LaunchType { get; set; } = "Explorer";
        public int DisplayOrder { get; set; }
    }
}
