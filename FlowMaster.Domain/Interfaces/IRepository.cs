using System.Collections.Generic;
using System.Threading.Tasks;
using FlowMaster.Domain.Models;

namespace FlowMaster.Domain.Interfaces
{
    public interface IUserRepository
    {
        Task<List<User>> GetAllUsersAsync();
        Task<User> GetUserByAdAccountAsync(string adAccount);
        Task<List<User>> GetUsersByRoleAsync(UserRole role);
        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(string userId);
    }

    public interface IAppGroupRepository
    {
        Task<List<AppGroup>> GetAllGroupsAsync();
        Task<AppGroup> GetGroupWithMembersAsync(int groupId);
        Task<int> AddGroupAsync(AppGroup group);
        Task UpdateGroupAsync(AppGroup group);
        Task DeleteGroupAsync(int groupId);
        Task AddGroupMemberAsync(int groupId, string userId);
        Task RemoveGroupMemberAsync(int groupId, string userId);
    }

    public interface IApprovalRepository
    {
        Task<int> CreateDocumentAsync(ApprovalDocument doc);
        Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status);
        /// <summary>
        /// ApprovalSystem에서 발급된 결재 ID를 FM 문서에 연결합니다.
        /// </summary>
        Task UpdateApprovalIdAsync(int docId, string approvalId);
        /// <summary>
        /// ApprovalId로 FM 문서를 조회합니다. 상태 동기화에 사용합니다.
        /// </summary>
        Task<ApprovalDocument> GetDocumentByApprovalIdAsync(string approvalId);
        /// <summary>
        /// 미동기화 문서 목록 조회 (SyncStatus=Pending/Failed, SyncRetryCount &lt; 3).
        /// 연결 복구 시 ApprovalSystem 재등록에 사용합니다.
        /// </summary>
        Task<List<ApprovalDocument>> GetUnsyncedDocumentsAsync();
        /// <summary>
        /// 동기화 상태를 업데이트합니다.
        /// </summary>
        Task UpdateSyncStatusAsync(int docId, SyncStatus status, int retryCount, string error);
        Task<ApprovalDocument> GetDocumentAsync(int docId);
        Task<List<ApprovalDocument>> GetAllDocumentsAsync();
        Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId);
        Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId);
        Task DeleteDocumentAsync(int docId);
        
        // Approval Line logic
        Task AddApprovalLineAsync(ApprovalLine line);
        Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment);
        
        // Test Result logic
        Task AddTestResultAsync(TestResult result);
        Task<List<TestResult>> GetTestResultsAsync(int docId);

        // Participant (Watcher) logic
        Task<List<User>> GetDocumentParticipantsAsync(int docId);
        Task SaveDocumentParticipantsAsync(int docId, List<User> participants);
        Task AddDocumentParticipantAsync(int docId, User user);
        Task RemoveDocumentParticipantAsync(int docId, string userId);
        Task<List<User>> GetParticipantGroupAsync(string groupName);
        Task AddParticipantGroupMemberAsync(string groupName, User user);
        Task RemoveParticipantGroupMemberAsync(string groupName, string userId);
    }
}
