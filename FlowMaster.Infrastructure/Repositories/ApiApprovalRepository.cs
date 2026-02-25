using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Infrastructure.Repositories
{
    /// <summary>
    /// ApprovalSystem REST API(/api/fm/*)를 통해 IApprovalRepository를 구현하는 Adapter.
    /// FlowMaster.Desktop은 이 클래스만 사용하며 SQLite에 직접 접근하지 않습니다.
    /// </summary>
    public class ApiApprovalRepository : IApprovalRepository
    {
        private readonly ApprovalApiClient _client;

        public ApiApprovalRepository(ApprovalApiClient client)
        {
            _client = client;
        }

        // ─── 변환 헬퍼 ──────────────────────────────────────────────

        private static FmDocumentDto ToDto(ApprovalDocument doc) => new FmDocumentDto
        {
            DocId               = doc.DocId,
            Title               = doc.Title,
            WriterId            = doc.WriterId,
            WriterName          = doc.WriterName,
            CreateDate          = doc.CreateDate,
            UpdateDate          = doc.UpdateDate,
            Status              = (int)doc.Status,
            CurrentApproverId   = doc.CurrentApproverId,
            CurrentApproverName = doc.CurrentApproverName,
            ApprovalId          = doc.ApprovalId,
            IssueKey            = doc.IssueKey,
            TableType           = doc.TableType,
            GenType             = doc.GenType,
            InjType             = doc.InjType,
            Description         = doc.Description,
            Version             = doc.Version,
            OutputPath          = doc.OutputPath,
            ApproverComment     = doc.ApproverComment,
            ApprovalTime        = doc.ApprovalTime,
            SyncStatus          = (int)doc.SyncStatus,
            SyncRetryCount      = doc.SyncRetryCount,
            SyncError           = doc.SyncError,
        };

        private static ApprovalDocument FromDto(FmDocumentDto dto) => new ApprovalDocument
        {
            DocId               = dto.DocId,
            Title               = dto.Title,
            WriterId            = dto.WriterId,
            WriterName          = dto.WriterName,
            CreateDate          = dto.CreateDate,
            UpdateDate          = dto.UpdateDate,
            Status              = (ApprovalStatus)dto.Status,
            CurrentApproverId   = dto.CurrentApproverId,
            CurrentApproverName = dto.CurrentApproverName,
            ApprovalId          = dto.ApprovalId,
            IssueKey            = dto.IssueKey,
            TableType           = dto.TableType,
            GenType             = dto.GenType,
            InjType             = dto.InjType,
            Description         = dto.Description,
            Version             = dto.Version,
            OutputPath          = dto.OutputPath,
            ApproverComment     = dto.ApproverComment,
            ApprovalTime        = dto.ApprovalTime,
            SyncStatus          = (SyncStatus)dto.SyncStatus,
            SyncRetryCount      = dto.SyncRetryCount,
            SyncError           = dto.SyncError,
        };

        private static FmChecklistItemDto ToDto(ChecklistItem item) => new FmChecklistItemDto
        {
            ItemId          = item.ItemId,
            DocId           = item.DocId,
            RowNo           = item.RowNo,
            CheckItem       = item.CheckItem,
            OutputContent   = item.OutputContent,
            EvaluationCode  = item.EvaluationCode,
            Remarks         = item.Remarks,
            DisplayOrder    = item.DisplayOrder,
        };

        private static ChecklistItem FromDto(FmChecklistItemDto dto) => new ChecklistItem
        {
            ItemId          = dto.ItemId,
            DocId           = dto.DocId,
            RowNo           = dto.RowNo,
            CheckItem       = dto.CheckItem,
            OutputContent   = dto.OutputContent,
            EvaluationCode  = dto.EvaluationCode,
            Remarks         = dto.Remarks,
            DisplayOrder    = dto.DisplayOrder,
        };

        private static FmApprovalLineDto ToDto(ApprovalLine line) => new FmApprovalLineDto
        {
            LineId       = line.LineId,
            DocId        = line.DocId,
            ApproverId   = line.ApproverId,
            ApproverName = line.ApproverName,
            Sequence     = line.Sequence,
            Status       = (int)line.Status,
            ActionDate   = line.ActionDate,
            Comment      = line.Comment,
        };

        private static FmParticipantDto ToDto(User user) => new FmParticipantDto
        {
            UserId   = user.UserId,
            UserName = user.Name,
        };

        private static User FromDto(FmParticipantDto dto) => new User
        {
            UserId = dto.UserId,
            Name   = dto.UserName,
        };

        // ─── 문서 ──────────────────────────────────────────────────

        public async Task<int> CreateDocumentAsync(ApprovalDocument doc)
            => await _client.FmCreateDocumentAsync(ToDto(doc));

        public async Task UpdateDocumentAsync(ApprovalDocument doc)
            => await _client.FmUpdateDocumentAsync(ToDto(doc));

        public async Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status)
            => await _client.FmUpdateStatusAsync(docId, (int)status);

        public async Task DeleteDocumentAsync(int docId)
            => await _client.FmDeleteDocumentAsync(docId);

        public async Task<ApprovalDocument> GetDocumentAsync(int docId)
        {
            var dto = await _client.FmGetDocumentAsync(docId);
            return dto is null ? null : FromDto(dto);
        }

        public async Task<List<ApprovalDocument>> GetAllDocumentsAsync()
        {
            var list = await _client.FmGetAllDocumentsAsync();
            return list.Select(FromDto).ToList();
        }

        public async Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId)
        {
            var list = await _client.FmGetMyDraftsAsync(userId);
            return list.Select(FromDto).ToList();
        }

        public async Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId)
        {
            var list = await _client.FmGetPendingAsync(approverId);
            return list.Select(FromDto).ToList();
        }

        public async Task UpdateApprovalIdAsync(int docId, string approvalId)
            => await _client.FmUpdateApprovalIdAsync(docId, approvalId);

        public async Task<ApprovalDocument> GetDocumentByApprovalIdAsync(string approvalId)
        {
            var dto = await _client.FmGetDocumentByApprovalIdAsync(approvalId);
            return dto is null ? null : FromDto(dto);
        }

        public async Task<List<ApprovalDocument>> GetUnsyncedDocumentsAsync()
        {
            var list = await _client.FmGetUnsyncedDocumentsAsync();
            return list.Select(FromDto).ToList();
        }

        public async Task UpdateSyncStatusAsync(int docId, SyncStatus status, int retryCount, string error)
            => await _client.FmUpdateSyncStatusAsync(docId, (int)status, retryCount, error);

        // ─── 체크리스트 ────────────────────────────────────────────

        public async Task SaveChecklistItemsAsync(int docId, List<ChecklistItem> items)
            => await _client.FmSaveChecklistAsync(docId, items.Select(ToDto).ToList());

        public async Task<List<ChecklistItem>> GetChecklistItemsAsync(int docId)
        {
            var list = await _client.FmGetChecklistAsync(docId);
            return list.Select(FromDto).ToList();
        }

        // ─── 결재선 ───────────────────────────────────────────────

        public async Task AddApprovalLineAsync(ApprovalLine line)
            => await _client.FmAddApprovalLineAsync(line.DocId, ToDto(line));

        public async Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment)
            => await _client.FmUpdateLineStatusAsync(lineId, (int)status, comment);

        // ─── 테스트 결과 ──────────────────────────────────────────

        public async Task AddTestResultAsync(TestResult result)
        {
            await _client.FmAddTestResultAsync(result.DocId, new FmTestResultDto
            {
                DocId         = result.DocId,
                ProjectName   = result.ProjectName,
                Version       = result.Version,
                TestDate      = result.TestDate,
                TestCaseName  = result.TestCaseName,
                IsPass        = result.IsPass,
                FailureReason = result.FailureReason,
                Details       = result.Details,
                BackupDbSource= result.BackupDbSource,
            });
        }

        public async Task<List<TestResult>> GetTestResultsAsync(int docId)
        {
            var list = await _client.FmGetTestResultsAsync(docId);
            return list.Select(dto => new TestResult
            {
                ResultId      = dto.ResultId,
                DocId         = dto.DocId,
                ProjectName   = dto.ProjectName,
                Version       = dto.Version,
                TestDate      = dto.TestDate,
                TestCaseName  = dto.TestCaseName,
                IsPass        = dto.IsPass,
                FailureReason = dto.FailureReason,
                Details       = dto.Details,
                BackupDbSource= dto.BackupDbSource,
            }).ToList();
        }

        // ─── 참여자 ──────────────────────────────────────────────

        public async Task<List<User>> GetDocumentParticipantsAsync(int docId)
        {
            var list = await _client.FmGetParticipantsAsync(docId);
            return list.Select(FromDto).ToList();
        }

        public async Task SaveDocumentParticipantsAsync(int docId, List<User> participants)
            => await _client.FmSaveParticipantsAsync(docId, participants.Select(ToDto).ToList());

        public async Task AddDocumentParticipantAsync(int docId, User user)
        {
            await _client.FmSaveParticipantsAsync(docId,
                new List<FmParticipantDto> { ToDto(user) });
        }

        public async Task RemoveDocumentParticipantAsync(int docId, string userId)
        {
            var current = await _client.FmGetParticipantsAsync(docId);
            var updated = current.Where(p => p.UserId != userId).ToList();
            await _client.FmSaveParticipantsAsync(docId, updated);
        }

        public async Task<List<User>> GetParticipantGroupAsync(string groupName)
        {
            var list = await _client.FmGetGroupAsync(groupName);
            return list.Select(FromDto).ToList();
        }

        public async Task AddParticipantGroupMemberAsync(string groupName, User user)
            => await _client.FmAddGroupMemberAsync(groupName, ToDto(user));

        public async Task RemoveParticipantGroupMemberAsync(string groupName, string userId)
            => await _client.FmRemoveGroupMemberAsync(groupName, userId);
    }
}
