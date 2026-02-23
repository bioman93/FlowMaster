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
        Task<ApprovalDocument> GetDocumentAsync(int docId);
        Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId);
        Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId);
        
        // Approval Line logic
        Task AddApprovalLineAsync(ApprovalLine line);
        Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment);
        
        // Test Result logic
        Task AddTestResultAsync(TestResult result);
        Task<List<TestResult>> GetTestResultsAsync(int docId);
    }
}
