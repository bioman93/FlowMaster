using System;
using System.Collections.Generic;
using System.Linq;
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

        // Current User State
        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            set
            {
                if (SetProperty(ref _currentUser, value))
                {
                    OnUserSwitched(value);
                }
            }
        }

        private List<User> _availableUsers;
        public List<User> AvailableUsers
        {
            get => _availableUsers;
            set => SetProperty(ref _availableUsers, value);
        }

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
        public ICommand SwitchUserCommand { get; }
        public ICommand ExtractSchemaCommand { get; }

        public MainViewModel(IServiceProvider serviceProvider, IUserRepository userRepo,
            IAuthService authService, ExternalDbRepository externalDb)
        {
            _serviceProvider = serviceProvider;
            _userRepo = userRepo;
            _authService = authService;
            _externalDb = externalDb;

            // Load Test Users
            LoadUsers();

            // Set Initial View
            NavigateDashboardCommand = new RelayCommand(NavigateToDashboard);
            NavigateWriteCommand = new RelayCommand(NavigateToWrite);
            NavigateTestInputCommand = new RelayCommand(NavigateToTestInput);
            SwitchUserCommand = new RelayCommand<User>(OnUserSwitched);
            ExtractSchemaCommand = new RelayCommand(ExtractDbSchema);

            NavigateToDashboard();
        }

        private async void LoadUsers()
        {
            // Emulator 실행 시 AD 사용자 목록 로드, 미실행 시 Mock 4명 폴백
            AvailableUsers = await _userRepo.GetAllUsersAsync();

            // Emulator Current Context 사용자를 초기 선택 (대시보드 선택과 동기화)
            // Emulator 미실행이면 null 반환 → FirstOrDefault 폴백
            var contextUser = await _authService.GetCurrentContextUserAsync();
            if (contextUser != null)
                CurrentUser = AvailableUsers.FirstOrDefault(u => u.AdAccount == contextUser.AdAccount)
                              ?? AvailableUsers.FirstOrDefault();
            else
                CurrentUser = AvailableUsers.FirstOrDefault();
        }

        private async void NavigateToDashboard()
        {
            // 이전 대시보드 VM의 폴링 중지 후 폐기
            _currentDashboardVm?.StopPolling();
            _currentDashboardVm?.Dispose();

            Title = "FlowMaster - Dashboard";
            var vm = _serviceProvider.GetRequiredService<DashboardViewModel>();
            vm.OnOpenDetailRequest = NavigateToDetail;
            _currentDashboardVm = vm;

            await vm.LoadDataAsync(CurrentUser); // 데이터 로드 + 폴링 시작
            CurrentView = vm;
        }

        private async void NavigateToDetail(ApprovalDocument doc)
        {
            _currentDashboardVm?.StopPolling(); // 상세 화면 이동 시 폴링 중지

            // BA1/BA2 문서는 TestInputView로 이동
            if (doc.TableType == "BA1" || doc.TableType == "BA2")
            {
                Title = $"FlowMaster - {doc.TableType} 테스트 입력";
                var internalDb = _serviceProvider.GetRequiredService<SqliteApprovalRepository>();
                var apiClient = _serviceProvider.GetRequiredService<ApprovalApiClient>();
                var vm = new TestInputViewModel(_externalDb, internalDb, apiClient, NavigateToDashboard);
                await vm.InitializeExistingDocumentAsync(doc.DocId, CurrentUser, AvailableUsers);
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

        private void NavigateToTestInput()
        {
            _currentDashboardVm?.StopPolling(); // 대시보드 이탈 시 폴링 중지
            Title = "FlowMaster - 테스트 결과 입력";
            var vm = new TypeSelectionViewModel(
                _externalDb,
                OnTypeSelected,
                NavigateToDashboard
            );
            CurrentView = vm;
        }

        private async void OnTypeSelected(string tableType, ApprovalDocument cloneSource)
        {
            Title = $"FlowMaster - {tableType} 테스트 입력";
            var internalDb = _serviceProvider.GetRequiredService<SqliteApprovalRepository>();
            var apiClient = _serviceProvider.GetRequiredService<ApprovalApiClient>();
            var vm = new TestInputViewModel(_externalDb, internalDb, apiClient, NavigateToDashboard);
            await vm.InitializeNewDocumentAsync(tableType, cloneSource, CurrentUser, AvailableUsers);
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
