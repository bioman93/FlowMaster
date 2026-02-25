using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Core.Services;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;
using FlowMaster.Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using FlowMaster.Desktop.Services;
#pragma warning disable CS4014 // 비동기 호출 결과 미대기 (async void 의도적 사용)

namespace FlowMaster.Desktop.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserRepository _userRepo;
        private readonly IAuthService _authService;
        private readonly ExternalDbRepository _externalDb;

        // 현재 대시보드 VM 참조 (폴링 제어용)
        private DashboardViewModel _currentDashboardVm;

        // 네비게이션 시퀀스: async 네비게이션이 중첩될 때 마지막 요청만 적용하기 위한 카운터
        private int _navSeq = 0;

        // 초기화 완료 플래그: LoadUsersDataAsync 완료 전까지 CurrentUser setter → OnUserSwitched 호출 차단
        private bool _initialized = false;

        // Current User State
        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            set
            {
                if (SetProperty(ref _currentUser, value))
                {
                    OnPropertyChanged(nameof(IsAdminMenuVisible));
                    // 초기화 완료 후에만 호출 (초기화 중에는 race condition 유발)
                    if (_initialized) OnUserSwitched(value);
                }
            }
        }

        private List<User> _availableUsers;
        public List<User> AvailableUsers
        {
            get => _availableUsers;
            set
            {
                if (SetProperty(ref _availableUsers, value))
                {
                    OnPropertyChanged(nameof(IsAdminMenuVisible));
                }
            }
        }

        // Admin 역할 사용자가 없으면 true (최초 실행 등)
        private bool _hasNoAdmin = true;
        public bool HasNoAdmin
        {
            get => _hasNoAdmin;
            set
            {
                if (SetProperty(ref _hasNoAdmin, value))
                    OnPropertyChanged(nameof(IsAdminMenuVisible));
            }
        }

        /// <summary>
        /// 관리자 메뉴 표시 여부.
        /// 현재 사용자가 Admin이거나, FM_AppUsers에 Admin이 한 명도 없을 때 표시.
        /// </summary>
        public bool IsAdminMenuVisible =>
            CurrentUser?.Role == UserRole.Admin || HasNoAdmin;

        // Navigation State
        private object _currentView;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _title = "FlowMaster";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        // Commands
        public ICommand NavigateDashboardCommand { get; }
        public ICommand NavigateWriteCommand { get; }
        public ICommand NavigateTestInputCommand { get; }
        public ICommand NavigateAdminCommand { get; }
        public ICommand SwitchUserCommand { get; }
        public ICommand ExtractSchemaCommand { get; }

        public MainViewModel(IServiceProvider serviceProvider, IUserRepository userRepo,
            IAuthService authService, ExternalDbRepository externalDb)
        {
            _serviceProvider = serviceProvider;
            _userRepo = userRepo;
            _authService = authService;
            _externalDb = externalDb;

            NavigateDashboardCommand = new RelayCommand(NavigateToDashboard);
            NavigateWriteCommand = new RelayCommand(NavigateToWrite);
            NavigateTestInputCommand = new RelayCommand(NavigateToTestInput);
            NavigateAdminCommand = new RelayCommand(NavigateToAdmin);
            SwitchUserCommand = new RelayCommand<User>(OnUserSwitched);
            ExtractSchemaCommand = new RelayCommand(ExtractDbSchema);

            // 로컬 DB 빠른 체크 → 즉시 초기 화면 결정 후 전체 데이터 백그라운드 로드
            StartupAsync();
        }

        /// <summary>
        /// Phase 1: 즉시 대시보드로 이동 (API 기반으로 로컬 즉시 체크 불가)
        /// Phase 2: 전체 사용자 데이터 로드 (Emulator/AD, 백그라운드)
        /// Phase 3: Emulator 미실행 + 등록 사용자 없으면 시스템 관리 화면으로
        /// </summary>
        private async void StartupAsync()
        {
            // Phase 1: 즉시 대시보드 표시
            NavigateToDashboard();

            // Phase 2: 전체 사용자 데이터 로드 (Emulator/AD, 백그라운드)
            await LoadUsersDataAsync();

            // Phase 3: Emulator 미실행이고 등록 사용자가 없으면 → 시스템 관리 화면
            if (!_authService.IsEmulatorAvailable && (AvailableUsers == null || !AvailableUsers.Any()))
            {
                HasNoAdmin = true;
                AppLogger.Info("[MainViewModel] 등록 사용자 없음 → 시스템 관리 화면으로 이동");
                NavigateToAdmin();
            }
        }

        /// <summary>
        /// 전체 사용자 데이터 로드 (Emulator/AD 포함). 네비게이션 없음.
        /// StartupAsync()의 Phase 2에서 백그라운드로 호출됨.
        /// </summary>
        private async Task LoadUsersDataAsync()
        {
            AppLogger.Info("[MainViewModel] 사용자 목록 로드 시작");
            try
            {
                AvailableUsers = await _userRepo.GetAllUsersAsync();
                AppLogger.Info($"[MainViewModel] 사용자 목록 로드 완료: {AvailableUsers?.Count ?? 0}명");
                if (AvailableUsers != null)
                    foreach (var u in AvailableUsers)
                        AppLogger.Info($"[MainViewModel]   - {u.AdAccount} / {u.Name} / {u.Role}");

                HasNoAdmin = AvailableUsers == null || !AvailableUsers.Any(u => u.Role == UserRole.Admin);
                AppLogger.Info($"[MainViewModel] HasNoAdmin = {HasNoAdmin}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[MainViewModel] 사용자 목록 로드 실패", ex);
                AvailableUsers = new List<User>();
                HasNoAdmin = true;
            }

            try
            {
                AppLogger.Info("[MainViewModel] 현재 사용자 조회 시작");
                var contextUser = await _authService.GetCurrentContextUserAsync();
                if (contextUser != null)
                {
                    AppLogger.Info($"[MainViewModel] 컨텍스트 사용자: {contextUser.AdAccount} / {contextUser.Name}");
                    var registeredUser = AvailableUsers.FirstOrDefault(u =>
                        string.Equals(u.AdAccount, contextUser.AdAccount, StringComparison.OrdinalIgnoreCase));
                    CurrentUser = registeredUser ?? contextUser;
                }
                else
                {
                    AppLogger.Warn("[MainViewModel] 컨텍스트 사용자 없음 → 첫 번째 사용자로 설정");
                    CurrentUser = AvailableUsers.FirstOrDefault();
                }
                AppLogger.Info($"[MainViewModel] 최종 CurrentUser: {CurrentUser?.AdAccount ?? "없음"} / {CurrentUser?.Role}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[MainViewModel] 현재 사용자 설정 실패", ex);
                CurrentUser = AvailableUsers.FirstOrDefault();
            }

            // 초기 인증 토큰 설정 (Emulator 연결 시 필요, AD 모드에서는 null 반환)
            if (CurrentUser != null)
            {
                var token = await _authService.LoginAsync(CurrentUser.AdAccount);
                _serviceProvider.GetRequiredService<ApprovalApiClient>().SetAuthToken(token);
            }

            // CurrentUser가 AvailableUsers에 없으면 추가
            // (로컬/비도메인 계정: FM_AppUsers 비어있어도 현재 사용자는 ComboBox에 표시)
            if (CurrentUser != null && AvailableUsers != null &&
                !AvailableUsers.Any(u => string.Equals(u.AdAccount, CurrentUser.AdAccount,
                    StringComparison.OrdinalIgnoreCase)))
            {
                AvailableUsers = new List<User>(AvailableUsers) { CurrentUser };
                AppLogger.Info($"[MainViewModel] CurrentUser '{CurrentUser.AdAccount}'를 AvailableUsers에 추가");
            }

            // 초기화 완료: 이후 CurrentUser 변경은 OnUserSwitched(전환 + 네비게이션) 호출
            _initialized = true;
        }

        private async void NavigateToDashboard()
        {
            int seq = ++_navSeq;

            // 이전 대시보드 VM의 폴링 중지 후 폐기
            _currentDashboardVm?.StopPolling();
            _currentDashboardVm?.Dispose();

            Title = "FlowMaster - Dashboard";
            var vm = _serviceProvider.GetRequiredService<DashboardViewModel>();
            vm.OnOpenDetailRequest = NavigateToDetail;
            _currentDashboardVm = vm;

            await vm.LoadDataAsync(CurrentUser); // 데이터 로드 + 폴링 시작

            if (seq != _navSeq) return; // 더 최신 네비게이션 요청이 있으면 포기
            CurrentView = vm;
        }

        private async void NavigateToDetail(ApprovalDocument doc)
        {
            int seq = ++_navSeq;
            _currentDashboardVm?.StopPolling(); // 상세 화면 이동 시 폴링 중지

            // BA1/BA2 문서는 TestInputView로 이동
            if (doc.TableType == "BA1" || doc.TableType == "BA2")
            {
                Title = $"FlowMaster - {doc.TableType} 테스트 입력";
                var internalDb = _serviceProvider.GetRequiredService<IApprovalRepository>();
                var apiClient = _serviceProvider.GetRequiredService<ApprovalApiClient>();
                var vm = new TestInputViewModel(_externalDb, internalDb, apiClient, NavigateToDashboard);
                await vm.InitializeExistingDocumentAsync(doc.DocId, CurrentUser, AvailableUsers);
                if (seq != _navSeq) return;
                CurrentView = vm;
            }
            else
            {
                // 일반 결재 문서는 DetailView로 이동
                Title = $"FlowMaster - {doc.Title}";
                var vm = _serviceProvider.GetRequiredService<DetailViewModel>();
                vm.Initialize(doc, CurrentUser, NavigateToDashboard);
                CurrentView = vm;
            }
        }

        private void NavigateToWrite()
        {
            _currentDashboardVm?.StopPolling(); // 대시보드 이탈 시 폴링 중지
            Title = "FlowMaster - 결재 작성";
            var vm = _serviceProvider.GetRequiredService<WriteViewModel>();
            vm.SetWriter(CurrentUser);
            CurrentView = vm;
        }

        private async void NavigateToAdmin()
        {
            int seq = ++_navSeq;
            _currentDashboardVm?.StopPolling();
            Title = "FlowMaster - 시스템 관리";
            var vm = _serviceProvider.GetRequiredService<AdminViewModel>();
            vm.GoBack = NavigateToDashboard;
            await vm.InitializeAsync();
            if (seq != _navSeq) return; // 더 최신 네비게이션 요청이 있으면 포기
            CurrentView = vm;
        }

        private void NavigateToTestInput()
        {
            _currentDashboardVm?.StopPolling(); // 대시보드 이탈 시 폴링 중지
            Title = "FlowMaster - 테스트 결과 입력";
            var internalDbForType = _serviceProvider.GetRequiredService<IApprovalRepository>();
            var vm = new TypeSelectionViewModel(
                _externalDb,
                internalDbForType,
                OnTypeSelected,
                NavigateToDashboard
            );
            CurrentView = vm;
        }

        private async void OnTypeSelected(string tableType, ApprovalDocument cloneSource)
        {
            int seq = ++_navSeq;
            Title = $"FlowMaster - {tableType} 테스트 입력";
            var internalDb = _serviceProvider.GetRequiredService<IApprovalRepository>();
            var apiClient = _serviceProvider.GetRequiredService<ApprovalApiClient>();
            var vm = new TestInputViewModel(_externalDb, internalDb, apiClient, NavigateToDashboard);
            await vm.InitializeNewDocumentAsync(tableType, cloneSource, CurrentUser, AvailableUsers);
            if (seq != _navSeq) return;
            CurrentView = vm;
        }

        private async void OnUserSwitched(User newUser)
        {
            if (newUser == null) return;

            // Emulator 실행 중이면 JWT 발급 후 ApprovalApiClient에 전달
            // 미실행이면 null 반환 → 무인증 모드로 계속 동작
            var token = await _authService.LoginAsync(newUser.AdAccount);
            var apiClient = _serviceProvider.GetRequiredService<ApprovalApiClient>();
            apiClient.SetAuthToken(token);

            // async 이후 WPF 바인딩이 CurrentUser.Role 재평가를 놓칠 수 있으므로 명시적 알림
            OnPropertyChanged(nameof(CurrentUser));

            NavigateToDashboard();
        }

        private void ExtractDbSchema()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "분석할 SQLite DB 파일 선택",
                Filter = "SQLite Database (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var outputPath = DbSchemaExtractor.ExtractSchema(openFileDialog.FileName);
                    MessageBox.Show($"스키마 추출 완료!\n\n출력 파일:\n{outputPath}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 출력 파일 열기
                    System.Diagnostics.Process.Start("notepad.exe", outputPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"스키마 추출 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
