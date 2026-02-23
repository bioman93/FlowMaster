using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Domain.DTOs;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Desktop.ViewModels
{
    public class TestInputViewModel : ObservableObject
    {
        private readonly ExternalDbRepository _externalDb;
        private readonly SqliteApprovalRepository _internalDb;
        private readonly ApprovalApiClient _apiClient;
        private readonly Action _onGoBack;
        private User _currentUser;
        private bool _isNewDocument;
        private bool _useInternalDb; // 내부 DB 사용 여부
        private string _approvalId; // API 결재 ID (APV-xxx)

        #region Properties

        private int _docId;
        public int DocId
        {
            get => _docId;
            set => SetProperty(ref _docId, value);
        }

        private string _title = "새 테스트 문서";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _tableType;
        public string TableType
        {
            get => _tableType;
            set => SetProperty(ref _tableType, value);
        }

        private string _genType;
        public string GenType
        {
            get => _genType;
            set => SetProperty(ref _genType, value);
        }

        private string _injType;
        public string InjType
        {
            get => _injType;
            set => SetProperty(ref _injType, value);
        }

        private string _writerName;
        public string WriterName
        {
            get => _writerName;
            set => SetProperty(ref _writerName, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private string _approverComment;
        public string ApproverComment
        {
            get => _approverComment;
            set => SetProperty(ref _approverComment, value);
        }

        private string _statusText = "작성중";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _statusColor = "#666";
        public string StatusColor
        {
            get => _statusColor;
            set => SetProperty(ref _statusColor, value);
        }

        private string _approvalDate;
        public string ApprovalDate
        {
            get => _approvalDate;
            set => SetProperty(ref _approvalDate, value);
        }

        private User _selectedApprover;
        public User SelectedApprover
        {
            get => _selectedApprover;
            set => SetProperty(ref _selectedApprover, value);
        }

        public List<User> AvailableApprovers { get; set; } = new List<User>();

        public ObservableCollection<ChecklistItem> ChecklistItems { get; } = new ObservableCollection<ChecklistItem>();

        public int ChecklistItemCount => ChecklistItems.Count;

        // Visibility helpers
        public Visibility ShowDescription => TableType == "BA2" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowApproverComment => !string.IsNullOrEmpty(ApproverComment) ? Visibility.Visible : Visibility.Collapsed;
        // 작성중, 임시저장 상태면 결재 상신 가능
        public Visibility CanSubmit => (StatusText == "작성중" || StatusText == "임시저장" || _isNewDocument) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CanApprove => StatusText == "승인대기" && _currentUser?.Role == UserRole.Approver ? Visibility.Visible : Visibility.Collapsed;

        // 승인완료/반려 상태면 수정 불가
        public bool IsReadOnly => StatusText == "승인완료" || StatusText == "반려";
        public bool CanEdit => !IsReadOnly;
        public Visibility CanSave => IsReadOnly ? Visibility.Collapsed : Visibility.Visible;

        #endregion

        #region Commands

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }

        #endregion

        public TestInputViewModel(ExternalDbRepository externalDb, SqliteApprovalRepository internalDb, ApprovalApiClient apiClient, Action onGoBack)
        {
            _externalDb = externalDb;
            _internalDb = internalDb;
            _apiClient = apiClient;
            _onGoBack = onGoBack;

            GoBackCommand = new RelayCommand(() => _onGoBack?.Invoke());
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            ApproveCommand = new AsyncRelayCommand(ApproveAsync);
            RejectCommand = new AsyncRelayCommand(RejectAsync);
        }

        /// <summary>
        /// 새 문서 생성 모드로 초기화
        /// </summary>
        public async Task InitializeNewDocumentAsync(string tableType, ApprovalDocument cloneSource, User currentUser, List<User> approvers)
        {
            _isNewDocument = true;
            _currentUser = currentUser;
            AvailableApprovers = approvers;
            
            TableType = tableType;
            WriterName = currentUser.Name;
            Title = $"새 {tableType} 테스트 문서";
            StatusText = "작성중";
            StatusColor = "#666";

            ChecklistItems.Clear();

            if (cloneSource != null && _externalDb.IsConnected)
            {
                // Clone from existing document
                var clonedItems = await _externalDb.CloneChecklistFromDocumentAsync(cloneSource.DocId);
                foreach (var item in clonedItems)
                {
                    ChecklistItems.Add(item);
                }
                GenType = cloneSource.GenType;
                InjType = cloneSource.InjType;
            }
            else
            {
                // Load default template
                LoadDefaultChecklist(tableType);
            }

            OnPropertyChanged(nameof(ChecklistItemCount));
            OnPropertyChanged(nameof(ShowDescription));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(CanApprove));
        }

        /// <summary>
        /// 기존 문서 편집 모드로 초기화
        /// </summary>
        public async Task InitializeExistingDocumentAsync(int docId, User currentUser, List<User> approvers)
        {
            _isNewDocument = false;
            _currentUser = currentUser;
            AvailableApprovers = approvers;

            ApprovalDocument doc = null;

            // 외부 DB 연결되면 외부 DB에서 조회, 아니면 내부 DB 사용
            if (_externalDb != null && _externalDb.IsConnected)
            {
                doc = await _externalDb.GetDocumentWithChecklistAsync(docId);
                _useInternalDb = false;
            }
            else
            {
                // 내부 DB에서 조회
                doc = await _internalDb.GetDocumentAsync(docId);
                _useInternalDb = true;
            }

            if (doc == null)
            {
                MessageBox.Show("문서를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DocId = doc.DocId;
            Title = doc.Title;
            TableType = doc.TableType;
            GenType = doc.GenType;
            InjType = doc.InjType;
            WriterName = doc.WriterName;
            Description = doc.Description;
            ApproverComment = doc.ApproverComment;
            ApprovalDate = doc.ApprovalTime?.ToString("yyyy-MM-dd") ?? "";
            StatusText = GetStatusText(doc.Status);
            StatusColor = GetStatusColor(StatusText);

            ChecklistItems.Clear();
            if (doc.ChecklistItems != null)
            {
                foreach (var item in doc.ChecklistItems)
                {
                    ChecklistItems.Add(item);
                }
            }

            OnPropertyChanged(nameof(ChecklistItemCount));
            OnPropertyChanged(nameof(ShowDescription));
            OnPropertyChanged(nameof(ShowApproverComment));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanSave));
        }

        private void LoadDefaultChecklist(string tableType)
        {
            // BA1: 22 items, BA2: 24 items (기본 템플릿)
            var items = GetDefaultChecklistItems(tableType);
            foreach (var item in items)
            {
                ChecklistItems.Add(item);
            }
        }

        private List<ChecklistItem> GetDefaultChecklistItems(string tableType)
        {
            // 기본 체크리스트 템플릿 (실제로는 외부 DB나 설정에서 로드)
            var items = new List<ChecklistItem>();
            int count = tableType == "BA1" ? 22 : 24;

            for (int i = 1; i <= count; i++)
            {
                items.Add(new ChecklistItem
                {
                    RowNo = $"{(i / 10) + 1}.{i % 10}",
                    CheckItem = $"확인항목 {i}",
                    DisplayOrder = i
                });
            }

            return items;
        }

        private string GetStatusColor(string status)
        {
            switch (status)
            {
                case "승인대기":
                case "Pending": return "#ff9800";
                case "승인완료":
                case "Approved": return "#4CAF50";
                case "반려":
                case "Rejected": return "#f44336";
                default: return "#666";
            }
        }

        private string GetStatusText(ApprovalStatus status)
        {
            switch (status)
            {
                case ApprovalStatus.TempSaved: return "임시저장";
                case ApprovalStatus.Pending: return "승인대기";
                case ApprovalStatus.Approved: return "승인완료";
                case ApprovalStatus.Rejected: return "반려";
                case ApprovalStatus.Canceled: return "취소됨";
                default: return "작성중";
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var doc = new ApprovalDocument
                {
                    DocId = DocId,
                    Title = Title,
                    TableType = TableType,
                    GenType = GenType,
                    InjType = InjType,
                    WriterName = WriterName,
                    WriterId = _currentUser?.UserId ?? "Unknown",
                    Description = Description,
                    CurrentApproverId = SelectedApprover?.UserId,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.TempSaved
                };

                if (_externalDb != null && _externalDb.IsConnected)
                {
                    // 외부 DB 사용
                    DocId = await _externalDb.SaveDocumentAsync(doc);
                    await _externalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());
                    _useInternalDb = false;
                }
                else
                {
                    // 내부 DB 사용 (fallback)
                    if (_isNewDocument || DocId == 0)
                    {
                        DocId = await _internalDb.CreateDocumentAsync(doc);
                    }
                    else
                    {
                        doc.DocId = DocId;
                        await _internalDb.UpdateDocumentAsync(doc);
                    }
                    await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());
                    _useInternalDb = true;
                }

                _isNewDocument = false;
                string dbType = _useInternalDb ? "(내부 DB)" : "(외부 DB)";
                MessageBox.Show($"저장되었습니다. {dbType}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SubmitAsync()
        {
            if (SelectedApprover == null)
            {
                MessageBox.Show("결재자를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 먼저 내부 DB에 저장 (임시저장 → Pending)
                var doc = new ApprovalDocument
                {
                    DocId = DocId,
                    Title = Title,
                    TableType = TableType,
                    GenType = GenType,
                    InjType = InjType,
                    WriterName = WriterName,
                    WriterId = _currentUser?.UserId ?? "Unknown",
                    Description = Description,
                    CurrentApproverId = SelectedApprover.UserId,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.Pending
                };

                // 내부 DB 저장
                if (_isNewDocument || DocId == 0)
                {
                    DocId = await _internalDb.CreateDocumentAsync(doc);
                }
                else
                {
                    doc.DocId = DocId;
                    await _internalDb.UpdateDocumentAsync(doc);
                }
                await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                // ApprovalService API 호출 (연결 시)
                if (_apiClient != null && await _apiClient.CheckConnectionAsync())
                {
                    var apiRequest = new CreateApprovalRequest
                    {
                        Title = Title,
                        Description = $"{TableType} - {GenType ?? ""} {InjType ?? ""}".Trim(),
                        RequesterId = _currentUser?.AdAccount ?? _currentUser?.UserId ?? "Unknown",
                        RequesterName = WriterName,
                        ApproverIds = new List<string> { SelectedApprover.AdAccount ?? SelectedApprover.UserId },
                        SourceApp = "FlowMaster",
                        SourceId = DocId.ToString()
                    };

                    var apiResponse = await _apiClient.CreateApprovalAsync(apiRequest);
                    _approvalId = apiResponse.Id;

                    // ApprovalId를 내부 DB에 저장
                    doc.DocId = DocId;
                    doc.ApprovalId = _approvalId;
                    await _internalDb.UpdateDocumentAsync(doc);
                }

                _isNewDocument = false;
                StatusText = "승인대기";
                StatusColor = "#ff9800";
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));

                var apiMsg = _approvalId != null ? $" (API ID: {_approvalId})" : " (로컬 전용)";
                MessageBox.Show($"결재가 상신되었습니다.{apiMsg}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"결재 상신 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApproveAsync()
        {
            try
            {
                // ApprovalService API 호출 (연결 시)
                if (_apiClient != null && !string.IsNullOrEmpty(_approvalId) && await _apiClient.CheckConnectionAsync())
                {
                    var approverId = _currentUser?.AdAccount ?? _currentUser?.UserId ?? "Unknown";
                    await _apiClient.MakeDecisionAsync(_approvalId, "approve", approverId, ApproverComment);
                }

                // 내부 DB 업데이트
                var doc = new ApprovalDocument
                {
                    DocId = DocId,
                    Title = Title,
                    TableType = TableType,
                    GenType = GenType,
                    InjType = InjType,
                    WriterName = WriterName,
                    WriterId = _currentUser?.UserId ?? "Unknown",
                    Description = Description,
                    ApproverComment = ApproverComment,
                    CurrentApproverId = SelectedApprover?.UserId,
                    ApprovalId = _approvalId,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.Approved
                };

                await _internalDb.UpdateDocumentAsync(doc);
                await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                StatusText = "승인완료";
                StatusColor = "#4CAF50";
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanSave));

                MessageBox.Show("승인되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"승인 처리 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RejectAsync()
        {
            try
            {
                // ApprovalService API 호출 (연결 시)
                if (_apiClient != null && !string.IsNullOrEmpty(_approvalId) && await _apiClient.CheckConnectionAsync())
                {
                    var approverId = _currentUser?.AdAccount ?? _currentUser?.UserId ?? "Unknown";
                    await _apiClient.MakeDecisionAsync(_approvalId, "reject", approverId, ApproverComment);
                }

                // 내부 DB 업데이트
                var doc = new ApprovalDocument
                {
                    DocId = DocId,
                    Title = Title,
                    TableType = TableType,
                    GenType = GenType,
                    InjType = InjType,
                    WriterName = WriterName,
                    WriterId = _currentUser?.UserId ?? "Unknown",
                    Description = Description,
                    ApproverComment = ApproverComment,
                    CurrentApproverId = SelectedApprover?.UserId,
                    ApprovalId = _approvalId,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.Rejected
                };

                await _internalDb.UpdateDocumentAsync(doc);
                await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                StatusText = "반려";
                StatusColor = "#f44336";
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanSave));

                MessageBox.Show("반려되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"반려 처리 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
