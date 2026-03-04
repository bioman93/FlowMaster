using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowMaster.Desktop.Services;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Repositories;
using FlowMaster.Infrastructure.Services;
using Microsoft.Win32;

namespace FlowMaster.Desktop.ViewModels
{
    /// <summary>
    /// 시스템 관리자 화면 ViewModel.
    /// - 사용자 탭: 앱 등록 사용자 CRUD (AD 계정 기반)
    /// - 그룹 탭: 앱 자체 그룹 CRUD + 그룹 멤버 관리
    /// </summary>
    public class AdminViewModel : ObservableObject
    {
        private readonly IUserRepository _userRepo;
        private readonly IAppGroupRepository _groupRepo;
        private readonly AdAuthService _adService;
        public Action GoBack { get; set; }

        // AD 자동완성 검색 - debounce 타이머 (300ms)
        private readonly DispatcherTimer _adSearchTimer;
        private CancellationTokenSource _adSearchCts;

        // ── 사용자 탭 ─────────────────────────────────────────────────

        private ObservableCollection<User> _users = new ObservableCollection<User>();
        public ObservableCollection<User> Users
        {
            get => _users;
            set => SetProperty(ref _users, value);
        }

        private User _selectedUser;
        public User SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetProperty(ref _selectedUser, value) && value != null)
                    PopulateUserForm(value);
            }
        }

        // 사용자 입력 폼
        private string _formAdAccount;
        public string FormAdAccount
        {
            get => _formAdAccount;
            set
            {
                if (!SetProperty(ref _formAdAccount, value)) return;

                // 수정 모드에서는 자동 검색 비활성
                if (IsUserEditMode) return;

                if ((value?.Length ?? 0) >= 2)
                {
                    // 300ms debounce: 타이핑 멈춤 후 검색 실행
                    _adSearchTimer.Stop();
                    _adSearchTimer.Start();
                }
                else
                {
                    _adSearchTimer.Stop();
                    ClearAdSearchResults();
                }
            }
        }

        // AD 검색 드롭다운 상태
        private ObservableCollection<User> _adSearchResults = new ObservableCollection<User>();
        public ObservableCollection<User> AdSearchResults
        {
            get => _adSearchResults;
            set => SetProperty(ref _adSearchResults, value);
        }

        private bool _isAdSearchDropdownVisible;
        public bool IsAdSearchDropdownVisible
        {
            get => _isAdSearchDropdownVisible;
            set => SetProperty(ref _isAdSearchDropdownVisible, value);
        }

        private bool _isAdSearchLoading;
        public bool IsAdSearchLoading
        {
            get => _isAdSearchLoading;
            set => SetProperty(ref _isAdSearchLoading, value);
        }

        private string _formDisplayName;
        public string FormDisplayName
        {
            get => _formDisplayName;
            set => SetProperty(ref _formDisplayName, value);
        }

        private string _formEmail;
        public string FormEmail
        {
            get => _formEmail;
            set => SetProperty(ref _formEmail, value);
        }

        // 사용자 폼 - 그룹 다중 선택 체크박스 목록
        private ObservableCollection<GroupSelection> _formGroupSelections = new ObservableCollection<GroupSelection>();
        public ObservableCollection<GroupSelection> FormGroupSelections
        {
            get => _formGroupSelections;
            set => SetProperty(ref _formGroupSelections, value);
        }

        // 사용자 폼 수정 시 "변경 전" 그룹 목록 (저장 시 diff 계산용)
        private List<string> _originalUserGroups = new List<string>();

        // MPI/GDI 참여자 그룹 체크박스
        private bool _formIsMpi;
        public bool FormIsMpi
        {
            get => _formIsMpi;
            set => SetProperty(ref _formIsMpi, value);
        }

        private bool _formIsGdi;
        public bool FormIsGdi
        {
            get => _formIsGdi;
            set => SetProperty(ref _formIsGdi, value);
        }

        // 참여자 그룹 멤버 캐시 (사용자 폼 체크박스 상태 판별용)
        private List<User> _mpiMembers = new List<User>();
        private List<User> _gdiMembers = new List<User>();

        private bool _isUserEditMode;
        public bool IsUserEditMode
        {
            get => _isUserEditMode;
            set
            {
                if (SetProperty(ref _isUserEditMode, value))
                    OnPropertyChanged(nameof(UserFormTitle));
            }
        }

        private string _userStatusMessage;
        public string UserStatusMessage
        {
            get => _userStatusMessage;
            set => SetProperty(ref _userStatusMessage, value);
        }

        public string UserFormTitle => IsUserEditMode ? "사용자 수정" : "새 사용자 추가";

        // ── 그룹 탭 ──────────────────────────────────────────────────

        private ObservableCollection<AppGroup> _groups = new ObservableCollection<AppGroup>();
        public ObservableCollection<AppGroup> Groups
        {
            get => _groups;
            set => SetProperty(ref _groups, value);
        }

        private AppGroup _selectedGroup;
        public AppGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                {
                    if (value != null)
                    {
                        FormGroupName        = value.GroupName;
                        FormGroupDescription = value.Description;
                        IsGroupEditMode      = true;
                    }
                    OnPropertyChanged(nameof(CanDeleteSelectedGroup));
                    LoadGroupMembersAsync(value);
                }
            }
        }

        /// <summary>선택된 그룹이 기본 그룹이 아닐 때만 삭제 버튼 활성화.</summary>
        public bool CanDeleteSelectedGroup =>
            SelectedGroup != null && !SelectedGroup.IsDefault;

        private ObservableCollection<User> _groupMembers = new ObservableCollection<User>();
        public ObservableCollection<User> GroupMembers
        {
            get => _groupMembers;
            set => SetProperty(ref _groupMembers, value);
        }

        private ObservableCollection<User> _nonGroupMembers = new ObservableCollection<User>();
        public ObservableCollection<User> NonGroupMembers
        {
            get => _nonGroupMembers;
            set => SetProperty(ref _nonGroupMembers, value);
        }

        // 복수 선택 (코드-비하인드의 SelectionChanged에서 갱신)
        public List<User> SelectedMembersToAdd    { get; set; } = new List<User>();
        public List<User> SelectedMembersToRemove { get; set; } = new List<User>();

        // 그룹 입력 폼
        private string _formGroupName;
        public string FormGroupName
        {
            get => _formGroupName;
            set => SetProperty(ref _formGroupName, value);
        }

        private string _formGroupDescription;
        public string FormGroupDescription
        {
            get => _formGroupDescription;
            set => SetProperty(ref _formGroupDescription, value);
        }

        private bool _isGroupEditMode;
        public bool IsGroupEditMode
        {
            get => _isGroupEditMode;
            set
            {
                if (SetProperty(ref _isGroupEditMode, value))
                    OnPropertyChanged(nameof(GroupFormTitle));
            }
        }

        private string _groupStatusMessage;
        public string GroupStatusMessage
        {
            get => _groupStatusMessage;
            set => SetProperty(ref _groupStatusMessage, value);
        }

        public string GroupFormTitle => IsGroupEditMode ? "그룹 수정" : "새 그룹 추가";

        // ── 참여자 그룹 탭 ────────────────────────────────────────────

        private string _selectedParticipantGroup = "MPI";
        public string SelectedParticipantGroup
        {
            get => _selectedParticipantGroup;
            set
            {
                if (SetProperty(ref _selectedParticipantGroup, value))
                    _ = LoadParticipantGroupMembersAsync();
            }
        }

        private ObservableCollection<User> _participantGroupMembers = new ObservableCollection<User>();
        public ObservableCollection<User> ParticipantGroupMembers
        {
            get => _participantGroupMembers;
            set => SetProperty(ref _participantGroupMembers, value);
        }

        private CancellationTokenSource _participantGroupSearchCts;

        private string _participantGroupSearchText;
        public string ParticipantGroupSearchText
        {
            get => _participantGroupSearchText;
            set
            {
                if (!SetProperty(ref _participantGroupSearchText, value)) return;
                _participantGroupSearchCts?.Cancel();
                if ((value?.Length ?? 0) >= 2)
                {
                    _participantGroupSearchCts = new CancellationTokenSource();
                    var cts = _participantGroupSearchCts;
                    var query = value;
                    Task.Delay(300, cts.Token).ContinueWith(async t =>
                    {
                        if (t.IsCanceled) return;
                        var results = await _adService.SearchAdUsersAsync(query);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (cts.IsCancellationRequested) return;
                            ParticipantGroupSearchResults = new ObservableCollection<User>(
                                results.Where(u => !ParticipantGroupMembers.Any(m => m.UserId == u.UserId)));
                            IsParticipantGroupSearchVisible = ParticipantGroupSearchResults.Count > 0;
                        });
                    }, TaskScheduler.Default);
                }
                else
                {
                    ParticipantGroupSearchResults.Clear();
                    IsParticipantGroupSearchVisible = false;
                }
            }
        }

        private ObservableCollection<User> _participantGroupSearchResults = new ObservableCollection<User>();
        public ObservableCollection<User> ParticipantGroupSearchResults
        {
            get => _participantGroupSearchResults;
            set => SetProperty(ref _participantGroupSearchResults, value);
        }

        private bool _isParticipantGroupSearchVisible;
        public bool IsParticipantGroupSearchVisible
        {
            get => _isParticipantGroupSearchVisible;
            set => SetProperty(ref _isParticipantGroupSearchVisible, value);
        }

        private User _selectedParticipantGroupMember;
        public User SelectedParticipantGroupMember
        {
            get => _selectedParticipantGroupMember;
            set => SetProperty(ref _selectedParticipantGroupMember, value);
        }

        private string _participantGroupStatusMessage;
        public string ParticipantGroupStatusMessage
        {
            get => _participantGroupStatusMessage;
            set => SetProperty(ref _participantGroupStatusMessage, value);
        }

        // ── 체크리스트 템플릿 탭 ──────────────────────────────────────

        private ObservableCollection<FmChecklistTemplateDto> _checklistTemplates
            = new ObservableCollection<FmChecklistTemplateDto>();
        public ObservableCollection<FmChecklistTemplateDto> ChecklistTemplates
        {
            get => _checklistTemplates;
            set => SetProperty(ref _checklistTemplates, value);
        }

        private FmChecklistTemplateDto _selectedTemplate;
        public FmChecklistTemplateDto SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value) && value != null)
                    _ = LoadTemplateDetailAsync(value.TemplateId);
            }
        }

        private ObservableCollection<FmChecklistTemplateItemDto> _templateItems
            = new ObservableCollection<FmChecklistTemplateItemDto>();
        public ObservableCollection<FmChecklistTemplateItemDto> TemplateItems
        {
            get => _templateItems;
            set => SetProperty(ref _templateItems, value);
        }

        private string _templateFormCode;
        public string TemplateFormCode
        {
            get => _templateFormCode;
            set => SetProperty(ref _templateFormCode, value);
        }

        private string _templateFormName;
        public string TemplateFormName
        {
            get => _templateFormName;
            set => SetProperty(ref _templateFormName, value);
        }

        private string _templateStatusMessage;
        public string TemplateStatusMessage
        {
            get => _templateStatusMessage;
            set => SetProperty(ref _templateStatusMessage, value);
        }

        private bool _templateStatusIsError;
        public bool TemplateStatusIsError
        {
            get => _templateStatusIsError;
            set => SetProperty(ref _templateStatusIsError, value);
        }

        /// <summary>평가 코드 선택 목록 (DataGrid ComboBox용)</summary>
        public static IReadOnlyList<string> EvalOptions { get; } =
            new List<string> { "", "+", "(+)", "(-)", "-", "nb" };

        private bool _isTemplateEditMode;
        public bool IsTemplateEditMode
        {
            get => _isTemplateEditMode;
            set
            {
                if (SetProperty(ref _isTemplateEditMode, value))
                {
                    OnPropertyChanged(nameof(IsNewTemplate));
                    OnPropertyChanged(nameof(TemplateFormTitle));
                }
            }
        }

        /// <summary>새 템플릿 작성 중 여부 (IsTemplateEditMode의 역). 샘플 내보내기 버튼 표시 조건.</summary>
        public bool IsNewTemplate => !_isTemplateEditMode;

        public string TemplateFormTitle => _isTemplateEditMode ? "템플릿 수정" : "새 템플릿 작성";

        private bool _templateHasDescription;
        public bool TemplateHasDescription
        {
            get => _templateHasDescription;
            set => SetProperty(ref _templateHasDescription, value);
        }

        /// <summary>DataGrid에서 선택된 항목 목록. AdminView 코드비하인드에서 설정.</summary>
        public IList<FmChecklistTemplateItemDto> SelectedTemplateItems { get; set; }
            = new List<FmChecklistTemplateItemDto>();

        // 현재 편집 중인 TemplateId (저장 시 사용)
        private int _currentTemplateId;

        // ── 산출물 경로 설정 탭 ─────────────────────────────────────────

        public ObservableCollection<FmOutputPathConfigDto> Ba1OutputConfigs { get; }
            = new ObservableCollection<FmOutputPathConfigDto>();

        public ObservableCollection<FmOutputPathConfigDto> Ba2OutputConfigs { get; }
            = new ObservableCollection<FmOutputPathConfigDto>();

        // ── Commands ─────────────────────────────────────────────────

        public ICommand GoBackCommand { get; }

        // 사용자 CRUD
        public ICommand LookupAdUserCommand { get; }
        public ICommand SaveUserCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand NewUserFormCommand { get; }

        // 그룹 CRUD
        public ICommand SaveGroupCommand { get; }
        public ICommand DeleteGroupCommand { get; }
        public ICommand NewGroupFormCommand { get; }

        // 그룹 멤버 관리
        public ICommand AddGroupMemberCommand { get; }
        public ICommand RemoveGroupMemberCommand { get; }

        // 참여자 그룹 관리
        public ICommand SelectMpiGroupCommand { get; }
        public ICommand SelectGdiGroupCommand { get; }
        public ICommand AddToParticipantGroupCommand { get; }
        public ICommand RemoveFromParticipantGroupCommand { get; }

        // 산출물 경로 설정
        public ICommand SaveOutputConfigsCommand { get; }

        // 체크리스트 템플릿 관리
        public ICommand NewTemplateCommand { get; }
        public ICommand RefreshTemplatesCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand AddMainTemplateItemCommand { get; }
        public ICommand AddSubTemplateItemCommand { get; }
        public ICommand RemoveSelectedTemplateItemsCommand { get; }
        public ICommand RevertTemplateCommand { get; }
        public ICommand ImportFromClipboardCommand { get; }
        public ICommand ExportSampleCsvCommand { get; }
        public ICommand ExportCurrentCsvCommand { get; }

        // ── Constructor ───────────────────────────────────────────────

        private readonly IApprovalRepository _approvalRepo;
        private readonly ApprovalApiClient _apiClient;

        public AdminViewModel(IUserRepository userRepo, IAppGroupRepository groupRepo,
            AdAuthService adService, IApprovalRepository approvalRepo = null,
            ApprovalApiClient apiClient = null)
        {
            _userRepo     = userRepo;
            _groupRepo    = groupRepo;
            _adService    = adService;
            _approvalRepo = approvalRepo;
            _apiClient    = apiClient;

            // AD 자동완성 debounce 타이머 (사용자/그룹 탭 공용)
            _adSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _adSearchTimer.Tick += OnAdSearchTimerTick;

            GoBackCommand         = new RelayCommand(() => GoBack?.Invoke());
            LookupAdUserCommand   = new AsyncRelayCommand(LookupAdUserAsync);
            SaveUserCommand       = new AsyncRelayCommand(SaveUserAsync);
            DeleteUserCommand     = new AsyncRelayCommand(DeleteUserAsync);
            NewUserFormCommand    = new RelayCommand(ClearUserForm);
            SaveGroupCommand      = new AsyncRelayCommand(SaveGroupAsync);
            DeleteGroupCommand    = new AsyncRelayCommand(DeleteGroupAsync);
            NewGroupFormCommand   = new RelayCommand(ClearGroupForm);
            AddGroupMemberCommand = new AsyncRelayCommand(AddGroupMemberAsync);
            RemoveGroupMemberCommand = new AsyncRelayCommand(RemoveGroupMemberAsync);

            SelectMpiGroupCommand = new RelayCommand(() => SelectedParticipantGroup = "MPI");
            SelectGdiGroupCommand = new RelayCommand(() => SelectedParticipantGroup = "GDI");
            AddToParticipantGroupCommand = new RelayCommand<User>(AddToParticipantGroup);
            RemoveFromParticipantGroupCommand = new AsyncRelayCommand(RemoveFromParticipantGroupAsync);

            SaveOutputConfigsCommand  = new AsyncRelayCommand(SaveOutputConfigsAsync);

            NewTemplateCommand        = new RelayCommand(ClearTemplateForm);
            RefreshTemplatesCommand   = new AsyncRelayCommand(LoadChecklistTemplatesAsync);
            SaveTemplateCommand       = new AsyncRelayCommand(SaveTemplateAsync);
            DeleteTemplateCommand     = new AsyncRelayCommand(DeleteTemplateAsync);
            AddMainTemplateItemCommand         = new RelayCommand(AddMainTemplateItem);
            AddSubTemplateItemCommand          = new RelayCommand(AddSubTemplateItem);
            RemoveSelectedTemplateItemsCommand = new RelayCommand(RemoveSelectedTemplateItems);
            RevertTemplateCommand              = new AsyncRelayCommand(RevertTemplateAsync);
            ImportFromClipboardCommand         = new RelayCommand(ImportFromClipboard);
            ExportSampleCsvCommand             = new AsyncRelayCommand(ExportSampleCsvAsync);
            ExportCurrentCsvCommand            = new AsyncRelayCommand(ExportCurrentCsvAsync);
        }

        public async Task InitializeAsync()
        {
            await LoadGroupsAsync();   // 먼저 로드해야 RefreshGroupSelections 가능
            await LoadUsersAsync();
            await LoadParticipantGroupMembersAsync();
            await LoadParticipantGroupCacheAsync();
            UpdateFirstRunState();
            await LoadChecklistTemplatesAsync();
            await LoadOutputConfigsAsync();
        }

        private bool _isFirstRun;
        public bool IsFirstRun
        {
            get => _isFirstRun;
            set => SetProperty(ref _isFirstRun, value);
        }

        private void UpdateFirstRunState()
        {
            IsFirstRun = Users.Count == 0;
        }

        // ── AD 자동완성 검색 ──────────────────────────────────────────

        private async void OnAdSearchTimerTick(object sender, EventArgs e)
        {
            _adSearchTimer.Stop();
            await RunAdSearchAsync(FormAdAccount);
        }

        private async Task RunAdSearchAsync(string query)
        {
            // 이전 검색 취소
            _adSearchCts?.Cancel();
            _adSearchCts = new CancellationTokenSource();
            var token = _adSearchCts.Token;

            IsAdSearchLoading = true;
            try
            {
                var results = await _adService.SearchAdUsersAsync(query);
                if (token.IsCancellationRequested) return;

                AdSearchResults = new ObservableCollection<User>(results);
                IsAdSearchDropdownVisible = results.Count > 0;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    AppLogger.Error("[AdminViewModel] AD 자동완성 검색 실패", ex);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    IsAdSearchLoading = false;
            }
        }

        /// <summary>드롭다운에서 항목 선택 시 호출됩니다 (코드-비하인드에서 호출).</summary>
        public void SelectAdSearchResult(User user)
        {
            if (user == null) return;

            // 검색 트리거 없이 값 설정 (타이머 방지)
            _adSearchTimer.Stop();
            _formAdAccount = user.AdAccount; // backing field 직접 설정
            OnPropertyChanged(nameof(FormAdAccount));

            FormDisplayName = user.Name;
            FormEmail       = user.Email;

            ClearAdSearchResults();
            UserStatusMessage = $"선택됨: {user.Name} ({user.AdAccount})";
        }

        private void ClearAdSearchResults()
        {
            _adSearchCts?.Cancel();
            AdSearchResults = new ObservableCollection<User>();
            IsAdSearchDropdownVisible = false;
            IsAdSearchLoading = false;
        }

        // ── 사용자 관리 ───────────────────────────────────────────────

        private async Task LoadUsersAsync()
        {
            try
            {
                var list = await _userRepo.GetAllUsersIncludeDisabledAsync();
                Users = new ObservableCollection<User>(list);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 사용자 목록 로드 실패", ex);
                UserStatusMessage = $"사용자 목록 로드 실패: {ex.Message}";
            }
        }

        private async Task LookupAdUserAsync()
        {
            if (string.IsNullOrWhiteSpace(FormAdAccount))
            {
                UserStatusMessage = "AD 계정명을 입력하세요.";
                return;
            }

            UserStatusMessage = $"AD에서 '{FormAdAccount}' 조회 중...";
            try
            {
                var adUser = await _adService.LookupAdUserAsync(FormAdAccount.Trim());
                if (adUser != null)
                {
                    FormDisplayName = adUser.Name;
                    FormEmail       = adUser.Email;
                    UserStatusMessage = $"AD 조회 성공: {adUser.Name}";
                }
                else
                {
                    UserStatusMessage = $"AD에서 '{FormAdAccount}' 계정을 찾을 수 없습니다.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] AD 조회 실패", ex);
                UserStatusMessage = $"AD 조회 실패: {ex.Message}";
            }
        }

        private async Task SaveUserAsync()
        {
            if (string.IsNullOrWhiteSpace(FormAdAccount))
            {
                UserStatusMessage = "AD 계정명을 입력하세요.";
                return;
            }
            if (string.IsNullOrWhiteSpace(FormDisplayName))
            {
                UserStatusMessage = "이름을 입력하세요.";
                return;
            }

            try
            {
                var user = new User
                {
                    UserId    = FormAdAccount.Trim(),
                    AdAccount = FormAdAccount.Trim(),
                    Name      = FormDisplayName.Trim(),
                    Email     = FormEmail?.Trim()
                };

                if (IsUserEditMode)
                {
                    await _userRepo.UpdateUserAsync(user);
                    UserStatusMessage = $"'{user.Name}' 수정 완료";
                }
                else
                {
                    await _userRepo.AddUserAsync(user);
                    UserStatusMessage = $"'{user.Name}' 추가 완료";
                }

                // 앱 그룹 멤버십 동기화 (체크박스 선택 기준)
                // 아무 그룹도 선택하지 않은 경우 GeneralUser를 기본으로 적용
                bool anySelected = FormGroupSelections.Any(s => s.IsSelected);
                foreach (var sel in FormGroupSelections)
                {
                    bool effectiveSelected = sel.IsSelected ||
                        (!anySelected && string.Equals(sel.GroupName, "GeneralUser", StringComparison.OrdinalIgnoreCase));
                    var wasIn = _originalUserGroups.Any(g =>
                        string.Equals(g, sel.GroupName, StringComparison.OrdinalIgnoreCase));
                    if (effectiveSelected && !wasIn)
                        await _groupRepo.AddGroupMemberAsync(sel.GroupId, user.AdAccount);
                    else if (!effectiveSelected && wasIn)
                        await _groupRepo.RemoveGroupMemberAsync(sel.GroupId, user.AdAccount);
                }

                // MPI/GDI 참여자 그룹 멤버십 동기화
                if (_approvalRepo != null)
                {
                    var wasMpi = _mpiMembers.Any(m =>
                        string.Equals(m.UserId, user.AdAccount, StringComparison.OrdinalIgnoreCase));
                    var wasGdi = _gdiMembers.Any(m =>
                        string.Equals(m.UserId, user.AdAccount, StringComparison.OrdinalIgnoreCase));

                    if (FormIsMpi && !wasMpi)
                        await _approvalRepo.AddParticipantGroupMemberAsync("MPI", user);
                    else if (!FormIsMpi && wasMpi)
                        await _approvalRepo.RemoveParticipantGroupMemberAsync("MPI", user.AdAccount);

                    if (FormIsGdi && !wasGdi)
                        await _approvalRepo.AddParticipantGroupMemberAsync("GDI", user);
                    else if (!FormIsGdi && wasGdi)
                        await _approvalRepo.RemoveParticipantGroupMemberAsync("GDI", user.AdAccount);

                    await LoadParticipantGroupCacheAsync();
                }

                ClearUserForm();
                await LoadGroupsAsync(); // 그룹 멤버 카운트 갱신
                await LoadUsersAsync();
                UpdateFirstRunState();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 사용자 저장 실패", ex);
                UserStatusMessage = $"저장 실패: {ex.Message}";
            }
        }

        private async Task DeleteUserAsync()
        {
            if (SelectedUser == null)
            {
                UserStatusMessage = "삭제할 사용자를 선택하세요.";
                return;
            }

            var result = MessageBox.Show(
                $"'{SelectedUser.Name}' 사용자를 삭제하시겠습니까?\n그룹 멤버십도 함께 삭제됩니다.",
                "사용자 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _userRepo.DeleteUserAsync(SelectedUser.AdAccount);
                UserStatusMessage = $"'{SelectedUser.Name}' 삭제 완료";
                ClearUserForm();
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 사용자 삭제 실패", ex);
                UserStatusMessage = $"삭제 실패: {ex.Message}";
            }
        }

        private void PopulateUserForm(User user)
        {
            IsUserEditMode  = true;   // FormAdAccount setter의 타이머 기동 방지를 위해 먼저 설정
            FormAdAccount   = user.AdAccount;
            FormDisplayName = user.Name;
            FormEmail       = user.Email;

            // 현재 소속 그룹 체크박스 반영 + 변경 전 상태 기록
            _originalUserGroups = new List<string>(user.Groups ?? new List<string>());
            RefreshGroupSelections(_originalUserGroups);

            // MPI/GDI 현재 멤버십 반영
            FormIsMpi = _mpiMembers.Any(m =>
                string.Equals(m.UserId, user.AdAccount, StringComparison.OrdinalIgnoreCase));
            FormIsGdi = _gdiMembers.Any(m =>
                string.Equals(m.UserId, user.AdAccount, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearUserForm()
        {
            _adSearchTimer.Stop();
            ClearAdSearchResults();
            _formAdAccount  = string.Empty;
            OnPropertyChanged(nameof(FormAdAccount));
            FormDisplayName = string.Empty;
            FormEmail       = string.Empty;
            FormIsMpi       = false;
            FormIsGdi       = false;
            IsUserEditMode  = false;
            SelectedUser    = null;
            _originalUserGroups = new List<string>();
            RefreshGroupSelections();
        }

        // ── 그룹 관리 ─────────────────────────────────────────────────

        private async Task LoadGroupsAsync()
        {
            try
            {
                var list = await _groupRepo.GetAllGroupsAsync();
                Groups = new ObservableCollection<AppGroup>(list);
                // 사용자 폼 그룹 체크박스 목록도 갱신 (선택 상태 유지)
                var selectedNames = FormGroupSelections
                    .Where(s => s.IsSelected).Select(s => s.GroupName).ToList();
                RefreshGroupSelections(selectedNames);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 그룹 목록 로드 실패", ex);
                GroupStatusMessage = $"그룹 목록 로드 실패: {ex.Message}";
            }
        }

        private async void LoadGroupMembersAsync(AppGroup group)
        {
            if (group == null)
            {
                GroupMembers    = new ObservableCollection<User>();
                NonGroupMembers = new ObservableCollection<User>();
                return;
            }

            try
            {
                var withMembers = await _groupRepo.GetGroupWithMembersAsync(group.GroupId);
                GroupMembers = new ObservableCollection<User>(withMembers?.Members ?? new List<User>());

                var allUsers  = await _userRepo.GetAllUsersAsync();
                var memberIds = GroupMembers.Select(m => m.AdAccount).ToHashSet(StringComparer.OrdinalIgnoreCase);
                NonGroupMembers = new ObservableCollection<User>(
                    allUsers.Where(u => !memberIds.Contains(u.AdAccount)));
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 그룹 멤버 로드 실패", ex);
                GroupStatusMessage = $"그룹 멤버 로드 실패: {ex.Message}";
            }
        }

        private async Task SaveGroupAsync()
        {
            if (string.IsNullOrWhiteSpace(FormGroupName))
            {
                GroupStatusMessage = "그룹명을 입력하세요.";
                return;
            }

            try
            {
                var group = new AppGroup
                {
                    GroupId     = SelectedGroup?.GroupId ?? 0,
                    GroupName   = FormGroupName.Trim(),
                    Description = FormGroupDescription?.Trim()
                };

                if (IsGroupEditMode && SelectedGroup != null)
                {
                    await _groupRepo.UpdateGroupAsync(group);
                    GroupStatusMessage = $"'{group.GroupName}' 수정 완료";
                }
                else
                {
                    await _groupRepo.AddGroupAsync(group);
                    GroupStatusMessage = $"'{group.GroupName}' 그룹 추가 완료";
                }

                ClearGroupForm();
                await LoadGroupsAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 그룹 저장 실패", ex);
                GroupStatusMessage = $"저장 실패: {ex.Message}";
            }
        }

        private async Task DeleteGroupAsync()
        {
            if (SelectedGroup == null)
            {
                GroupStatusMessage = "삭제할 그룹을 선택하세요.";
                return;
            }

            if (SelectedGroup.IsDefault)
            {
                GroupStatusMessage = $"'{SelectedGroup.GroupName}'은(는) 기본 그룹으로 삭제할 수 없습니다.";
                return;
            }

            var result = MessageBox.Show(
                $"'{SelectedGroup.GroupName}' 그룹을 삭제하시겠습니까?\n멤버십 정보도 함께 삭제됩니다.",
                "그룹 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _groupRepo.DeleteGroupAsync(SelectedGroup.GroupId);
                GroupStatusMessage = $"'{SelectedGroup.GroupName}' 삭제 완료";
                ClearGroupForm();
                await LoadGroupsAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 그룹 삭제 실패", ex);
                GroupStatusMessage = $"삭제 실패: {ex.Message}";
            }
        }

        private async Task AddGroupMemberAsync()
        {
            if (SelectedGroup == null || SelectedMembersToAdd.Count == 0)
            {
                GroupStatusMessage = "그룹과 추가할 사용자를 선택하세요.";
                return;
            }
            try
            {
                foreach (var member in SelectedMembersToAdd)
                    await _groupRepo.AddGroupMemberAsync(SelectedGroup.GroupId, member.AdAccount);
                GroupStatusMessage = $"{SelectedMembersToAdd.Count}명 그룹에 추가됨";
                LoadGroupMembersAsync(SelectedGroup);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 멤버 추가 실패", ex);
                GroupStatusMessage = $"멤버 추가 실패: {ex.Message}";
            }
        }

        private async Task RemoveGroupMemberAsync()
        {
            if (SelectedGroup == null || SelectedMembersToRemove.Count == 0)
            {
                GroupStatusMessage = "그룹과 제거할 멤버를 선택하세요.";
                return;
            }
            var names = string.Join(", ", SelectedMembersToRemove.Select(m => m.Name));
            var result = MessageBox.Show(
                $"'{names}'을(를) '{SelectedGroup.GroupName}' 그룹에서 제거하시겠습니까?",
                "멤버 제거", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                foreach (var member in SelectedMembersToRemove)
                    await _groupRepo.RemoveGroupMemberAsync(SelectedGroup.GroupId, member.AdAccount);
                GroupStatusMessage = $"{SelectedMembersToRemove.Count}명 그룹에서 제거됨";
                LoadGroupMembersAsync(SelectedGroup);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 멤버 제거 실패", ex);
                GroupStatusMessage = $"멤버 제거 실패: {ex.Message}";
            }
        }

        private void ClearGroupForm()
        {
            FormGroupName        = string.Empty;
            FormGroupDescription = string.Empty;
            IsGroupEditMode      = false;
            SelectedGroup        = null;
        }

        // ── 참여자 그룹 관리 ─────────────────────────────────────────

        /// <summary>
        /// MPI/GDI 두 그룹 멤버를 모두 캐시에 로드합니다.
        /// 사용자 폼에서 체크박스 초기값 판별에 사용됩니다.
        /// </summary>
        private async Task LoadParticipantGroupCacheAsync()
        {
            if (_approvalRepo == null) return;
            try
            {
                _mpiMembers = await _approvalRepo.GetParticipantGroupAsync("MPI");
                _gdiMembers = await _approvalRepo.GetParticipantGroupAsync("GDI");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 참여자 그룹 캐시 로드 실패", ex);
            }
        }

        private async Task LoadParticipantGroupMembersAsync()
        {
            if (_approvalRepo == null) return;
            try
            {
                var members = await _approvalRepo.GetParticipantGroupAsync(SelectedParticipantGroup);
                ParticipantGroupMembers = new ObservableCollection<User>(members);
                ParticipantGroupStatusMessage = $"{SelectedParticipantGroup} 그룹 멤버 {members.Count}명";
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 참여자 그룹 로드 실패", ex);
                ParticipantGroupStatusMessage = $"그룹 로드 실패: {ex.Message}";
            }
        }

        private async void AddToParticipantGroup(User user)
        {
            if (user == null || _approvalRepo == null) return;
            try
            {
                await _approvalRepo.AddParticipantGroupMemberAsync(SelectedParticipantGroup, user);
                ParticipantGroupSearchResults.Clear();
                IsParticipantGroupSearchVisible = false;
                _participantGroupSearchText = "";
                OnPropertyChanged(nameof(ParticipantGroupSearchText));
                await LoadParticipantGroupMembersAsync();
                ParticipantGroupStatusMessage = $"'{user.Name}' {SelectedParticipantGroup} 그룹에 추가됨";
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 참여자 그룹 멤버 추가 실패", ex);
                ParticipantGroupStatusMessage = $"추가 실패: {ex.Message}";
            }
        }

        private async Task RemoveFromParticipantGroupAsync()
        {
            if (SelectedParticipantGroupMember == null || _approvalRepo == null)
            {
                ParticipantGroupStatusMessage = "제거할 멤버를 선택하세요.";
                return;
            }
            var result = MessageBox.Show(
                $"'{SelectedParticipantGroupMember.Name}'을(를) {SelectedParticipantGroup} 그룹에서 제거하시겠습니까?",
                "멤버 제거", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _approvalRepo.RemoveParticipantGroupMemberAsync(
                    SelectedParticipantGroup, SelectedParticipantGroupMember.UserId);
                ParticipantGroupStatusMessage = $"'{SelectedParticipantGroupMember.Name}' 제거됨";
                SelectedParticipantGroupMember = null;
                await LoadParticipantGroupMembersAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 참여자 그룹 멤버 제거 실패", ex);
                ParticipantGroupStatusMessage = $"제거 실패: {ex.Message}";
            }
        }

        // ── 그룹 체크박스 바인딩 헬퍼 ────────────────────────────────

        /// <summary>
        /// 현재 Groups 목록을 기반으로 FormGroupSelections를 재구성합니다.
        /// selectedGroupNames가 null이면 모두 미선택으로 초기화합니다.
        /// </summary>
        private void RefreshGroupSelections(IEnumerable<string> selectedGroupNames = null)
        {
            var selected = new HashSet<string>(
                selectedGroupNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            FormGroupSelections = new ObservableCollection<GroupSelection>(
                Groups.Select(g => new GroupSelection(g, selected.Contains(g.GroupName))));
        }

        // ── 체크리스트 템플릿 관리 ────────────────────────────────────

        private async Task LoadChecklistTemplatesAsync()
        {
            if (_apiClient == null)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = "API 클라이언트가 설정되지 않았습니다.";
                return;
            }
            try
            {
                TemplateStatusIsError = false;
                TemplateStatusMessage = "로드 중...";
                var list = await _apiClient.FmGetAllChecklistTemplatesAsync();
                ChecklistTemplates = new ObservableCollection<FmChecklistTemplateDto>(list);
                TemplateStatusIsError = false;
                TemplateStatusMessage = list.Count > 0
                    ? $"템플릿 {list.Count}개 로드됨"
                    : "템플릿이 없습니다. ApprovalSystem 서버를 재시작하면 BA1/BA2가 자동 생성됩니다.";
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 템플릿 목록 로드 실패", ex);
                TemplateStatusIsError = true;
                TemplateStatusMessage = $"로드 실패 (서버 연결 확인): {ex.Message}";
            }
        }

        private async Task LoadTemplateDetailAsync(int templateId)
        {
            if (_apiClient == null) return;
            try
            {
                var detail = await _apiClient.FmGetChecklistTemplateAsync(templateId);
                if (detail == null) return;

                _currentTemplateId     = detail.TemplateId;
                TemplateFormCode       = detail.TemplateCode;
                TemplateFormName       = detail.Name;
                TemplateHasDescription = detail.HasDescription;
                IsTemplateEditMode     = true;

                TemplateItems = new ObservableCollection<FmChecklistTemplateItemDto>(detail.Items);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 템플릿 상세 로드 실패", ex);
                TemplateStatusMessage = $"로드 실패: {ex.Message}";
            }
        }

        private void ClearTemplateForm()
        {
            _currentTemplateId     = 0;
            TemplateFormCode       = "";
            TemplateFormName       = "";
            TemplateHasDescription = false;
            IsTemplateEditMode     = false;
            TemplateItems.Clear();
            TemplateStatusMessage = "새 템플릿 작성 중";
        }

        private void AddMainTemplateItem()
        {
            var maxOrder = TemplateItems.Count > 0 ? TemplateItems.Max(i => i.DisplayOrder) : 0;
            var lastMain = TemplateItems
                .Where(i => !string.IsNullOrEmpty(i.RowNo) && !i.RowNo.Contains("."))
                .Select(i => { int v; return int.TryParse(i.RowNo, out v) ? v : 0; })
                .DefaultIfEmpty(0).Max();
            TemplateItems.Add(new FmChecklistTemplateItemDto
            {
                DisplayOrder  = maxOrder + 1,
                RowNo         = (lastMain + 1).ToString(),
                CheckItem     = "",
                OutputContent = null,
                EvaluationCode = null,
                Remarks       = null
            });
        }

        private void AddSubTemplateItem()
        {
            var maxOrder = TemplateItems.Count > 0 ? TemplateItems.Max(i => i.DisplayOrder) : 0;
            var lastMain = TemplateItems
                .Where(i => !string.IsNullOrEmpty(i.RowNo) && !i.RowNo.Contains("."))
                .Select(i => { int v; return int.TryParse(i.RowNo, out v) ? v : 0; })
                .DefaultIfEmpty(1).Max();
            var lastSub = TemplateItems
                .Where(i => !string.IsNullOrEmpty(i.RowNo) && i.RowNo.StartsWith(lastMain + "."))
                .Select(i =>
                {
                    var parts = i.RowNo.Split('.');
                    int v;
                    return parts.Length == 2 && int.TryParse(parts[1], out v) ? v : 0;
                })
                .DefaultIfEmpty(0).Max();
            TemplateItems.Add(new FmChecklistTemplateItemDto
            {
                DisplayOrder   = maxOrder + 1,
                RowNo          = $"{lastMain}.{lastSub + 1}",
                CheckItem      = "",
                OutputContent  = null,
                EvaluationCode = null,
                Remarks        = null
            });
        }

        private void RemoveSelectedTemplateItems()
        {
            if (SelectedTemplateItems == null || SelectedTemplateItems.Count == 0)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = "삭제할 항목을 먼저 선택하세요.";
                return;
            }
            var toRemove = SelectedTemplateItems.ToList();
            foreach (var item in toRemove)
                TemplateItems.Remove(item);
            SelectedTemplateItems = new List<FmChecklistTemplateItemDto>();
            TemplateStatusIsError = false;
            TemplateStatusMessage = $"{toRemove.Count}개 항목 삭제됨.";
        }

        private async Task RevertTemplateAsync()
        {
            if (_currentTemplateId > 0)
            {
                await LoadTemplateDetailAsync(_currentTemplateId);
                TemplateStatusIsError = false;
                TemplateStatusMessage = "마지막 저장 상태로 되돌렸습니다.";
            }
            else
            {
                ClearTemplateForm();
                TemplateStatusMessage = "작성 내용을 초기화했습니다.";
            }
        }

        /// <summary>
        /// 클립보드에서 항목 가져오기.
        /// 엑셀에서 복사(Ctrl+C)한 탭 구분 데이터를 파싱합니다.
        /// 보안툴로 암호화된 파일을 직접 읽을 수 없을 때 대안으로 사용합니다.
        /// </summary>
        private void ImportFromClipboard()
        {
            try
            {
                var text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    TemplateStatusIsError = true;
                    TemplateStatusMessage = "클립보드가 비어 있습니다. 엑셀에서 셀을 선택 후 Ctrl+C 하세요.";
                    return;
                }

                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var imported = new List<FmChecklistTemplateItemDto>();

                bool hasHeader = lines.Length > 0 && IsHeaderLine(lines[0]);
                int startLine = hasHeader ? 1 : 0;
                int order = TemplateItems.Count > 0 ? TemplateItems.Max(i => i.DisplayOrder) : 0;

                for (int i = startLine; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 엑셀 클립보드는 탭 구분, 없으면 CSV 자동 감지
                    char sep = line.Contains('\t') ? '\t' : line.Contains(';') ? ';' : ',';
                    var cols = SplitCsvLine(line, sep);

                    var rowNo      = cols.Length > 0 ? cols[0].Trim() : "";
                    var checkItem  = cols.Length > 1 ? UnescapeNewlines(cols[1].Trim()) : "";
                    var output     = cols.Length > 2 ? UnescapeNewlines(cols[2].Trim()) : null;
                    var evalCode   = cols.Length > 3 ? cols[3].Trim() : null;
                    var remarks    = cols.Length > 4 ? UnescapeNewlines(cols[4].Trim()) : null;
                    var remarksMpi = cols.Length > 5 ? UnescapeNewlines(cols[5].Trim()) : null;
                    var remarksGdi = cols.Length > 6 ? UnescapeNewlines(cols[6].Trim()) : null;

                    if (string.IsNullOrEmpty(rowNo) && string.IsNullOrEmpty(checkItem)) continue;

                    imported.Add(new FmChecklistTemplateItemDto
                    {
                        RowNo          = rowNo,
                        CheckItem      = checkItem,
                        OutputContent  = string.IsNullOrEmpty(output)     ? null : output,
                        EvaluationCode = string.IsNullOrEmpty(evalCode)   ? null : evalCode,
                        Remarks        = string.IsNullOrEmpty(remarks)    ? null : remarks,
                        RemarksMpi     = string.IsNullOrEmpty(remarksMpi) ? null : remarksMpi,
                        RemarksGdi     = string.IsNullOrEmpty(remarksGdi) ? null : remarksGdi,
                        DisplayOrder   = ++order
                    });
                }

                if (imported.Count == 0)
                {
                    TemplateStatusIsError = true;
                    TemplateStatusMessage = "가져올 항목이 없습니다. 헤더(No./확인항목/…)를 포함하여 복사하세요.";
                    return;
                }

                var result = MessageBox.Show(
                    $"{imported.Count}개 항목을 가져옵니다.\n기존 항목에 추가할까요?\n\n[예] 기존 항목 유지 후 추가\n[아니오] 기존 항목 대체",
                    "클립보드 붙여넣기", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.No) TemplateItems.Clear();

                foreach (var item in imported) TemplateItems.Add(item);
                TemplateStatusIsError = false;
                TemplateStatusMessage = $"클립보드에서 {imported.Count}개 항목 가져오기 완료";
            }
            catch (Exception ex)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = $"클립보드 읽기 실패: {ex.Message}";
            }
        }


        private async Task ExportSampleCsvAsync()
        {
            var dlg = new SaveFileDialog
            {
                Title      = "샘플 CSV 파일 저장",
                Filter     = "CSV 파일 (*.csv)|*.csv",
                FileName   = "체크리스트_템플릿_샘플.csv",
                DefaultExt = "csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // UTF-8 BOM: Excel에서 한글이 깨지지 않도록
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("No.,확인항목,산출물,평가,비고,비고(MPI),비고(GDI)");
                sb.AppendLine("1,메인 항목 예시 (숫자만 입력),,,,,");
                sb.AppendLine("1.1,서브 항목 예시 (숫자.숫자 형식),산출물 기본값,,,,");
                sb.AppendLine("1.2,\"확인항목에 쉼표가 포함되면 큰따옴표로 감쌀 것\",,+,,,");
                sb.AppendLine("2,두 번째 메인 항목,,,,,");
                sb.AppendLine("2.1,서브 항목,,,비고 예시,,");
                sb.AppendLine("2.2,MPI/GDI별 비고 예시,,,공통 비고,MPI 전용 비고,GDI 전용 비고");

                await Task.Run(() =>
                    File.WriteAllBytes(dlg.FileName,
                        new byte[] { 0xEF, 0xBB, 0xBF }  // UTF-8 BOM
                            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))
                            .ToArray()));

                TemplateStatusIsError = false;
                TemplateStatusMessage = $"샘플 파일 저장됨: {System.IO.Path.GetFileName(dlg.FileName)}";

                // 저장 후 Excel(또는 기본 앱)으로 바로 열기
                System.Diagnostics.Process.Start(dlg.FileName);
            }
            catch (Exception ex)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = $"저장 실패: {ex.Message}";
            }
        }

        /// <summary>현재 편집 중인 템플릿 항목을 CSV로 내보내고 Excel로 엽니다.</summary>
        private async Task ExportCurrentCsvAsync()
        {
            if (TemplateItems.Count == 0)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = "내보낼 항목이 없습니다. 먼저 템플릿을 선택하거나 항목을 추가하세요.";
                return;
            }

            // 파일명 제안: 코드_이름.csv (파일명 불가 문자 제거)
            var safeCode = SanitizeFileName(TemplateFormCode ?? "template");
            var safeName = SanitizeFileName(TemplateFormName ?? "");
            var suggested = string.IsNullOrEmpty(safeName)
                ? $"{safeCode}.csv"
                : $"{safeCode}_{safeName}.csv";

            var dlg = new SaveFileDialog
            {
                Title      = "현재 템플릿 CSV로 내보내기",
                Filter     = "CSV 파일 (*.csv)|*.csv",
                FileName   = suggested,
                DefaultExt = "csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("No.,확인항목,산출물,평가,비고,비고(MPI),비고(GDI)");

                foreach (var item in TemplateItems)
                {
                    var rowNo      = EscapeCsvField(item.RowNo           ?? "");
                    var checkItem  = EscapeCsvField(EscapeNewlines(item.CheckItem     ?? ""));
                    var output     = EscapeCsvField(EscapeNewlines(item.OutputContent ?? ""));
                    var eval       = EscapeCsvField(item.EvaluationCode  ?? "");
                    var remarks    = EscapeCsvField(EscapeNewlines(item.Remarks       ?? ""));
                    var remarksMpi = EscapeCsvField(EscapeNewlines(item.RemarksMpi    ?? ""));
                    var remarksGdi = EscapeCsvField(EscapeNewlines(item.RemarksGdi    ?? ""));
                    sb.AppendLine($"{rowNo},{checkItem},{output},{eval},{remarks},{remarksMpi},{remarksGdi}");
                }

                await Task.Run(() =>
                    File.WriteAllBytes(dlg.FileName,
                        new byte[] { 0xEF, 0xBB, 0xBF }   // UTF-8 BOM
                            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))
                            .ToArray()));

                TemplateStatusIsError = false;
                TemplateStatusMessage = $"내보내기 완료: {System.IO.Path.GetFileName(dlg.FileName)} ({TemplateItems.Count}행)";

                System.Diagnostics.Process.Start(dlg.FileName);
            }
            catch (Exception ex)
            {
                TemplateStatusIsError = true;
                TemplateStatusMessage = $"내보내기 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 첫 번째 셀 값이 헤더 행임을 나타내는지 판별합니다.
        /// "No.", "No.#", "번호", "Row No." 등 정확한 패턴만 헤더로 인식하여
        /// "No.1", "No.1-1" 같은 데이터 행을 헤더로 오인하지 않습니다.
        /// </summary>
        private static bool IsHeaderLine(string firstCell)
        {
            var cell = firstCell.Trim();
            // 정확히 일치하는 헤더 키워드
            var exact = new[] { "no", "no.", "no.#", "row", "row no.", "번호", "순번", "항목번호" };
            return Array.Exists(exact, k => string.Equals(cell, k, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>CSV 한 줄 파싱 (큰따옴표 처리 포함)</summary>
        private static string[] SplitCsvLine(string line, char sep)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { inQuotes = !inQuotes; }
                else if (c == sep && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        /// <summary>CSV에서 읽은 리터럴 \n을 실제 줄바꿈 문자로 변환</summary>
        private static string UnescapeNewlines(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Replace("\\n", "\n");
        }

        /// <summary>실제 줄바꿈 문자를 리터럴 \n으로 변환 (CSV 내보내기용)</summary>
        private static string EscapeNewlines(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Replace("\r\n", "\\n").Replace("\n", "\\n");
        }

        /// <summary>CSV 필드 이스케이프: 쉼표·큰따옴표 포함 시 큰따옴표로 감쌈</summary>
        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Contains(',') || value.Contains('"'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        /// <summary>파일명에 사용 불가한 문자를 밑줄로 대체</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private async Task SaveTemplateAsync()
        {
            if (_apiClient == null)
            {
                TemplateStatusMessage = "API 연결이 없습니다.";
                return;
            }
            if (string.IsNullOrWhiteSpace(TemplateFormCode) || string.IsNullOrWhiteSpace(TemplateFormName))
            {
                TemplateStatusMessage = "코드와 이름을 입력하세요.";
                return;
            }

            var dto = new FmChecklistTemplateDto
            {
                TemplateId      = _currentTemplateId,
                TemplateCode    = TemplateFormCode.Trim().ToUpper(),
                Name            = TemplateFormName.Trim(),
                HasDescription  = TemplateHasDescription,
                Items           = TemplateItems.ToList()
            };

            bool createNewVersion = false;
            if (IsTemplateEditMode)
            {
                // 수정 모드: 버전 변경 여부 확인
                var ask = MessageBox.Show(
                    "새 버전으로 저장하시겠습니까?\n\n[예] 새 버전 생성 (기존 버전 보존)\n[아니오] 현재 버전 덮어쓰기",
                    "버전 관리",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                createNewVersion = (ask == MessageBoxResult.Yes);
            }

            try
            {
                TemplateStatusIsError = false;
                if (!IsTemplateEditMode)
                {
                    var newId = await _apiClient.FmCreateChecklistTemplateAsync(dto);
                    _currentTemplateId = newId;
                    IsTemplateEditMode = true;
                    TemplateStatusMessage = $"새 템플릿 '{dto.Name}' 생성됨";
                }
                else
                {
                    await _apiClient.FmSaveChecklistTemplateAsync(_currentTemplateId, dto, createNewVersion);
                    TemplateStatusMessage = createNewVersion
                        ? $"'{dto.Name}' 새 버전으로 저장됨"
                        : $"'{dto.Name}' 저장됨";
                }

                await LoadChecklistTemplatesAsync();

                // 저장 후 해당 템플릿 선택 상태 유지
                SelectedTemplate = ChecklistTemplates.FirstOrDefault(
                    t => t.TemplateCode == dto.TemplateCode && t.IsLatest);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 템플릿 저장 실패", ex);
                TemplateStatusIsError = true;
                TemplateStatusMessage = $"저장 실패: {ex.Message}";
            }
        }

        private async Task DeleteTemplateAsync()
        {
            if (_apiClient == null || _currentTemplateId == 0)
            {
                TemplateStatusMessage = "삭제할 템플릿을 선택하세요.";
                return;
            }

            var result = MessageBox.Show(
                $"'{TemplateFormName}' 템플릿을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "템플릿 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _apiClient.FmDeleteChecklistTemplateAsync(_currentTemplateId);
                TemplateStatusMessage = $"'{TemplateFormName}' 삭제됨";
                ClearTemplateForm();
                await LoadChecklistTemplatesAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 템플릿 삭제 실패", ex);
                TemplateStatusMessage = $"삭제 실패: {ex.Message}";
            }
        }

        // ── 산출물 경로 설정 ───────────────────────────────────────────

        private async Task LoadOutputConfigsAsync()
        {
            if (_apiClient == null) return;
            try
            {
                var ba1 = await _apiClient.FmGetOutputPathConfigsAsync("BA1");
                Ba1OutputConfigs.Clear();
                foreach (var c in ba1) Ba1OutputConfigs.Add(c);

                var ba2 = await _apiClient.FmGetOutputPathConfigsAsync("BA2");
                Ba2OutputConfigs.Clear();
                foreach (var c in ba2) Ba2OutputConfigs.Add(c);
            }
            catch { /* API 미연결 시 무시 */ }
        }

        private async Task SaveOutputConfigsAsync()
        {
            if (_apiClient == null) return;
            try
            {
                await _apiClient.FmSaveOutputPathConfigsAsync("BA1", Ba1OutputConfigs.ToList());
                await _apiClient.FmSaveOutputPathConfigsAsync("BA2", Ba2OutputConfigs.ToList());
                MessageBox.Show("산출물 경로 설정이 저장되었습니다.", "저장 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdminViewModel] 산출물 경로 설정 저장 실패", ex);
                MessageBox.Show($"저장 실패:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }

    /// <summary>
    /// 사용자 폼에서 그룹 체크박스 바인딩에 사용되는 헬퍼 클래스.
    /// </summary>
    public class GroupSelection : ObservableObject
    {
        public int    GroupId   { get; }
        public string GroupName { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public GroupSelection(AppGroup group, bool isSelected)
        {
            GroupId    = group.GroupId;
            GroupName  = group.GroupName;
            _isSelected = isSelected;
        }
    }
}
