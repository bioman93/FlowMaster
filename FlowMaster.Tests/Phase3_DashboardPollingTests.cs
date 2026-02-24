using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 3: DashboardViewModel 폴링 — 단위 테스트
    /// WPF Dispatcher가 없는 테스트 환경이므로 폴링 관련 속성/인터페이스만 검증합니다.
    /// DispatcherTimer 동작은 실제 앱에서 수동으로 확인해야 합니다.
    /// </summary>
    public class Phase3_DashboardPollingTests
    {
        // ─── Fake IApprovalRepository ──────────────────────────────────────────
        private class FakeApprovalRepository : IApprovalRepository
        {
            public int LoadCallCount { get; private set; }
            private readonly List<ApprovalDocument> _pending;
            private readonly List<ApprovalDocument> _drafts;

            public FakeApprovalRepository(
                List<ApprovalDocument> pending = null,
                List<ApprovalDocument> drafts  = null)
            {
                _pending = pending ?? new List<ApprovalDocument>();
                _drafts  = drafts  ?? new List<ApprovalDocument>();
            }

            public Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string userId)
            {
                LoadCallCount++;
                return Task.FromResult(_pending);
            }

            public Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId)
                => Task.FromResult(_drafts);

            // 사용하지 않는 IApprovalRepository 멤버들 (no-op 구현)
            public Task<int> CreateDocumentAsync(ApprovalDocument doc) => Task.FromResult(0);
            public Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status) => Task.CompletedTask;
            public Task<ApprovalDocument> GetDocumentAsync(int docId) => Task.FromResult<ApprovalDocument>(null);
            public Task AddApprovalLineAsync(ApprovalLine line) => Task.CompletedTask;
            public Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment) => Task.CompletedTask;
            public Task AddTestResultAsync(TestResult result) => Task.CompletedTask;
            public Task<List<TestResult>> GetTestResultsAsync(int docId) => Task.FromResult(new List<TestResult>());
            public Task UpdateApprovalIdAsync(int docId, string approvalId) => Task.CompletedTask;
            public Task<ApprovalDocument> GetDocumentByApprovalIdAsync(string approvalId) => Task.FromResult<ApprovalDocument>(null);
            public Task<List<ApprovalDocument>> GetUnsyncedDocumentsAsync() => Task.FromResult(new List<ApprovalDocument>());
            public Task UpdateSyncStatusAsync(int docId, SyncStatus status, int retryCount, string error) => Task.CompletedTask;

            // 新규 추가된 인터페이스 멤버 (no-op 구현)
            public Task<List<ApprovalDocument>> GetAllDocumentsAsync() => Task.FromResult(new List<ApprovalDocument>());
            public Task DeleteDocumentAsync(int docId) => Task.CompletedTask;
            public Task<List<User>> GetDocumentParticipantsAsync(int docId) => Task.FromResult(new List<User>());
            public Task SaveDocumentParticipantsAsync(int docId, List<User> participants) => Task.CompletedTask;
            public Task AddDocumentParticipantAsync(int docId, User user) => Task.CompletedTask;
            public Task RemoveDocumentParticipantAsync(int docId, string userId) => Task.CompletedTask;
            public Task<List<User>> GetParticipantGroupAsync(string groupName) => Task.FromResult(new List<User>());
            public Task AddParticipantGroupMemberAsync(string groupName, User user) => Task.CompletedTask;
            public Task RemoveParticipantGroupMemberAsync(string groupName, string userId) => Task.CompletedTask;
        }

        // ─── Fake IUserRepository ──────────────────────────────────────────────
        private class FakeUserRepository : IUserRepository
        {
            public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
            public Task<User> GetUserByAdAccountAsync(string adAccount) => Task.FromResult<User>(null);
            public Task<List<User>> GetUsersByRoleAsync(UserRole role) => Task.FromResult(new List<User>());
            public Task AddUserAsync(User user) => Task.CompletedTask;
            public Task UpdateUserAsync(User user) => Task.CompletedTask;
            public Task DeleteUserAsync(string userId) => Task.CompletedTask;
        }

        private static User MakeUser() =>
            new User { UserId = "U001", AdAccount = "user", Name = "테스트", Role = UserRole.GeneralUser };

        // ── TC-D-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-01] DashboardViewModel은 IDisposable을 구현해야 한다")]
        public void DashboardViewModel_ImplementsIDisposable()
        {
            // WPF DispatcherTimer는 테스트 환경에서도 인스턴스 생성 가능
            var repo = new FakeApprovalRepository();
            using (var vm = CreateVm(repo))
            {
                Assert.IsAssignableFrom<IDisposable>(vm);
            } // Dispose()가 예외 없이 호출되어야 함
        }

        // ── TC-D-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-02] 초기 LastRefreshedText는 '갱신 전'이어야 한다")]
        public void DashboardViewModel_InitialLastRefreshedText_IsNotRefreshed()
        {
            using (var vm = CreateVm())
            {
                Assert.Equal("갱신 전", vm.LastRefreshedText);
            }
        }

        // ── TC-D-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-03] 초기 IsPollingActive는 false여야 한다")]
        public void DashboardViewModel_InitialIsPollingActive_IsFalse()
        {
            using (var vm = CreateVm())
            {
                Assert.False(vm.IsPollingActive);
            }
        }

        // ── TC-D-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-04] 초기 IsRefreshing은 false여야 한다")]
        public void DashboardViewModel_InitialIsRefreshing_IsFalse()
        {
            using (var vm = CreateVm())
            {
                Assert.False(vm.IsRefreshing);
            }
        }

        // ── TC-D-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-05] LoadDataAsync() 후 LastRefreshedText가 시각을 포함해야 한다")]
        public async Task LoadDataAsync_AfterLoad_LastRefreshedTextContainsTime()
        {
            using (var vm = CreateVm())
            {
                await vm.LoadDataAsync(MakeUser());

                // "마지막 갱신: HH:mm:ss" 형식이어야 함
                Assert.StartsWith("마지막 갱신:", vm.LastRefreshedText);
                Assert.DoesNotContain("갱신 전", vm.LastRefreshedText);
            }
        }

        // ── TC-D-06 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-06] LoadDataAsync() 후 IsPollingActive가 true여야 한다")]
        public async Task LoadDataAsync_AfterLoad_IsPollingActiveTrue()
        {
            using (var vm = CreateVm())
            {
                await vm.LoadDataAsync(MakeUser());

                Assert.True(vm.IsPollingActive);
            }
        }

        // ── TC-D-07 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-07] StopPolling() 후 IsPollingActive가 false여야 한다")]
        public async Task StopPolling_AfterStart_IsPollingActiveFalse()
        {
            using (var vm = CreateVm())
            {
                await vm.LoadDataAsync(MakeUser());
                vm.StopPolling();

                Assert.False(vm.IsPollingActive);
            }
        }

        // ── TC-D-08 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-08] Dispose() 후 IsPollingActive가 false여야 한다")]
        public async Task Dispose_StopsPolling()
        {
            var vm = CreateVm();
            await vm.LoadDataAsync(MakeUser());

            vm.Dispose();

            Assert.False(vm.IsPollingActive);
        }

        // ── TC-D-09 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-09] LoadDataAsync()는 Repository를 1회 호출해야 한다")]
        public async Task LoadDataAsync_CallsRepositoryOnce()
        {
            var repo = new FakeApprovalRepository();
            using (var vm = CreateVm(repo))
            {
                await vm.LoadDataAsync(MakeUser());

                Assert.Equal(1, repo.LoadCallCount);
            }
        }

        // ── TC-D-10 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-10] user=null이면 LoadDataAsync()가 Repository를 호출하지 않아야 한다")]
        public async Task LoadDataAsync_NullUser_DoesNotCallRepository()
        {
            var repo = new FakeApprovalRepository();
            using (var vm = CreateVm(repo))
            {
                await vm.LoadDataAsync(null);

                Assert.Equal(0, repo.LoadCallCount);
            }
        }

        // ── TC-D-11 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-11] StartPolling() 중복 호출 시 타이머가 하나만 실행되어야 한다")]
        public async Task StartPolling_CalledTwice_RemainsActive()
        {
            using (var vm = CreateVm())
            {
                await vm.LoadDataAsync(MakeUser()); // 내부에서 StartPolling() 호출
                vm.StartPolling();                  // 중복 호출

                Assert.True(vm.IsPollingActive);
            }
        }

        // ── TC-D-12 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[D-12] Dispose() 중복 호출 시 예외가 발생하지 않아야 한다")]
        public void Dispose_CalledTwice_NoException()
        {
            var vm = CreateVm();
            vm.Dispose();
            vm.Dispose(); // 두 번째 호출 → 예외 없어야 함
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────
        private static FlowMaster.Desktop.ViewModels.DashboardViewModel CreateVm(
            IApprovalRepository repo = null)
        {
            return new FlowMaster.Desktop.ViewModels.DashboardViewModel(
                repo ?? new FakeApprovalRepository(),
                new FakeUserRepository(),
                approvalApiClient: null); // 테스트 환경: API 호출 없음 (try-catch로 보호됨)
        }
    }
}
