using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 2: EmulatorUserRepository — IUserRepository 인터페이스 구현 검증
    /// FakeAuthService를 통해 Emulator 없이도 모든 테스트 실행 가능
    /// </summary>
    public class Phase2_EmulatorUserRepositoryTests
    {
        // ─── Fake IAuthService ────────────────────────────────────────────────
        private class FakeAuthService : IAuthService
        {
            private readonly List<User> _users;
            public bool IsEmulatorAvailable => true;
            public string CurrentToken => "fake.jwt.token";

            public FakeAuthService()
            {
                _users = new List<User>
                {
                    new User { UserId = "u1", AdAccount = "alice",   Name = "Alice",   Role = UserRole.GeneralUser },
                    new User { UserId = "u2", AdAccount = "bob",     Name = "Bob",     Role = UserRole.Approver    },
                    new User { UserId = "u3", AdAccount = "charlie", Name = "Charlie", Role = UserRole.Admin       },
                };
            }

            public Task<List<User>> GetUsersAsync() => Task.FromResult(_users);
            public Task<string> LoginAsync(string adAccount) => Task.FromResult(CurrentToken);
        }

        private static EmulatorUserRepository CreateRepo()
            => new EmulatorUserRepository(new FakeAuthService());

        // ── TC-R-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-01] GetAllUsersAsync()는 FakeAuthService에서 3명을 반환해야 한다")]
        public async Task GetAllUsersAsync_ReturnsUsersFromAuthService()
        {
            var repo = CreateRepo();

            var users = await repo.GetAllUsersAsync();

            Assert.Equal(3, users.Count);
        }

        // ── TC-R-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-02] GetAllUsersAsync() 두 번 호출 시 동일한 참조(캐시)를 반환해야 한다")]
        public async Task GetAllUsersAsync_SecondCall_ReturnsCachedList()
        {
            var repo = CreateRepo();

            var first  = await repo.GetAllUsersAsync();
            var second = await repo.GetAllUsersAsync();

            Assert.Same(first, second);
        }

        // ── TC-R-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-03] GetUserByAdAccountAsync()는 정확한 계정을 조회해야 한다")]
        public async Task GetUserByAdAccountAsync_ExactMatch_ReturnsUser()
        {
            var repo = CreateRepo();

            var user = await repo.GetUserByAdAccountAsync("bob");

            Assert.NotNull(user);
            Assert.Equal("bob", user.AdAccount);
        }

        // ── TC-R-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-04] GetUserByAdAccountAsync()는 대소문자 구분 없이 조회해야 한다")]
        public async Task GetUserByAdAccountAsync_CaseInsensitive_ReturnsUser()
        {
            var repo = CreateRepo();

            var lower = await repo.GetUserByAdAccountAsync("alice");
            var upper = await repo.GetUserByAdAccountAsync("ALICE");

            Assert.NotNull(lower);
            Assert.NotNull(upper);
            Assert.Equal(lower.AdAccount, upper.AdAccount);
        }

        // ── TC-R-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-05] GetUserByAdAccountAsync()는 없는 계정에 null을 반환해야 한다")]
        public async Task GetUserByAdAccountAsync_NotFound_ReturnsNull()
        {
            var repo = CreateRepo();

            var user = await repo.GetUserByAdAccountAsync("nonexistent");

            Assert.Null(user);
        }

        // ── TC-R-06 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-06] GetUsersByRoleAsync(Approver)는 Approver만 반환해야 한다")]
        public async Task GetUsersByRoleAsync_Approver_ReturnsOnlyApprovers()
        {
            var repo = CreateRepo();

            var approvers = await repo.GetUsersByRoleAsync(UserRole.Approver);

            Assert.True(approvers.Count >= 1);
            Assert.All(approvers, u => Assert.Equal(UserRole.Approver, u.Role));
        }

        // ── TC-R-07 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-07] GetUsersByRoleAsync(Admin)는 Admin만 반환해야 한다")]
        public async Task GetUsersByRoleAsync_Admin_ReturnsOnlyAdmins()
        {
            var repo = CreateRepo();

            var admins = await repo.GetUsersByRoleAsync(UserRole.Admin);

            Assert.Single(admins);
            Assert.Equal("charlie", admins[0].AdAccount);
        }

        // ── TC-R-08 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[R-08] AddUserAsync()는 예외 없이 완료되어야 한다 (no-op)")]
        public async Task AddUserAsync_NoOp_CompletesWithoutException()
        {
            var repo = CreateRepo();

            // 예외가 발생하지 않으면 성공
            await repo.AddUserAsync(new User { AdAccount = "dave" });
        }
    }
}
