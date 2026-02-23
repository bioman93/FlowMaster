using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 2: EmulatorAuthService — Emulator 미실행 시 Mock 폴백 동작 검증
    /// (Emulator가 실행 중이지 않은 환경에서도 반드시 통과해야 하는 테스트)
    /// </summary>
    public class Phase2_EmulatorAuthServiceTests
    {
        // 존재하지 않는 주소 → Emulator 미실행과 동일한 효과
        private static EmulatorAuthService CreateOfflineService()
            => new EmulatorAuthService("http://localhost:19999");

        // ── TC-E-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-01] Emulator 미실행 시 GetUsersAsync()는 null이 아닌 목록을 반환해야 한다")]
        public async Task GetUsersAsync_EmulatorDown_ReturnsNonNullList()
        {
            var svc = CreateOfflineService();

            var users = await svc.GetUsersAsync();

            Assert.NotNull(users);
        }

        // ── TC-E-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-02] Emulator 미실행 시 GetUsersAsync()는 Mock 4명을 반환해야 한다")]
        public async Task GetUsersAsync_EmulatorDown_ReturnsFourMockUsers()
        {
            var svc = CreateOfflineService();

            var users = await svc.GetUsersAsync();

            Assert.Equal(4, users.Count);
        }

        // ── TC-E-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-03] Emulator 미실행 시 IsEmulatorAvailable은 false여야 한다")]
        public async Task GetUsersAsync_EmulatorDown_IsAvailableFalse()
        {
            var svc = CreateOfflineService();

            await svc.GetUsersAsync();

            Assert.False(svc.IsEmulatorAvailable);
        }

        // ── TC-E-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-04] Emulator 미실행 시 LoginAsync()는 null을 반환해야 한다")]
        public async Task LoginAsync_EmulatorDown_ReturnsNull()
        {
            var svc = CreateOfflineService();

            var token = await svc.LoginAsync("approver");

            Assert.Null(token);
        }

        // ── TC-E-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-05] Emulator 미실행 시 CurrentToken은 null이어야 한다")]
        public async Task LoginAsync_EmulatorDown_CurrentTokenNull()
        {
            var svc = CreateOfflineService();

            await svc.LoginAsync("approver");

            Assert.Null(svc.CurrentToken);
        }

        // ── TC-E-06 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-06] Mock 폴백 사용자 목록에 'approver' 계정이 포함되어야 한다")]
        public async Task GetUsersAsync_EmulatorDown_ContainsApproverAccount()
        {
            var svc = CreateOfflineService();

            var users = await svc.GetUsersAsync();

            Assert.Contains(users, u => u.AdAccount == "approver");
        }

        // ── TC-E-07 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-07] Mock 폴백 사용자 중 Approver 역할이 1명 이상 있어야 한다")]
        public async Task GetUsersAsync_EmulatorDown_HasAtLeastOneApprover()
        {
            var svc = CreateOfflineService();

            var users = await svc.GetUsersAsync();

            Assert.Contains(users, u => u.Role == UserRole.Approver);
        }

        // ── TC-E-08 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[E-08] Mock 폴백 사용자 중 Admin 역할이 1명 이상 있어야 한다")]
        public async Task GetUsersAsync_EmulatorDown_HasAtLeastOneAdmin()
        {
            var svc = CreateOfflineService();

            var users = await svc.GetUsersAsync();

            Assert.Contains(users, u => u.Role == UserRole.Admin);
        }
    }
}
