using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Core.Services;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FlowMaster.Desktop.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserRepository _userRepo;

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
        public ICommand SwitchUserCommand { get; }

        public MainViewModel(IServiceProvider serviceProvider, IUserRepository userRepo)
        {
            _serviceProvider = serviceProvider;
            _userRepo = userRepo;

            // Load Test Users
            LoadUsers();

            // Set Initial View
            NavigateDashboardCommand = new RelayCommand(NavigateToDashboard);
            NavigateWriteCommand = new RelayCommand(NavigateToWrite);
            SwitchUserCommand = new RelayCommand<User>(OnUserSwitched);

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

        private void OnUserSwitched(User newUser)
        {
            if (newUser != null)
            {
                CurrentUser = newUser;
                // Reload current view to reflect permissions if needed
                NavigateToDashboard(); 
            }
        }
    }
}
