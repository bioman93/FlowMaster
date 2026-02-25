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
using FlowMaster.Domain.DTOs;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Desktop.ViewModels
{
    public class TestInputViewModel : ObservableObject
    {
        private readonly ExternalDbRepository _externalDb;
        private readonly IApprovalRepository _internalDb;
        private readonly ApprovalApiClient _apiClient;
        private readonly Action _onGoBack;
        private User _currentUser;
        private bool _isNewDocument;
        private bool _useInternalDb; // 내부 DB 사용 여부
        private string _approvalId; // API 결재 ID (APV-xxx)
        private string _writerId;   // 문서 작성자 ID (취소 권한 확인)
        private CancellationTokenSource _versionSuggestionCts;

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
            set { if (SetProperty(ref _tableType, value)) UpdateAutoTitle(); }
        }

        private string _genType;
        public string GenType
        {
            get => _genType;
            set
            {
                if (SetProperty(ref _genType, value))
                {
                    // 3세대 이상 선택 시 자동으로 GDI
                    if (value == "3세대") InjType = "GDI";
                    OnPropertyChanged(nameof(CanSelectInjType));
                }
            }
        }

        private string _injType;
        public string InjType
        {
            get => _injType;
            set
            {
                if (SetProperty(ref _injType, value) && !string.IsNullOrEmpty(value))
                    _ = LoadParticipantGroupAsync(value); // MPI/GDI 그룹 자동 추가
            }
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

        private string _version;
        public string Version
        {
            get => _version;
            set
            {
                if (SetProperty(ref _version, value))
                {
                    UpdateAutoTitle();
                    OnPropertyChanged(nameof(IsVersionFilled));
                    OnPropertyChanged(nameof(CanRequestApproval));
                    OnPropertyChanged(nameof(CanSubmit));
                    if (value != null && value.Length >= 4)
                        ScheduleVersionSuggestion(value);
                    else
                        IsVersionSuggestionsVisible = false;
                }
            }
        }

        private string _outputPath;
        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
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
            set
            {
                if (SetProperty(ref _selectedApprover, value))
                    OnPropertyChanged(nameof(CanRequestApproval));
            }
        }

        public List<User> AvailableApprovers { get; set; } = new List<User>();

        public ObservableCollection<ChecklistItem> ChecklistItems { get; } = new ObservableCollection<ChecklistItem>();

        public int ChecklistItemCount => ChecklistItems.Count;

        // Visibility helpers
        public Visibility ShowDescription => TableType == "BA2" ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>승인완료 또는 반려 후 코멘트 표시 (읽기 전용)</summary>
        public Visibility ShowApproverComment => (StatusText == "승인완료" || StatusText == "반려") && !string.IsNullOrEmpty(ApproverComment) ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>결재자가 승인 대기 문서 열었을 때 코멘트 입력란 표시</summary>
        public Visibility ShowApproverInput => CanApprove == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CanApprove => StatusText == "승인대기" && _currentUser?.Role == UserRole.Approver ? Visibility.Visible : Visibility.Collapsed;

        // 승인완료만 수정 불가 (반려는 수정/삭제 허용)
        public bool IsReadOnly => StatusText == "승인완료";
        public bool CanEdit => !IsReadOnly;
        /// <summary>임시저장: 승인완료/반려 외에 표시. 반려는 결재요청 버튼만 표시</summary>
        public Visibility CanSave => (IsReadOnly || StatusText == "반려") ? Visibility.Collapsed : Visibility.Visible;
        /// <summary>결재 요청: 작성중/임시저장/반려 상태에서 표시</summary>
        public Visibility CanSubmit => (StatusText == "작성중" || StatusText == "임시저장" || StatusText == "반려" || _isNewDocument) ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>버전이 입력된 경우에만 결재 요청 버튼 활성화</summary>
        public bool IsVersionFilled => !string.IsNullOrWhiteSpace(Version);
        /// <summary>결재자 선택 + 버전 입력 모두 완료된 경우에만 결재 요청 버튼 활성화</summary>
        public bool CanRequestApproval => SelectedApprover != null && IsVersionFilled;
        /// <summary>결재 처리 일시 표시 (승인완료 또는 반려)</summary>
        public bool ShowApprovalDate => StatusText == "승인완료" || StatusText == "반려";
        /// <summary>승인완료 여부 (하위 호환)</summary>
        public bool IsApproved => StatusText == "승인완료";
        /// <summary>반려된 문서는 삭제 가능</summary>
        public Visibility CanDelete => StatusText == "반려" ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>3세대 선택 시 유형 비활성 (자동 GDI)</summary>
        public bool CanSelectInjType => CanEdit && GenType != "3세대";
        /// <summary>임시저장 버튼 텍스트: 승인대기 중이면 "수정", 나머지는 "임시저장"</summary>
        public string SaveButtonText => StatusText == "승인대기" ? "수정" : "임시저장";
        /// <summary>결재 요청 취소 버튼: 승인대기 상태에서 원작성자에게만 표시</summary>
        public Visibility CanCancel => StatusText == "승인대기" && _currentUser?.UserId == _writerId
            ? Visibility.Visible : Visibility.Collapsed;

        // ── 버전 자동완성 ───────────────────────────────────────────────
        public ObservableCollection<string> VersionSuggestions { get; } = new ObservableCollection<string>();

        private bool _isVersionSuggestionsVisible;
        public bool IsVersionSuggestionsVisible
        {
            get => _isVersionSuggestionsVisible;
            set => SetProperty(ref _isVersionSuggestionsVisible, value);
        }

        public ICommand SelectVersionSuggestionCommand { get; }
        /// <summary>버전 제안 선택 후 View에서 TextBox 포커스를 되돌리기 위한 콜백</summary>
        public Action AfterVersionSelected { get; set; }

        // ── 참여자(Watcher) ────────────────────────────────────────────
        public ObservableCollection<User> Participants { get; } = new ObservableCollection<User>();
        public ObservableCollection<User> ParticipantSearchResults { get; } = new ObservableCollection<User>();

        private string _participantSearchText;
        public string ParticipantSearchText
        {
            get => _participantSearchText;
            set
            {
                if (SetProperty(ref _participantSearchText, value))
                    ScheduleParticipantSearch();
            }
        }

        private bool _isParticipantSearchVisible;
        public bool IsParticipantSearchVisible
        {
            get => _isParticipantSearchVisible;
            set => SetProperty(ref _isParticipantSearchVisible, value);
        }

        /// <summary>참여자 검색 debounce용 CT</summary>
        private CancellationTokenSource _participantSearchCts;

        /// <summary>모든 사용자 목록 (검색 소스)</summary>
        private List<User> _allUsers = new List<User>();

        /// <summary>그룹 자동 추가된 참여자 ID 추적 (InjType 변경 시 교체)</summary>
        private HashSet<string> _groupParticipantIds = new HashSet<string>();

        #endregion

        #region Commands

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand CancelSubmitCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand AddParticipantCommand { get; }
        public ICommand RemoveParticipantCommand { get; }

        #endregion

        public TestInputViewModel(ExternalDbRepository externalDb, IApprovalRepository internalDb, ApprovalApiClient apiClient, Action onGoBack)
        {
            _externalDb = externalDb;
            _internalDb = internalDb;
            _apiClient = apiClient;
            _onGoBack = onGoBack;

            GoBackCommand = new RelayCommand(() => _onGoBack?.Invoke());
            SaveCommand = new AsyncRelayCommand(SaveAsync);
            SubmitCommand = new AsyncRelayCommand(SubmitAsync);
            CancelSubmitCommand = new AsyncRelayCommand(CancelSubmitAsync);
            ApproveCommand = new AsyncRelayCommand(ApproveAsync);
            RejectCommand = new AsyncRelayCommand(RejectAsync);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync);
            AddParticipantCommand = new RelayCommand<User>(AddParticipant);
            RemoveParticipantCommand = new RelayCommand<User>(RemoveParticipant);
            SelectVersionSuggestionCommand = new RelayCommand<string>(v =>
            {
                Version = v;
                IsVersionSuggestionsVisible = false;
                AfterVersionSelected?.Invoke();
            });
        }

        /// <summary>
        /// 새 문서 생성 모드로 초기화
        /// </summary>
        public async Task InitializeNewDocumentAsync(string tableType, ApprovalDocument cloneSource, User currentUser, List<User> approvers)
        {
            _isNewDocument = true;
            _currentUser = currentUser;
            _writerId = currentUser?.UserId;
            _allUsers = approvers ?? new List<User>();
            // 결재자 목록: Approver/Admin 역할만 표시
            AvailableApprovers = _allUsers
                .Where(u => u.Role == UserRole.Approver || u.Role == UserRole.Admin)
                .ToList();

            TableType = tableType;
            WriterName = currentUser?.Name ?? "";
            Title = $"새 {tableType} 테스트 문서";
            StatusText = "작성중";
            StatusColor = "#666";

            ChecklistItems.Clear();

            if (cloneSource != null)
            {
                // 외부 DB 연결 시 외부 DB에서, 아니면 내부 DB에서 체크리스트 복제
                List<ChecklistItem> clonedItems;
                if (_externalDb != null && _externalDb.IsConnected)
                {
                    clonedItems = await _externalDb.CloneChecklistFromDocumentAsync(cloneSource.DocId);
                }
                else
                {
                    clonedItems = await _internalDb.GetChecklistItemsAsync(cloneSource.DocId);
                }

                foreach (var item in clonedItems)
                {
                    item.ItemId = 0; // 새 문서용으로 ID 초기화
                    item.DocId = 0;
                    ChecklistItems.Add(item);
                }

                // 메타데이터 복제
                Title = cloneSource.Title + " (복사본)";
                GenType = cloneSource.GenType;
                InjType = cloneSource.InjType;
                Description = cloneSource.Description;

                SubscribeChecklistEvents();
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
            _allUsers = approvers ?? new List<User>();
            // 결재자 목록: Approver/Admin 역할만 표시
            AvailableApprovers = _allUsers
                .Where(u => u.Role == UserRole.Approver || u.Role == UserRole.Admin)
                .ToList();

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
            _writerId = doc.WriterId;
            ApprovalDate = doc.ApprovalTime?.ToString("yyyy-MM-dd HH:mm") ?? "";
            Version = doc.Version;
            OutputPath = doc.OutputPath;
            StatusText = GetStatusText(doc.Status);
            StatusColor = GetStatusColor(StatusText);

            // 결재자 매칭: CurrentApproverId → AvailableApprovers에서 찾아 설정 (Issue 3)
            SelectedApprover = AvailableApprovers.FirstOrDefault(u =>
                u.UserId == doc.CurrentApproverId ||
                u.AdAccount == doc.CurrentApproverId);

            ChecklistItems.Clear();
            if (doc.ChecklistItems != null)
            {
                foreach (var item in doc.ChecklistItems)
                    ChecklistItems.Add(item);
            }
            SubscribeChecklistEvents();

            // 참여자 로드
            Participants.Clear();
            var savedParticipants = await _internalDb.GetDocumentParticipantsAsync(docId);
            foreach (var p in savedParticipants) Participants.Add(p);

            OnPropertyChanged(nameof(ChecklistItemCount));
            OnPropertyChanged(nameof(ShowDescription));
            OnPropertyChanged(nameof(ShowApproverComment));
            OnPropertyChanged(nameof(ShowApproverInput));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(IsApproved));
            OnPropertyChanged(nameof(ShowApprovalDate));
            OnPropertyChanged(nameof(IsVersionFilled));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanSelectInjType));
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(CanCancel));
        }

        // 평가코드 가중치 (낮을수록 우선순위 낮음 = 집계 시 선택됨)
        // 인덱스 0 = 최저(nb), 인덱스 4 = 최고(+)
        private static readonly string[] EvalOrder = { "nb", "-", "(-)", "(+)", "+" };

        private void LoadDefaultChecklist(string tableType)
        {
            var items = GetDefaultChecklistItems(tableType);
            foreach (var item in items)
                ChecklistItems.Add(item);

            SubscribeChecklistEvents();
        }

        /// <summary>전체 평가 결과: 모든 헤더 항목의 최저 가중치 코드. 미완성이면 null.</summary>
        public string OverallEvaluationCode
        {
            get
            {
                var headers = ChecklistItems.Where(item => item.IsHeader).ToList();
                if (headers.Count == 0 || headers.Any(h => string.IsNullOrEmpty(h.EvaluationCode)))
                    return null;
                return AggregateEvalCodes(headers.Select(h => h.EvaluationCode).ToList());
            }
        }

        /// <summary>전체 평가 결과가 있으면 표시 (모든 메인항목 채워진 경우)</summary>
        public bool ShowOverallEvaluation => !string.IsNullOrEmpty(OverallEvaluationCode);

        /// <summary>평가 결과 배경색 (EvalOrder 기반)</summary>
        public string OverallEvaluationColor
        {
            get
            {
                switch (OverallEvaluationCode)
                {
                    case "+":   return "#4CAF50"; // 녹색
                    case "(+)": return "#8BC34A"; // 연녹
                    case "(-)": return "#FF9800"; // 주황
                    case "-":   return "#f44336"; // 빨강
                    case "nb":  return "#9E9E9E"; // 회색
                    default:    return "#666";
                }
            }
        }

        /// <summary>체크리스트 항목 PropertyChanged 전체 구독 (서브항목 → 헤더 집계, 헤더 → 전체평가 갱신)</summary>
        private void SubscribeChecklistEvents()
        {
            foreach (var item in ChecklistItems)
                item.PropertyChanged += OnChecklistItemPropertyChanged;
        }

        /// <summary>EvaluationCode 변경 이벤트 처리</summary>
        private void OnChecklistItemPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ChecklistItem.EvaluationCode)) return;
            if (!(sender is ChecklistItem changedItem)) return;

            if (changedItem.IsHeader)
            {
                // 헤더 EvaluationCode 변경 → 전체 평가결과 갱신
                OnPropertyChanged(nameof(OverallEvaluationCode));
                OnPropertyChanged(nameof(ShowOverallEvaluation));
                OnPropertyChanged(nameof(OverallEvaluationColor));
                return;
            }

            // 서브항목 변경 → 부모 헤더 RowNo 추출
            var dotIndex = changedItem.RowNo?.IndexOf('.') ?? -1;
            if (dotIndex < 1) return;
            var parentRowNo = changedItem.RowNo.Substring(0, dotIndex);

            var header = null as ChecklistItem;
            foreach (var item in ChecklistItems)
            {
                if (item.IsHeader && item.RowNo == parentRowNo) { header = item; break; }
            }
            if (header == null) return;

            // 해당 헤더 아래 모든 서브항목 수집
            var subItems = new List<ChecklistItem>();
            foreach (var item in ChecklistItems)
            {
                if (!item.IsHeader && item.RowNo != null && item.RowNo.StartsWith(parentRowNo + "."))
                    subItems.Add(item);
            }

            // 모든 서브항목이 채워져야만 헤더 집계 (하나라도 비어있으면 헤더 초기화)
            if (subItems.Count == 0 || subItems.Any(item => string.IsNullOrEmpty(item.EvaluationCode)))
            {
                header.EvaluationCode = null;
                return;
            }

            header.EvaluationCode = AggregateEvalCodes(subItems.Select(item => item.EvaluationCode).ToList());
        }

        /// <summary>
        /// 가중치 기반 집계: 가중치가 가장 낮은 코드 반환.
        /// 모두 비어있으면 null. 모두 "+"이면 "+".
        /// </summary>
        private static string AggregateEvalCodes(List<string> codes)
        {
            if (codes == null || codes.Count == 0) return null;

            int minIndex = int.MaxValue;
            string result = null;
            foreach (var code in codes)
            {
                var idx = System.Array.IndexOf(EvalOrder, code);
                if (idx < 0) continue;
                if (idx < minIndex) { minIndex = idx; result = code; }
            }
            return result;
        }

        private List<ChecklistItem> GetDefaultChecklistItems(string tableType)
        {
            var items = new List<ChecklistItem>();
            int order = 1;

            if (tableType == "BA1")
            {
                // ── 1. 프로젝트 관리 ──────────────────────────────
                items.Add(new ChecklistItem { RowNo = "1",   CheckItem = "프로젝트 관리",                                       DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.1", CheckItem = "SW 배포 일자 수립 (내부)",                             DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.2", CheckItem = "업무 계획 및 인적자원 할당",                           DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.3", CheckItem = "위험 요소 평가",                                       DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.4", CheckItem = "위험 요소와 배포 일자 고객에게 전달",                  DisplayOrder = order++ });
                // ── 2. 기능(Module) 요구사항 ─────────────────────
                items.Add(new ChecklistItem { RowNo = "2",   CheckItem = "기능(Module) 요구사항",                                DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.1", CheckItem = "기능(Module) 요구사항 접수/분석",                      DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.2", CheckItem = "기능(Module) 요구사항 개발 완료 여부 검토",            DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.3", CheckItem = "기능(Module) 변경사항 및 누락여부 검토",               DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.4", CheckItem = "BuggyLock, 이슈 항목 확인 및 수평전개 내용 검토",     DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.5", CheckItem = "HW 변경사항 검토",                                     DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.6", CheckItem = "기능(Module) 요구사항 적용 항목 고객 승인",            DisplayOrder = order++ });
                // ── 3. SW 통합 진행 ───────────────────────────────
                items.Add(new ChecklistItem { RowNo = "3",   CheckItem = "SW 통합 진행",                                         DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.1", CheckItem = "기능(Module) 요구사항별 버젼 검토",                    DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.2", CheckItem = "SW 통합 Package 구성",                                 DisplayOrder = order++ });
                // ── 4. 평가 및 검증 ───────────────────────────────
                items.Add(new ChecklistItem { RowNo = "4",   CheckItem = "평가 및 검증",                                         DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "4.1", CheckItem = "SW 배포 검증 계획 (일정, 테스트 케이스 No. 등)",       DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "4.2", CheckItem = "SW 테스트 환경 구성",                                  DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "4.3", CheckItem = "SW 배포 승인을 위한 요구사항 정의",                   DisplayOrder = order++ });
            }
            else // BA2
            {
                // ── 1. 프로젝트 관리 ──────────────────────────────
                items.Add(new ChecklistItem { RowNo = "1",   CheckItem = "프로젝트 관리",                                       DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.1", CheckItem = "Audit_1 평가결과 (이상여부)",                          DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.2", CheckItem = "SW 관리 문서 완성",                                   DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.3", CheckItem = "위험 요소 평가 - 고위험 요소 확인",                   DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "1.4", CheckItem = "위험 요소 고객에게 전달",                              DisplayOrder = order++ });
                // ── 2. 기능(Module) 요구사항 ─────────────────────
                items.Add(new ChecklistItem { RowNo = "2",   CheckItem = "기능(Module) 요구사항",                                DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.1", CheckItem = "기능(Module) 요구사항 및 누락여부 확인",               DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.2", CheckItem = "Audit_1 이후 신규 요구사항 확인 및 고객 승인 확인",   DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.3", CheckItem = "이슈 항목 확인 및 수평전개 적용 여부 확인",           DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.4", CheckItem = "연관 모듈 리비젼 관리/[변수/상수값]간 정합성 확인",   DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "2.5", CheckItem = "HW 변경사항 확인 및 평가내용 검토",                   DisplayOrder = order++ });
                // ── 3. SW 배포작업 진행 ───────────────────────────
                items.Add(new ChecklistItem { RowNo = "3",   CheckItem = "SW 배포작업 진행",                                    DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.1", CheckItem = "기능(Module) 요구사항별 버젼 확인",                   DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.2", CheckItem = "Build 산출물 확인 (3자 리뷰)",                        DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.3", CheckItem = "주요 Calibration Data 확인",                          DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.4", CheckItem = "변경 내역서 작성",                                    DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.5", CheckItem = "SW 사양서 생성",                                      DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "3.6", CheckItem = "SW 배포 문서 작성",                                   DisplayOrder = order++ });
                // ── 4. 평가 및 검증 ───────────────────────────────
                items.Add(new ChecklistItem { RowNo = "4",   CheckItem = "평가 및 검증",                                        DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "4.1", CheckItem = "SW 배포 검증 결과 확인 (최신 테스트 케이스 기반) (ECU 리프로그래밍, 제어로직 기본기능 확인, SW 기본기능 확인, 고장진단 확인, 과거차 문제 등)", DisplayOrder = order++ });
                items.Add(new ChecklistItem { RowNo = "4.2", CheckItem = "ECU 메모리 사용량 및 CPU 가동률 확인",                DisplayOrder = order++ });
            }

            return items;
        }

        /// <summary>TableType + Version으로 타이틀 자동 완성: "[BA1] 20260123_v03"</summary>
        private void UpdateAutoTitle()
        {
            if (!string.IsNullOrEmpty(TableType) || !string.IsNullOrEmpty(Version))
                Title = $"[{TableType ?? ""}] {Version ?? ""}".Trim();
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

        /// <summary>
        /// 저장 시 현재 상태를 보존할지 결정합니다.
        /// 이미 상신된 문서(승인대기/승인완료/반려)는 기존 상태 유지,
        /// 미상신 문서(작성중/임시저장)는 TempSaved로 저장합니다.
        /// </summary>
        private ApprovalStatus GetCurrentApprovalStatus()
        {
            switch (StatusText)
            {
                case "승인대기": return ApprovalStatus.Pending;
                case "승인완료": return ApprovalStatus.Approved;
                case "반려":     return ApprovalStatus.Rejected;
                case "취소됨":   return ApprovalStatus.Canceled;
                default:         return ApprovalStatus.TempSaved;
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
                    Version = Version,
                    OutputPath = OutputPath,
                    CurrentApproverId = SelectedApprover?.UserId,
                    CurrentApproverName = SelectedApprover?.Name,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    // 이미 상신된 문서(승인대기/승인완료/반려)는 상태 보존, 미상신만 TempSaved
                    Status = GetCurrentApprovalStatus()
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
                        await _internalDb.SaveDocumentParticipantsAsync(DocId, Participants.ToList());
                    }
                    else
                    {
                        doc.DocId = DocId;
                        await _internalDb.UpdateDocumentAsync(doc);
                        await _internalDb.SaveDocumentParticipantsAsync(DocId, Participants.ToList());
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
                    Version = Version,
                    OutputPath = OutputPath,
                    CurrentApproverId = SelectedApprover.UserId,
                    CurrentApproverName = SelectedApprover?.Name,
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
                await _internalDb.SaveDocumentParticipantsAsync(DocId, Participants.ToList());

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
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(SaveButtonText));

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
                var approvalTime = DateTime.Now;
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
                    Version = Version,
                    OutputPath = OutputPath,
                    ApproverComment = ApproverComment,
                    CurrentApproverId = SelectedApprover?.UserId,
                    CurrentApproverName = SelectedApprover?.Name,
                    ApprovalId = _approvalId,
                    ApprovalTime = approvalTime,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.Approved
                };

                await _internalDb.UpdateDocumentAsync(doc);
                await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                StatusText = "승인완료";
                StatusColor = "#4CAF50";
                ApprovalDate = approvalTime.ToString("yyyy-MM-dd HH:mm");
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(IsApproved));
                OnPropertyChanged(nameof(ShowApprovalDate));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(ShowApproverComment));
                OnPropertyChanged(nameof(ShowApproverInput));

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
                var rejectTime = DateTime.Now;
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
                    Version = Version,
                    OutputPath = OutputPath,
                    ApproverComment = ApproverComment,
                    CurrentApproverId = SelectedApprover?.UserId,
                    CurrentApproverName = SelectedApprover?.Name,
                    ApprovalId = _approvalId,
                    ApprovalTime = rejectTime,
                    CreateDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    Status = ApprovalStatus.Rejected
                };

                await _internalDb.UpdateDocumentAsync(doc);
                await _internalDb.SaveChecklistItemsAsync(DocId, ChecklistItems.ToList());

                StatusText = "반려";
                StatusColor = "#f44336";
                ApprovalDate = rejectTime.ToString("yyyy-MM-dd HH:mm");
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(ShowApprovalDate));
                OnPropertyChanged(nameof(ShowApproverComment));
                OnPropertyChanged(nameof(CanDelete));

                MessageBox.Show("반려되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"반려 처리 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CancelSubmitAsync()
        {
            var result = MessageBox.Show(
                "결재 요청을 취소하시겠습니까?\n취소 후 임시저장 상태로 변경됩니다.",
                "결재 취소 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _internalDb.UpdateDocumentStatusAsync(DocId, ApprovalStatus.TempSaved);

                StatusText = "임시저장";
                StatusColor = "#666";
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(CanApprove));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(SaveButtonText));

                MessageBox.Show("결재 요청이 취소되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"취소 처리 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteAsync()
        {
            if (DocId == 0) return;
            var result = MessageBox.Show(
                $"'{Title}' 문서를 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _internalDb.DeleteDocumentAsync(DocId);
                _onGoBack?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Participant Helpers

        private void AddParticipant(User user)
        {
            if (user == null || Participants.Any(p => p.UserId == user.UserId)) return;
            Participants.Add(user);
            IsParticipantSearchVisible = false;
            ParticipantSearchResults.Clear();
            _participantSearchText = "";
            OnPropertyChanged(nameof(ParticipantSearchText));
        }

        private void RemoveParticipant(User user)
        {
            if (user == null) return;
            _groupParticipantIds.Remove(user.UserId); // 수동 제거 시 그룹 추적에서도 제거
            Participants.Remove(user);
        }

        private async Task LoadParticipantGroupAsync(string groupName)
        {
            try
            {
                // 이전 그룹에서 자동 추가된 참여자 제거
                if (_groupParticipantIds.Count > 0)
                {
                    var toRemove = Participants.Where(p => _groupParticipantIds.Contains(p.UserId)).ToList();
                    foreach (var p in toRemove) Participants.Remove(p);
                    _groupParticipantIds.Clear();
                }

                // 새 그룹 참여자 추가
                var groupMembers = await _internalDb.GetParticipantGroupAsync(groupName);
                foreach (var m in groupMembers)
                {
                    if (!Participants.Any(p => p.UserId == m.UserId))
                    {
                        Participants.Add(m);
                        _groupParticipantIds.Add(m.UserId);
                    }
                }
            }
            catch { /* 그룹 로드 실패 시 무시 */ }
        }

        private void ScheduleParticipantSearch()
        {
            _participantSearchCts?.Cancel();
            _participantSearchCts?.Dispose();
            _participantSearchCts = new CancellationTokenSource();
            var token = _participantSearchCts.Token;
            var searchText = ParticipantSearchText;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (token.IsCancellationRequested) return;
                    Application.Current.Dispatcher.Invoke(() => RunParticipantSearch(searchText));
                }
                catch (OperationCanceledException)
                {
                    // 정상적인 debounce 취소 — 무시
                }
            });
        }

        private void RunParticipantSearch(string searchText)
        {
            ParticipantSearchResults.Clear();
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 1)
            {
                IsParticipantSearchVisible = false;
                return;
            }

            var matches = _allUsers
                .Where(u => (u.Name?.Contains(searchText) == true ||
                             u.UserId?.Contains(searchText) == true ||
                             u.AdAccount?.Contains(searchText) == true)
                            && !Participants.Any(p => p.UserId == u.UserId))
                .Take(8);

            foreach (var u in matches) ParticipantSearchResults.Add(u);
            IsParticipantSearchVisible = ParticipantSearchResults.Count > 0;
        }

        // ── 버전 자동완성 ───────────────────────────────────────────────

        private void ScheduleVersionSuggestion(string keyword)
        {
            _versionSuggestionCts?.Cancel();
            _versionSuggestionCts?.Dispose();
            _versionSuggestionCts = new CancellationTokenSource();
            var token = _versionSuggestionCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (token.IsCancellationRequested) return;

                    var suggestions = await GetVersionSuggestionsAsync(keyword);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        VersionSuggestions.Clear();
                        foreach (var s in suggestions) VersionSuggestions.Add(s);
                        IsVersionSuggestionsVisible = VersionSuggestions.Count > 0;
                    });
                }
                catch (OperationCanceledException) { }
            });
        }

        private async Task<List<string>> GetVersionSuggestionsAsync(string keyword)
        {
            var docs = await _internalDb.GetAllDocumentsAsync();

            // 키워드가 포함된 버전만, 현재 입력값과 동일한 것은 제외
            var filtered = docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Version)
                         && d.Version.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                         && !string.Equals(d.Version, Version, StringComparison.OrdinalIgnoreCase));

            // 우선순위 점수 산정
            // 1. GenType 일치(+2) + InjType 일치(+2)
            // 2. 최신 문서 (CreateDate, tiebreaker)
            // 3. 키워드 일치율 (keyword.Length / version.Length 비율, 높을수록 정확한 매칭)
            var scored = filtered.Select(d => new
            {
                d.Version,
                d.CreateDate,
                Score = (string.Equals(d.GenType, GenType, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                      + (string.Equals(d.InjType, InjType, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
                      + (d.Version.Length > 0 ? (double)keyword.Length / d.Version.Length : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreateDate);

            // 중복 제거 후 10개
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var item in scored)
            {
                if (seen.Add(item.Version))
                    result.Add(item.Version);
                if (result.Count >= 10) break;
            }
            return result;
        }

        #endregion
    }
}
