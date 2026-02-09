using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;

namespace FlowMaster.Desktop.ViewModels
{
    public class TestInputViewModel : ObservableObject
    {
        private readonly ExternalDbRepository _externalDb;
        private readonly Action _onGoBack;
        private User _currentUser;
        private bool _isNewDocument;

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
        public Visibility CanSubmit => _isNewDocument || StatusText == "작성중" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CanApprove => StatusText == "승인대기" && _currentUser?.Role == UserRole.Approver ? Visibility.Visible : Visibility.Collapsed;

        #endregion

        #region Commands

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }

        #endregion

        public TestInputViewModel(ExternalDbRepository externalDb, Action onGoBack)
        {
            _externalDb = externalDb;
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

            if (!_externalDb.IsConnected)
            {
                MessageBox.Show("외부 DB에 연결되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var doc = await _externalDb.GetDocumentWithChecklistAsync(docId);
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
            StatusText = doc.Status.ToString();
            StatusColor = GetStatusColor(doc.Status.ToString());

            ChecklistItems.Clear();
            foreach (var item in doc.ChecklistItems)
            {
                ChecklistItems.Add(item);
            }

            OnPropertyChanged(nameof(ChecklistItemCount));
            OnPropertyChanged(nameof(ShowDescription));
            OnPropertyChanged(nameof(ShowApproverComment));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(CanApprove));
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
                case "Pending": return "#ff9800";
                case "Approved": return "#4CAF50";
                case "Rejected": return "#f44336";
                default: return "#666";
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                if (!_externalDb.IsConnected)
                {
                    MessageBox.Show("외부 DB에 연결되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var doc = new ApprovalDocument
                {
                    DocId = DocId,
                    Title = Title,
                    TableType = TableType,
                    GenType = GenType,
                    InjType = InjType,
                    WriterName = WriterName,
                    Description = Description,
                    CurrentApproverId = SelectedApprover?.UserId,
                    CreateDate = DateTime.Now,
                    Status = ApprovalStatus.TempSaved
                };

                DocId = await _externalDb.SaveDocumentAsync(doc);
                await _externalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                _isNewDocument = false;
                MessageBox.Show("저장되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
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

            await SaveAsync();

            StatusText = "승인대기";
            StatusColor = "#ff9800";
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(CanApprove));

            MessageBox.Show("결재가 상신되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task ApproveAsync()
        {
            StatusText = "승인완료";
            StatusColor = "#4CAF50";
            await SaveAsync();
            MessageBox.Show("승인되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RejectAsync()
        {
            StatusText = "반려";
            StatusColor = "#f44336";
            await SaveAsync();
            MessageBox.Show("반려되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
