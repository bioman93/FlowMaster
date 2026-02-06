using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Core.Interfaces;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.ViewModels
{
    public class WriteViewModel : ObservableObject
    {
        private readonly IApprovalService _approvalService;
        private readonly IUserRepository _userRepo;
        
        private User _writer;
        public User Writer
        {
            get => _writer;
            private set => SetProperty(ref _writer, value);
        }

        // Form Fields
        private string _title;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        // Test Results (Simple single item for now or list)
        // Let's use a List but for MVP provide inputs for one result or just a collection.
        // Let's make it simple: Title acts as payload summary.
        // Actually specs said grid input.
        public ObservableCollection<TestResult> TestResults { get; } = new ObservableCollection<TestResult>();

        // Selected Approver
        private User _selectedApprover;
        public User SelectedApprover
        {
            get => _selectedApprover;
            set => SetProperty(ref _selectedApprover, value);
        }

        public ObservableCollection<User> ApproverCandidates { get; } = new ObservableCollection<User>();

        public ICommand SubmitCommand { get; }
        public ICommand AddTestResultCommand { get; }

        public WriteViewModel(IApprovalService approvalService, IUserRepository userRepo)
        {
            _approvalService = approvalService;
            _userRepo = userRepo;
            SubmitCommand = new RelayCommand(Submit);
            AddTestResultCommand = new RelayCommand(AddTestResult);
            
            LoadApprovers();
            
            // Add initial empty test result row
            TestResults.Add(new TestResult { TestDate = DateTime.Today, ProjectName = "FlowMaster", Version = "1.0" });
        }

        public void SetWriter(User writer)
        {
            Writer = writer;
        }

        private async void LoadApprovers()
        {
            // In real app, filter appropriately. Here show all Approvers.
            var approvers = await _userRepo.GetUsersByRoleAsync(UserRole.Approver);
            foreach (var app in approvers)
            {
                ApproverCandidates.Add(app);
            }
        }

        private void AddTestResult()
        {
            TestResults.Add(new TestResult { TestDate = DateTime.Today });
        }

        private async void Submit()
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                MessageBox.Show("제목을 입력하세요.");
                return;
            }
            if (SelectedApprover == null)
            {
                MessageBox.Show("결재자를 선택하세요.");
                return;
            }

            try
            {
                var doc = new ApprovalDocument
                {
                    Title = Title,
                    WriterId = Writer?.UserId ?? "Unknown",
                    WriterName = Writer?.Name ?? "Unknown",
                    TestResults = TestResults.ToList()
                };

                // Simple 1-step approval for MVP
                var approvers = new List<string> { SelectedApprover.AdAccount };

                await _approvalService.SubmitDocumentAsync(doc, approvers);

                MessageBox.Show("결재가 상신되었습니다.");
                // Reset form
                Title = string.Empty;
                TestResults.Clear();
                AddTestResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"오류 발생: {ex.Message}");
            }
        }
    }
}
