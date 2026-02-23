using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        private ObservableCollection<ApprovalDocument> _myDrafts;
        public ObservableCollection<ApprovalDocument> MyDrafts
        {
            get => _myDrafts;
            set => SetProperty(ref _myDrafts, value);
        }

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

        // ── 명령 ────────────────────────────────────────────────────────────────
        public ICommand OpenDetailCommand { get; }
        public ICommand RefreshCommand { get; }
        public Action<ApprovalDocument> OnOpenDetailRequest;

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
            MyDrafts = new ObservableCollection<ApprovalDocument>();

            OpenDetailCommand = new RelayCommand<ApprovalDocument>(OpenDetail);
            RefreshCommand    = new AsyncRelayCommand(RefreshAsync);

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

                var pending = await _approvalRepo.GetPendingApprovalsAsync(_currentUser.UserId);
                PendingApprovals = new ObservableCollection<ApprovalDocument>(pending);

                var drafts = await _approvalRepo.GetMyDraftsAsync(_currentUser.UserId);
                MyDrafts = new ObservableCollection<ApprovalDocument>(drafts);

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
}
