using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private UserRole _formRole = UserRole.GeneralUser;
        public UserRole FormRole
        {
            get => _formRole;
            set => SetProperty(ref _formRole, value);
        }

        public UserRole[] RoleOptions { get; } =
        {
            UserRole.GeneralUser,
            UserRole.Approver,
            UserRole.Admin
        };

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
                        FormGroupRole        = value.Role;
                        IsGroupEditMode      = true;
                    }
                    LoadGroupMembersAsync(value);
                }
            }
        }

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

        private User _selectedMemberToAdd;
        public User SelectedMemberToAdd
        {
            get => _selectedMemberToAdd;
            set => SetProperty(ref _selectedMemberToAdd, value);
        }

        private User _selectedMemberToRemove;
        public User SelectedMemberToRemove
        {
            get => _selectedMemberToRemove;
            set => SetProperty(ref _selectedMemberToRemove, value);
        }

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

        private UserRole _formGroupRole = UserRole.GeneralUser;
        public UserRole FormGroupRole
        {
            get => _formGroupRole;
            set => SetProperty(ref _formGroupRole, value);
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

        // ── Constructor ───────────────────────────────────────────────

        private readonly SqliteApprovalRepository _approvalRepo;

        public AdminViewModel(IUserRepository userRepo, IAppGroupRepository groupRepo,
            AdAuthService adService, SqliteApprovalRepository approvalRepo = null)
        {
            _userRepo     = userRepo;
            _groupRepo    = groupRepo;
            _adService    = adService;
            _approvalRepo = approvalRepo;

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
        }

        public async Task InitializeAsync()
        {
            await LoadUsersAsync();
            await LoadGroupsAsync();
            await LoadParticipantGroupMembersAsync();
            UpdateFirstRunState();
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
            FormRole        = user.Role;

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
                // 관리자 화면은 비활성 사용자도 표시 (SqliteAppUserRepository 직접 캐스팅)
                List<User> list;
                if (_userRepo is SqliteAppUserRepository sqliteRepo)
                    list = await sqliteRepo.GetAllUsersIncludeDisabledAsync();
                else
                    list = await _userRepo.GetAllUsersAsync();

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
                    FormRole        = adUser.Role;
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
                    Email     = FormEmail?.Trim(),
                    Role      = FormRole
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

                ClearUserForm();
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
            FormAdAccount   = user.AdAccount;
            FormDisplayName = user.Name;
            FormEmail       = user.Email;
            FormRole        = user.Role;
            IsUserEditMode  = true;
        }

        private void ClearUserForm()
        {
            _adSearchTimer.Stop();
            ClearAdSearchResults();
            _formAdAccount  = string.Empty;
            OnPropertyChanged(nameof(FormAdAccount));
            FormDisplayName = string.Empty;
            FormEmail       = string.Empty;
            FormRole        = UserRole.GeneralUser;
            IsUserEditMode  = false;
            SelectedUser    = null;
        }

        // ── 그룹 관리 ─────────────────────────────────────────────────

        private async Task LoadGroupsAsync()
        {
            try
            {
                var list = await _groupRepo.GetAllGroupsAsync();
                Groups = new ObservableCollection<AppGroup>(list);
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
                    Description = FormGroupDescription?.Trim(),
                    Role        = FormGroupRole
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
            if (SelectedGroup == null || SelectedMemberToAdd == null)
            {
                GroupStatusMessage = "그룹과 추가할 사용자를 선택하세요.";
                return;
            }
            try
            {
                await _groupRepo.AddGroupMemberAsync(SelectedGroup.GroupId, SelectedMemberToAdd.AdAccount);
                GroupStatusMessage = $"'{SelectedMemberToAdd.Name}' 그룹에 추가됨";
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
            if (SelectedGroup == null || SelectedMemberToRemove == null)
            {
                GroupStatusMessage = "그룹과 제거할 멤버를 선택하세요.";
                return;
            }
            var result = MessageBox.Show(
                $"'{SelectedMemberToRemove.Name}'을(를) '{SelectedGroup.GroupName}' 그룹에서 제거하시겠습니까?",
                "멤버 제거", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _groupRepo.RemoveGroupMemberAsync(SelectedGroup.GroupId, SelectedMemberToRemove.AdAccount);
                GroupStatusMessage = $"'{SelectedMemberToRemove.Name}' 그룹에서 제거됨";
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
            FormGroupRole        = UserRole.GeneralUser;
            IsGroupEditMode      = false;
            SelectedGroup        = null;
        }

        // ── 참여자 그룹 관리 ─────────────────────────────────────────

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

    }
}
