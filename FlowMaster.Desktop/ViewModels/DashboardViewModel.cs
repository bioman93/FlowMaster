using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Domain.DTOs;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Desktop.ViewModels
{
    public class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IApprovalRepository _approvalRepo;
        private readonly IUserRepository _userRepo;
        private readonly ApprovalApiClient _approvalApiClient;
        private readonly DispatcherTimer _pollingTimer;

        private User _currentUser;
        private bool _isRefreshing;
        private bool _disposed;

        // ── 컬렉션 속성 ────────────────────────────────────────────────────────
        private ObservableCollection<ApprovalDocument> _pendingApprovals;
        public ObservableCollection<ApprovalDocument> PendingApprovals
        {
            get => _pendingApprovals;
            set => SetProperty(ref _pendingApprovals, value);
        }

        private ObservableCollection<SelectableDocument> _myDrafts;
        public ObservableCollection<SelectableDocument> MyDrafts
        {
            get => _myDrafts;
            set => SetProperty(ref _myDrafts, value);
        }

        // ── 전체 문서 (페이지네이션 + 정렬) ────────────────────────────────────
        private List<ApprovalDocument> _allDocumentsRaw  = new List<ApprovalDocument>();
        private List<ApprovalDocument> _sortedDocuments  = new List<ApprovalDocument>();
        private string _sortColumn    = "CreateDate";
        private bool   _sortAscending = false;
        private string _searchText    = "";

        private ObservableCollection<ApprovalDocument> _allDocuments = new ObservableCollection<ApprovalDocument>();
        public ObservableCollection<ApprovalDocument> AllDocuments
        {
            get => _allDocuments;
            private set => SetProperty(ref _allDocuments, value);
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            private set { if (SetProperty(ref _currentPage, value)) NotifyPageState(); }
        }

        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set { if (SetProperty(ref _pageSize, value)) { _currentPage = 1; UpdateAllDocumentsPage(); } }
        }

        public List<int> PageSizeOptions { get; } = new List<int> { 10, 20, 50, 100 };
        public int    TotalCount => _sortedDocuments.Count;
        public int    TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);
        public string PageInfo   => $"{CurrentPage} / {TotalPages} 페이지  ({TotalCount:N0}건)";
        public bool   CanGoNext  => CurrentPage < TotalPages;
        public bool   CanGoPrev  => CurrentPage > 1;

        // ── 폴링 상태 속성 ──────────────────────────────────────────────────────
        /// <summary>
        /// 데이터 갱신 중 여부 (Spinner 표시용)
        /// </summary>
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set => SetProperty(ref _isRefreshing, value);
        }

        private DateTime _lastRefreshed;
        /// <summary>
        /// 마지막 갱신 시각
        /// </summary>
        public string LastRefreshedText
        {
            get => _lastRefreshed == default
                ? "갱신 전"
                : $"마지막 갱신: {_lastRefreshed:HH:mm:ss}";
        }

        /// <summary>
        /// 폴링 활성 여부
        /// </summary>
        public bool IsPollingActive => _pollingTimer.IsEnabled;

        /// <summary>
        /// 현재 사용자가 결재 권한(Approver/Admin)을 가지고 있는지 여부.
        /// 결재 대기 목록 표시 여부를 제어합니다.
        /// </summary>
        public bool IsApprover => _currentUser?.Role == UserRole.Approver || _currentUser?.Role == UserRole.Admin;

        // ── 명령 ────────────────────────────────────────────────────────────────
        public ICommand OpenDetailCommand { get; }
        public ICommand OpenPendingDetailCommand { get; }
        public ICommand OpenAllDocDetailCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand SearchCommand   { get; }
        public Action<ApprovalDocument> OnOpenDetailRequest;

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        // ── 생성자 ──────────────────────────────────────────────────────────────
        public DashboardViewModel(
            IApprovalRepository approvalRepo,
            IUserRepository userRepo,
            ApprovalApiClient approvalApiClient)
        {
            _approvalRepo = approvalRepo;
            _userRepo = userRepo;
            _approvalApiClient = approvalApiClient;
            PendingApprovals = new ObservableCollection<ApprovalDocument>();
            MyDrafts         = new ObservableCollection<SelectableDocument>();
            AllDocuments     = new ObservableCollection<ApprovalDocument>();

            OpenDetailCommand        = new RelayCommand<SelectableDocument>(sd => OpenDetail(sd?.Document));
            OpenPendingDetailCommand = new RelayCommand<ApprovalDocument>(doc => OpenDetail(doc));
            OpenAllDocDetailCommand  = new RelayCommand<ApprovalDocument>(doc => OpenDetail(doc));
            RefreshCommand           = new AsyncRelayCommand(RefreshAsync);
            DeleteSelectedCommand    = new AsyncRelayCommand(DeleteSelectedAsync);
            NextPageCommand          = new RelayCommand(() => { _currentPage++; UpdateAllDocumentsPage(); });
            PrevPageCommand          = new RelayCommand(() => { _currentPage--; UpdateAllDocumentsPage(); });
            SearchCommand            = new RelayCommand(() => ApplySortAndPage(resetPage: true));

            // 30초 주기 폴링 타이머 (자동 시작하지 않음)
            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _pollingTimer.Tick += OnPollingTick;
        }

        // ── 데이터 로드 ─────────────────────────────────────────────────────────
        /// <summary>
        /// 사용자 지정 후 최초 데이터 로드. 폴링도 함께 시작합니다.
        /// </summary>
        public async Task LoadDataAsync(User user)
        {
            if (user == null) return;
            _currentUser = user;
            OnPropertyChanged(nameof(IsApprover));

            await RefreshAsync();
            StartPolling();
        }

        /// <summary>
        /// 데이터를 갱신합니다. 이미 갱신 중이면 건너뜁니다.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (_isRefreshing || _currentUser == null) return;

            IsRefreshing = true;
            try
            {
                // ApprovalSystem 결재 상태 동기화 (연결 가능한 경우)
                await SyncApprovalStatusAsync();

                // 미동기화 문서 재시도 (ApprovalSystem 복구 시 자동 재등록)
                await SyncUnsyncedDocumentsAsync();

                // 결재 권한이 있는 사용자만 결재 대기 목록 조회
                if (IsApprover)
                {
                    var pending = await _approvalRepo.GetPendingApprovalsAsync(_currentUser.UserId);
                    PendingApprovals = new ObservableCollection<ApprovalDocument>(pending);
                }
                else
                {
                    PendingApprovals = new ObservableCollection<ApprovalDocument>();
                }

                var drafts = await _approvalRepo.GetMyDraftsAsync(_currentUser.UserId);
                MyDrafts = new ObservableCollection<SelectableDocument>(
                    drafts.Select(d => new SelectableDocument(d)));

                // 전체 문서 목록 (페이지네이션 적용)
                _allDocumentsRaw = await _approvalRepo.GetAllDocumentsAsync();
                ApplySortAndPage(resetPage: false);

                _lastRefreshed = DateTime.Now;
                OnPropertyChanged(nameof(LastRefreshedText));
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        // ── ApprovalSystem 상태 동기화 ──────────────────────────────────────────
        /// <summary>
        /// ApprovalSystem에서 내 요청 상태를 조회하여 FM_ApprovalDocuments를 동기화합니다.
        /// ApprovalSystem 미실행 시 조용히 건너뜁니다.
        /// </summary>
        private async Task SyncApprovalStatusAsync()
        {
            if (_currentUser == null) return;

            try
            {
                var response = await _approvalApiClient.GetMyRequestsAsync(_currentUser.UserId, pageSize: 50);
                if (response?.Data == null) return;

                // 완료된 결재만 처리 (Approved=1, Rejected=2)
                var completedApprovals = response.Data
                    .Where(a => a.Status == "Approved" || a.Status == "Rejected")
                    .ToList();

                foreach (var apiApproval in completedApprovals)
                {
                    if (string.IsNullOrEmpty(apiApproval.Id)) continue;

                    // ApprovalId로 FM 문서 찾기
                    var fmDoc = await _approvalRepo.GetDocumentByApprovalIdAsync(apiApproval.Id);
                    if (fmDoc == null || fmDoc.Status != ApprovalStatus.Pending) continue;

                    // FM 상태 업데이트
                    var newStatus = apiApproval.Status == "Approved"
                        ? ApprovalStatus.Approved
                        : ApprovalStatus.Rejected;

                    await _approvalRepo.UpdateDocumentStatusAsync(fmDoc.DocId, newStatus);
                }
            }
            catch
            {
                // ApprovalSystem 미실행 시 무시
            }
        }

        // ── 미동기화 문서 재등록 ────────────────────────────────────────────────
        /// <summary>
        /// ApprovalId 없는 미동기화 문서를 ApprovalSystem에 재등록합니다.
        /// ApprovalSystem 복구 후 첫 폴링 tick에서 자동 실행됩니다.
        /// 재시도 횟수가 3회 이상이면 건너뜁니다 (SyncStatus = Failed 유지).
        /// </summary>
        private async Task SyncUnsyncedDocumentsAsync()
        {
            try
            {
                var unsynced = await _approvalRepo.GetUnsyncedDocumentsAsync();
                if (!unsynced.Any()) return;

                foreach (var doc in unsynced)
                {
                    try
                    {
                        // 결재선 정보 포함한 전체 문서 로드 (ApproverIds 추출용)
                        var fullDoc = await _approvalRepo.GetDocumentAsync(doc.DocId);
                        var approverIds = fullDoc?.ApprovalLines
                            ?.Select(l => l.ApproverId)
                            .ToList() ?? new List<string>();

                        // 결재선이 없으면 CurrentApproverId를 fallback으로 사용
                        if (!approverIds.Any() && !string.IsNullOrEmpty(doc.CurrentApproverId))
                            approverIds.Add(doc.CurrentApproverId);

                        var apiRequest = new CreateApprovalRequest
                        {
                            Title = doc.Title,
                            RequesterId = doc.WriterId,
                            RequesterName = doc.WriterName,
                            ApproverIds = approverIds,
                            SourceApp = "FlowMaster",
                            SourceId = doc.DocId.ToString(),
                            Description = $"FlowMaster 테스트 결과 승인 요청 (문서 #{doc.DocId})"
                        };

                        var apiResponse = await _approvalApiClient.CreateApprovalAsync(apiRequest);
                        if (!string.IsNullOrEmpty(apiResponse?.Id))
                        {
                            await _approvalRepo.UpdateApprovalIdAsync(doc.DocId, apiResponse.Id);
                            await _approvalRepo.UpdateSyncStatusAsync(doc.DocId, SyncStatus.Synced, doc.SyncRetryCount, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 개별 문서 재시도 실패 → 재시도 횟수 증가, 다음 polling 때 재시도
                        await _approvalRepo.UpdateSyncStatusAsync(
                            doc.DocId, SyncStatus.Failed,
                            doc.SyncRetryCount + 1, ex.Message);
                    }
                }
            }
            catch
            {
                // ApprovalSystem 미실행 시 조용히 건너뜀
            }
        }

        // ── 폴링 제어 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 30초 주기 자동 갱신을 시작합니다.
        /// </summary>
        public void StartPolling()
        {
            if (!_pollingTimer.IsEnabled)
            {
                _pollingTimer.Start();
                OnPropertyChanged(nameof(IsPollingActive));
            }
        }

        /// <summary>
        /// 자동 갱신을 중지합니다. 다른 화면으로 이동할 때 호출하세요.
        /// </summary>
        public void StopPolling()
        {
            if (_pollingTimer.IsEnabled)
            {
                _pollingTimer.Stop();
                OnPropertyChanged(nameof(IsPollingActive));
            }
        }

        private async void OnPollingTick(object sender, EventArgs e)
        {
            await RefreshAsync();
        }

        // ── 내부 명령 핸들러 ────────────────────────────────────────────────────
        private void OpenDetail(ApprovalDocument doc)
        {
            if (doc != null)
                OnOpenDetailRequest?.Invoke(doc);
        }

        private async Task DeleteSelectedAsync()
        {
            var toDelete = MyDrafts.Where(d => d.IsSelected && d.CanSelect).ToList();
            if (!toDelete.Any())
            {
                MessageBox.Show("삭제할 문서를 선택해주세요.\n(승인완료 문서는 삭제할 수 없습니다.)",
                    "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"선택한 {toDelete.Count}개 문서를 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            int deleted = 0;
            foreach (var item in toDelete)
            {
                try
                {
                    await _approvalRepo.DeleteDocumentAsync(item.DocId);
                    deleted++;
                }
                catch { }
            }

            await RefreshAsync();
            MessageBox.Show($"{deleted}개 문서가 삭제되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── 전체 문서 정렬 / 페이지네이션 ──────────────────────────────────────
        public string CurrentSortColumn    => _sortColumn;
        public bool   CurrentSortAscending => _sortAscending;

        /// <summary>
        /// DataGrid Sorting 이벤트에서 호출. 같은 컬럼이면 방향 반전.
        /// </summary>
        public void SortByColumn(string column, bool ascending)
        {
            _sortColumn    = column;
            _sortAscending = ascending;
            ApplySortAndPage(resetPage: true);
        }

        private void ApplySortAndPage(bool resetPage)
        {
            IEnumerable<ApprovalDocument> q = _allDocumentsRaw;

            // 검색 필터
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var kw = _searchText.Trim();
                q = q.Where(d =>
                    (d.Title       != null && d.Title.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d.WriterName  != null && d.WriterName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (d.DocId.ToString().Contains(kw)));
            }

            switch (_sortColumn)
            {
                case "DocId":      q = _sortAscending ? q.OrderBy(d => d.DocId)      : q.OrderByDescending(d => d.DocId);      break;
                case "Title":      q = _sortAscending ? q.OrderBy(d => d.Title)      : q.OrderByDescending(d => d.Title);      break;
                case "WriterName": q = _sortAscending ? q.OrderBy(d => d.WriterName) : q.OrderByDescending(d => d.WriterName); break;
                case "StatusDisplay": q = _sortAscending ? q.OrderBy(d => d.Status)  : q.OrderByDescending(d => d.Status);     break;
                default:           q = _sortAscending ? q.OrderBy(d => d.CreateDate) : q.OrderByDescending(d => d.CreateDate); break;
            }
            _sortedDocuments = q.ToList();
            if (resetPage) _currentPage = 1;
            UpdateAllDocumentsPage();
        }

        private void UpdateAllDocumentsPage()
        {
            var page = _sortedDocuments
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize);
            AllDocuments = new ObservableCollection<ApprovalDocument>(page);
            NotifyPageState();
        }

        private void NotifyPageState()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageInfo));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrev));
        }

        // ── IDisposable ─────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (!_disposed)
            {
                StopPolling();
                _pollingTimer.Tick -= OnPollingTick;
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 내 문서 목록 다중 선택용 래퍼. 승인완료 문서는 체크박스 비활성.
    /// </summary>
    public class SelectableDocument : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public ApprovalDocument Document { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>승인완료 문서는 선택 불가</summary>
        public bool CanSelect => Document.Status != ApprovalStatus.Approved;

        // DataGrid 컬럼 바인딩 편의 프로퍼티
        public int    DocId               => Document.DocId;
        public string Title               => Document.Title;
        public string StatusDisplay       => GetStatusText(Document.Status);
        public System.DateTime CreateDate => Document.CreateDate;
        public string CurrentApproverName => Document.CurrentApproverName;

        public SelectableDocument(ApprovalDocument doc) { Document = doc; }

        private static string GetStatusText(ApprovalStatus status)
        {
            switch (status)
            {
                case ApprovalStatus.TempSaved: return "임시저장";
                case ApprovalStatus.Pending:   return "승인대기";
                case ApprovalStatus.Approved:  return "승인완료";
                case ApprovalStatus.Rejected:  return "반려";
                case ApprovalStatus.Canceled:  return "취소됨";
                default: return "작성중";
            }
        }
    }
}
