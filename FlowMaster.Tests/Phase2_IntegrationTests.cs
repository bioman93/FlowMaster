using System.Threading.Tasks;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 2: 통합 테스트 — Emulator 및 ApprovalSystem 실행 필요
    /// 서비스 미실행 시 테스트를 건너뛰어 CI 환경에서도 실패하지 않습니다.
    /// </summary>
    public class Phase2_IntegrationTests
    {
        // ── TC-I-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[I-01] [Emulator 필요] GetUsersAsync()는 Emulator 사용자를 반환해야 한다")]
        public async Task GetUsersAsync_EmulatorRunning_ReturnsRealUsers()
        {
            if (!TestHelpers.IsEmulatorRunning()) return; // 서비스 미실행 시 Skip

            var svc   = new EmulatorAuthService("http://localhost:3900");
            var users = await svc.GetUsersAsync();

            Assert.NotNull(users);
            Assert.True(users.Count > 0, "Emulator 사용자가 1명 이상 있어야 합니다.");
            Assert.True(svc.IsEmulatorAvailable);
        }

        // ── TC-I-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[I-02] [Emulator 필요] LoginAsync()는 JWT 토큰을 반환해야 한다")]
        public async Task LoginAsync_EmulatorRunning_ReturnsJwt()
        {
            if (!TestHelpers.IsEmulatorRunning()) return;

            var svc   = new EmulatorAuthService("http://localhost:3900");
            var token = await svc.LoginAsync("hong.gildong");

            Assert.NotNull(token);
            Assert.True(token.Length > 20, $"JWT 토큰이 너무 짧습니다: {token}");
            // JWT 형식: header.payload.signature
            Assert.Equal(3, token.Split('.').Length);
        }

        // ── TC-I-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[I-03] [Emulator+ApprovalSystem 필요] ApprovalApiClient는 JWT 헤더로 요청해야 한다")]
        public async Task ApprovalApiClient_WithJwt_SetsAuthHeader()
        {
            if (!TestHelpers.IsEmulatorRunning()) return;
            if (!TestHelpers.IsApprovalSystemRunning()) return;

            // Emulator에서 JWT 발급
            var authSvc = new EmulatorAuthService("http://localhost:3900");
            var token   = await authSvc.LoginAsync("kim.chulsoo");
            Assert.NotNull(token);

            // JWT를 ApprovalApiClient에 설정
            var apiClient = new ApprovalApiClient("http://localhost:5002/api");
            apiClient.SetAuthToken(token);

            // Authorization 헤더 확인
            var httpClientField = typeof(ApprovalApiClient).GetField(
                "_httpClient",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var httpClient = (System.Net.Http.HttpClient)httpClientField.GetValue(apiClient);
            var auth       = httpClient.DefaultRequestHeaders.Authorization;

            Assert.NotNull(auth);
            Assert.Equal("Bearer", auth.Scheme);
            Assert.Equal(token, auth.Parameter);
        }

        // ── TC-I-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[I-04] [Emulator 필요] Emulator 사용자에 Approver 역할이 매핑되어야 한다")]
        public async Task GetUsersAsync_EmulatorRunning_HasApproverRole()
        {
            if (!TestHelpers.IsEmulatorRunning()) return;

            var svc   = new EmulatorAuthService("http://localhost:3900");
            var users = await svc.GetUsersAsync();

            // kim.chulsoo는 GRP_Approvers 멤버 → Approver 역할
            Assert.Contains(users, u =>
                u.AdAccount == "kim.chulsoo" &&
                u.Role == FlowMaster.Domain.Models.UserRole.Approver);
        }

        // ── TC-I-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[I-05] [Emulator 필요] Emulator 사용자에 Admin 역할이 매핑되어야 한다")]
        public async Task GetUsersAsync_EmulatorRunning_HasAdminRole()
        {
            if (!TestHelpers.IsEmulatorRunning()) return;

            var svc   = new EmulatorAuthService("http://localhost:3900");
            var users = await svc.GetUsersAsync();

            // park.jihye는 GRP_Executives 멤버 → Admin 역할
            Assert.Contains(users, u =>
                u.AdAccount == "park.jihye" &&
                u.Role == FlowMaster.Domain.Models.UserRole.Admin);
        }
    }
}
