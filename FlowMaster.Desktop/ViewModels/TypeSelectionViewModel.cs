using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;
using Microsoft.Win32;

namespace FlowMaster.Desktop.ViewModels
{
    public class TypeSelectionViewModel : ObservableObject
    {
        private readonly ExternalDbRepository _externalDb;
        private readonly Action<string, ApprovalDocument> _onTypeSelected;
        private readonly Action _onCancel;

        public ObservableCollection<ApprovalDocument> ExistingDocuments { get; } = new ObservableCollection<ApprovalDocument>();

        private ApprovalDocument _selectedCloneSource;
        public ApprovalDocument SelectedCloneSource
        {
            get => _selectedCloneSource;
            set => SetProperty(ref _selectedCloneSource, value);
        }

        private bool _isDbConnected;
        public bool IsDbConnected
        {
            get => _isDbConnected;
            set => SetProperty(ref _isDbConnected, value);
        }

        private string _dbConnectionStatus = "DB 연결 안됨";
        public string DbConnectionStatus
        {
            get => _dbConnectionStatus;
            set => SetProperty(ref _dbConnectionStatus, value);
        }

        public ICommand SelectTypeCommand { get; }
        public ICommand ConnectDbCommand { get; }
        public ICommand CancelCommand { get; }

        public TypeSelectionViewModel(
            ExternalDbRepository externalDb,
            Action<string, ApprovalDocument> onTypeSelected,
            Action onCancel)
        {
            _externalDb = externalDb;
            _onTypeSelected = onTypeSelected;
            _onCancel = onCancel;

            SelectTypeCommand = new RelayCommand<string>(OnTypeSelected);
            ConnectDbCommand = new AsyncRelayCommand(ConnectDbAsync);
            CancelCommand = new RelayCommand(() => _onCancel?.Invoke());

            // 이미 연결된 경우 문서 목록 로드
            if (_externalDb.IsConnected)
            {
                IsDbConnected = true;
                DbConnectionStatus = $"연결됨: {System.IO.Path.GetFileName(_externalDb.CurrentDbPath)}";
                _ = LoadDocumentsAsync();
            }
        }

        private void OnTypeSelected(string tableType)
        {
            _onTypeSelected?.Invoke(tableType, SelectedCloneSource);
        }

        private async Task ConnectDbAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "외부 DB 파일 선택",
                Filter = "SQLite Database (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _externalDb.Connect(openFileDialog.FileName);
                    IsDbConnected = true;
                    DbConnectionStatus = $"연결됨: {System.IO.Path.GetFileName(openFileDialog.FileName)}";
                    await LoadDocumentsAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"DB 연결 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadDocumentsAsync()
        {
            try
            {
                var docs = await _externalDb.GetAllDocumentsAsync();
                ExistingDocuments.Clear();
                foreach (var doc in docs)
                {
                    ExistingDocuments.Add(doc);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"문서 목록 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
