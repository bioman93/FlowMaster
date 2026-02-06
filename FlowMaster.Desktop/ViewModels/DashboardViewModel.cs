using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.ViewModels
{
    public class DashboardViewModel : ObservableObject
    {
        private readonly IApprovalRepository _approvalRepo;
        private readonly IUserRepository _userRepo;
        // In a real app, CurrentUser should be provided by a UserContext service. 
        // For simplicity, we might need value injection or a way to get valid userId.
        // For now, let's assume we can pass the User ID or we inject MainViewModel? No, circular dependency.
        // Let's rely on a Refresh method that accepts userId, or better: 
        // Create a SessionService. But for now, I'll add a LoadData(User user) method called by MainViewModel.

        private ObservableCollection<ApprovalDocument> _pendingApprovals;
        public ObservableCollection<ApprovalDocument> PendingApprovals
        {
            get => _pendingApprovals;
            set => SetProperty(ref _pendingApprovals, value);
        }

        private ObservableCollection<ApprovalDocument> _myDrafts;
        public ObservableCollection<ApprovalDocument> MyDrafts
        {
            get => _myDrafts;
            set => SetProperty(ref _myDrafts, value);
        }

        public ICommand OpenDetailCommand { get; }
        public Action<ApprovalDocument> OnOpenDetailRequest;

        public DashboardViewModel(IApprovalRepository approvalRepo, IUserRepository userRepo)
        {
            _approvalRepo = approvalRepo;
            _userRepo = userRepo;
            PendingApprovals = new ObservableCollection<ApprovalDocument>();
            MyDrafts = new ObservableCollection<ApprovalDocument>();
            OpenDetailCommand = new RelayCommand<ApprovalDocument>(OpenDetail);
        }

        private void OpenDetail(ApprovalDocument doc)
        {
            if (doc != null)
            {
                OnOpenDetailRequest?.Invoke(doc);
            }
        }

        public async Task LoadDataAsync(User user)
        {
            if (user == null) return;

            // 1. Get Pending Approvals (Documents where I am the current approver)
            var pending = await _approvalRepo.GetPendingApprovalsAsync(user.AdAccount);
            PendingApprovals = new ObservableCollection<ApprovalDocument>(pending);

            // 2. Get My Drafts/Requests
            var drafts = await _approvalRepo.GetMyDraftsAsync(user.UserId); // MockRepo uses UserId for writer? SqliteRepo uses WriterId.
            // Check entities: User.UserId vs AdAccount. 
            // MockUserRepository: UserId="U001", AdAccount="user".
            // SqliteApprovalRepository: GetMyDraftsAsync uses "WriterId".
            // Usually WriterId refers to UserId.
            MyDrafts = new ObservableCollection<ApprovalDocument>(drafts);
        }
    }
}
