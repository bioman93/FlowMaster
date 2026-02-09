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
using FlowMaster.Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FlowMaster.Desktop.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserRepository _userRepo;
        private readonly ExternalDbRepository _externalDb;

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

        public MainViewModel(IServiceProvider serviceProvider, IUserRepository userRepo, ExternalDbRepository externalDb)
        {
            _serviceProvider = serviceProvider;
            _userRepo = userRepo;
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

        private void LoadUsers()
        {
            // Assuming MockUserRepository has the helper method we added
            if (_userRepo is FlowMaster.Infrastructure.Services.MockUserRepository mockRepo)
            {
                AvailableUsers = mockRepo.GetAllTestUsers();
                CurrentUser = AvailableUsers.FirstOrDefault(); // Default to first user
            }
        }

        private async void NavigateToDashboard()
        {
            Title = "FlowMaster - Dashboard";
            var vm = _serviceProvider.GetRequiredService<DashboardViewModel>();
            vm.OnOpenDetailRequest = NavigateToDetail; // Subscribe
            await vm.LoadDataAsync(CurrentUser);
            CurrentView = vm; 
        }

        private void NavigateToDetail(ApprovalDocument doc)
        {
            Title = $"FlowMaster - {doc.Title}";
            var vm = _serviceProvider.GetRequiredService<DetailViewModel>();
            vm.Initialize(doc, CurrentUser, NavigateToDashboard); // Pass callback to go back
            CurrentView = vm;
        }

        private void NavigateToWrite()
        {
            Title = "FlowMaster - 결재 작성";
            var vm = _serviceProvider.GetRequiredService<WriteViewModel>();
            vm.SetWriter(CurrentUser);
            CurrentView = vm;
        }

        private void NavigateToTestInput()
        {
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
            var vm = new TestInputViewModel(_externalDb, NavigateToDashboard);
            await vm.InitializeNewDocumentAsync(tableType, cloneSource, CurrentUser, AvailableUsers);
            CurrentView = vm;
        }

        private void OnUserSwitched(User newUser)
        {
            if (newUser != null)
            {
                CurrentUser = newUser;
                // Reload current view to reflect permissions if needed
                NavigateToDashboard(); 
            }
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
