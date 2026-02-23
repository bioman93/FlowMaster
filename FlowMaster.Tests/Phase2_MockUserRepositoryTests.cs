using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 2: MockUserRepository — GetAllUsersAsync() 신규 메서드 검증
    /// </summary>
    public class Phase2_MockUserRepositoryTests
    {
        private readonly MockUserRepository _repo = new MockUserRepository();

        // ── TC-M-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-01] GetAllUsersAsync()는 4명을 반환해야 한다")]
        public async Task GetAllUsersAsync_ReturnsFourUsers()
        {
            var users = await _repo.GetAllUsersAsync();
            Assert.Equal(4, users.Count);
        }

        // ── TC-M-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-02] GetAllUsersAsync()와 GetAllTestUsers()의 결과가 동일해야 한다")]
        public async Task GetAllUsersAsync_MatchesGetAllTestUsers()
        {
            var fromAsync = await _repo.GetAllUsersAsync();
            var fromSync = _repo.GetAllTestUsers();

            Assert.Equal(fromSync.Count, fromAsync.Count);
            foreach (var user in fromSync)
                Assert.Contains(fromAsync, u => u.AdAccount == user.AdAccount);
        }

        // ── TC-M-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-03] GetUserByAdAccountAsync()는 대소문자 무관 조회해야 한다")]
        public async Task GetUserByAdAccountAsync_CaseInsensitive()
        {
            var user1 = await _repo.GetUserByAdAccountAsync("approver");
            var user2 = await _repo.GetUserByAdAccountAsync("APPROVER");

            Assert.NotNull(user1);
            Assert.NotNull(user2);
            Assert.Equal(user1.AdAccount, user2.AdAccount);
        }

        // ── TC-M-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-04] GetUsersByRoleAsync(Approver)는 결재자만 반환해야 한다")]
        public async Task GetUsersByRoleAsync_FiltersByRole()
        {
            var approvers = await _repo.GetUsersByRoleAsync(UserRole.Approver);

            Assert.True(approvers.Count >= 1);
            Assert.All(approvers, u => Assert.Equal(UserRole.Approver, u.Role));
        }

        // ── TC-M-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-05] GetUserByAdAccountAsync()는 존재하지 않는 계정에 null을 반환해야 한다")]
        public async Task GetUserByAdAccountAsync_UnknownAccount_ReturnsNull()
        {
            var user = await _repo.GetUserByAdAccountAsync("nonexistent.account");
            Assert.Null(user);
        }

        // ── TC-M-06 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[M-06] Mock 사용자 역할 구성이 올바르게 설정되어야 한다")]
        public async Task MockUsers_HaveCorrectRoles()
        {
            var users = await _repo.GetAllUsersAsync();

            var generalUser = users.FirstOrDefault(u => u.AdAccount == "user");
            var approver    = users.FirstOrDefault(u => u.AdAccount == "approver");
            var admin       = users.FirstOrDefault(u => u.AdAccount == "admin");

            Assert.NotNull(generalUser);
            Assert.NotNull(approver);
            Assert.NotNull(admin);

            Assert.Equal(UserRole.GeneralUser, generalUser.Role);
            Assert.Equal(UserRole.Approver,    approver.Role);
            Assert.Equal(UserRole.Admin,        admin.Role);
        }
    }
}
