using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Core.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.ViewModels
{
    public class DetailViewModel : ObservableObject
    {
        private readonly IApprovalService _approvalService;
        private Action _goBackAction;

        private ApprovalDocument _document;
        public ApprovalDocument Document
        {
            get => _document;
            set => SetProperty(ref _document, value);
        }

        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            set 
            {
                if(SetProperty(ref _currentUser, value))
                {
                    OnPropertyChanged(nameof(CanApprove));
                }
            }
        }

        public bool CanApprove
        {
            get
            {
                if (Document == null || CurrentUser == null) return false;
                return Document.Status == ApprovalStatus.Pending && 
                       Document.CurrentApproverId == CurrentUser.AdAccount; // Mock user AdAccount is "approver"
            }
        }

        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand BackCommand { get; }

        public DetailViewModel(IApprovalService approvalService)
        {
            _approvalService = approvalService;
            ApproveCommand = new RelayCommand(Approve);
            RejectCommand = new RelayCommand(Reject);
            BackCommand = new RelayCommand(() => _goBackAction?.Invoke());
        }

        public void Initialize(ApprovalDocument doc, User user, Action goBackAction)
        {
            _goBackAction = goBackAction;
            CurrentUser = user;
            if (doc != null)
            {
                LoadDocument(doc.DocId);
            }
        }

        private async void LoadDocument(int docId)
        {
            Document = await _approvalService.GetDocumentDetailAsync(docId);
            OnPropertyChanged(nameof(CanApprove)); // Re-check after doc load
        }

        private async void Approve()
        {
            try
            {
                 if (!CanApprove) return;
                 // Input comment dialog could be added here, using "Approved" for now
                 await _approvalService.ApproveDocumentAsync(Document.DocId, CurrentUser.AdAccount, "Approved");
                 MessageBox.Show("승인 처리되었습니다.");
                 _goBackAction?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private async void Reject()
        {
            try
            {
                 if (!CanApprove) return;
                 await _approvalService.RejectDocumentAsync(Document.DocId, CurrentUser.AdAccount, "Rejected");
                 MessageBox.Show("반려 처리되었습니다.");
                 _goBackAction?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
    }
}
