using System.Collections.Generic;
using System.Threading.Tasks;
using FlowMaster.Domain.Models;

namespace FlowMaster.Domain.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetUserByAdAccountAsync(string adAccount);
        Task<List<User>> GetUsersByRoleAsync(UserRole role);
        Task AddUserAsync(User user);
    }

    public interface IApprovalRepository
    {
        Task<int> CreateDocumentAsync(ApprovalDocument doc);
        Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status);
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
