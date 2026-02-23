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
using FlowMaster.Domain.DTOs;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Desktop.ViewModels
{
    public class WriteViewModel : ObservableObject
    {
        private readonly IApprovalService _approvalService;
        private readonly IApprovalRepository _approvalRepo;
        private readonly IUserRepository _userRepo;
        private readonly ApprovalApiClient _approvalApiClient;
        
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

        public WriteViewModel(
            IApprovalService approvalService,
            IApprovalRepository approvalRepo,
            IUserRepository userRepo,
            ApprovalApiClient approvalApiClient)
        {
            _approvalService = approvalService;
            _approvalRepo = approvalRepo;
            _userRepo = userRepo;
            _approvalApiClient = approvalApiClient;
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

                // 1단계 결재선 구성
                var approvers = new List<string> { SelectedApprover.AdAccount };

                // FM 내부 DB에 결재 문서 저장
                int docId = await _approvalService.SubmitDocumentAsync(doc, approvers);

                // ApprovalSystem에 결재 위임 (연결 가능한 경우)
                try
                {
                    var apiRequest = new CreateApprovalRequest
                    {
                        Title = Title,
                        RequesterId = Writer?.UserId ?? "Unknown",
                        RequesterName = Writer?.Name ?? "Unknown",
                        ApproverIds = approvers,
                        SourceApp = "FlowMaster",
                        SourceId = docId.ToString(),
                        Description = $"FlowMaster 테스트 결과 승인 요청 (문서 #{docId})"
                    };

                    var apiResponse = await _approvalApiClient.CreateApprovalAsync(apiRequest);

                    // ApprovalSystem에서 발급된 결재 ID를 FM 문서에 연결
                    if (!string.IsNullOrEmpty(apiResponse?.Id))
                        await _approvalRepo.UpdateApprovalIdAsync(docId, apiResponse.Id);
                }
                catch (Exception apiEx)
                {
                    // ApprovalSystem 미실행 시 경고만 표시하고 계속 진행
                    MessageBox.Show(
                        $"결재 문서가 저장되었지만 ApprovalSystem 연동에 실패했습니다.\n({apiEx.Message})\n\nFM 내부 결재로만 처리됩니다.",
                        "ApprovalSystem 연동 경고",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                MessageBox.Show("결재가 상신되었습니다.");
                // 폼 초기화
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
