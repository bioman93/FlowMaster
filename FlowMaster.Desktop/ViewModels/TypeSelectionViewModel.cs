using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
        private readonly SqliteApprovalRepository _internalDb;
        private readonly Action<string, ApprovalDocument> _onTypeSelected;
        private readonly Action _onCancel;

        /// <summary>전체 문서 목록 (검색 소스)</summary>
        private List<ApprovalDocument> _allDocuments = new List<ApprovalDocument>();

        public ObservableCollection<ApprovalDocument> ExistingDocuments { get; } = new ObservableCollection<ApprovalDocument>();

        /// <summary>검색 결과 목록 (드롭다운 표시)</summary>
        public ObservableCollection<ApprovalDocument> CloneSearchResults { get; } = new ObservableCollection<ApprovalDocument>();

        private ApprovalDocument _selectedCloneSource;
        public ApprovalDocument SelectedCloneSource
        {
            get => _selectedCloneSource;
            set => SetProperty(ref _selectedCloneSource, value);
        }

        private string _cloneSearchText;
        /// <summary>복제 문서 검색 텍스트</summary>
        public string CloneSearchText
        {
            get => _cloneSearchText;
            set
            {
                if (SetProperty(ref _cloneSearchText, value))
                    ScheduleCloneSearch();
            }
        }

        private bool _isCloneSearchVisible;
        /// <summary>검색 결과 드롭다운 표시 여부</summary>
        public bool IsCloneSearchVisible
        {
            get => _isCloneSearchVisible;
            set => SetProperty(ref _isCloneSearchVisible, value);
        }

        private string _selectedCloneText;
        /// <summary>선택된 복제 문서 표시 텍스트</summary>
        public string SelectedCloneText
        {
            get => _selectedCloneText;
            set => SetProperty(ref _selectedCloneText, value);
        }

        /// <summary>검색 debounce용 CancellationTokenSource</summary>
        private CancellationTokenSource _searchCts;

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

        /// <summary>문서 목록이 하나 이상 있으면 복제 검색 활성화</summary>
        public bool HasDocuments => _allDocuments.Count > 0;

        public ICommand SelectTypeCommand { get; }
        public ICommand ConnectDbCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectCloneDocumentCommand { get; }
        public ICommand ClearCloneSelectionCommand { get; }

        public TypeSelectionViewModel(
            ExternalDbRepository externalDb,
            SqliteApprovalRepository internalDb,
            Action<string, ApprovalDocument> onTypeSelected,
            Action onCancel)
        {
            _externalDb = externalDb;
            _internalDb = internalDb;
            _onTypeSelected = onTypeSelected;
            _onCancel = onCancel;

            SelectTypeCommand = new RelayCommand<string>(OnTypeSelected);
            ConnectDbCommand = new AsyncRelayCommand(ConnectDbAsync);
            CancelCommand = new RelayCommand(() => _onCancel?.Invoke());
            SelectCloneDocumentCommand = new RelayCommand<ApprovalDocument>(OnCloneDocumentSelected);
            ClearCloneSelectionCommand = new RelayCommand(ClearCloneSelection);

            // 항상 내부 DB에서 문서 목록 로드 (외부 DB 연결 시 갱신)
            _ = LoadDocumentsAsync();

            if (_externalDb != null && _externalDb.IsConnected)
            {
                IsDbConnected = true;
                DbConnectionStatus = $"연결됨: {System.IO.Path.GetFileName(_externalDb.CurrentDbPath)}";
            }
        }

        private void OnTypeSelected(string tableType)
        {
            _onTypeSelected?.Invoke(tableType, SelectedCloneSource);
        }

        private void OnCloneDocumentSelected(ApprovalDocument doc)
        {
            if (doc == null) return;
            SelectedCloneSource = doc;
            SelectedCloneText = $"[{doc.DocId}] {doc.Title}";
            IsCloneSearchVisible = false;
            _cloneSearchText = "";
            OnPropertyChanged(nameof(CloneSearchText));

            // TableType이 이미 있으면 BA1/BA2 선택을 건너뛰고 바로 테스트 입력 UI로 진입
            if (!string.IsNullOrEmpty(doc.TableType))
            {
                _onTypeSelected?.Invoke(doc.TableType, doc);
            }
        }

        private void ClearCloneSelection()
        {
            SelectedCloneSource = null;
            SelectedCloneText = null;
            CloneSearchText = "";
            CloneSearchResults.Clear();
            IsCloneSearchVisible = false;
        }

        private void ScheduleCloneSearch()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            var searchText = CloneSearchText;

            Task.Delay(300, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested) return;
                Application.Current.Dispatcher.Invoke(() => RunCloneSearch(searchText));
            }, token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.Default);
        }

        private void RunCloneSearch(string searchText)
        {
            CloneSearchResults.Clear();
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
            {
                IsCloneSearchVisible = false;
                return;
            }

            var matches = _allDocuments
                .Where(d => (d.Title?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d.WriterName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d.DocId.ToString() == searchText))
                .Take(10);

            foreach (var d in matches) CloneSearchResults.Add(d);
            IsCloneSearchVisible = CloneSearchResults.Count > 0;
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
                List<ApprovalDocument> docs;
                // 외부 DB 연결 시 외부 DB 우선, 아니면 내부 DB에서 조회
                if (_externalDb != null && _externalDb.IsConnected)
                    docs = await _externalDb.GetAllDocumentsAsync();
                else
                    docs = await _internalDb.GetAllDocumentsAsync();

                _allDocuments = docs ?? new List<ApprovalDocument>();

                ExistingDocuments.Clear();
                foreach (var doc in _allDocuments)
                    ExistingDocuments.Add(doc);

                OnPropertyChanged(nameof(HasDocuments));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"문서 목록 로드 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
