using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 2: ApprovalApiClient — SetAuthToken() 및 URL 검증
    /// </summary>
    public class Phase2_ApprovalApiClientTests
    {
        // HttpClient를 꺼내기 위한 Reflection 헬퍼
        private static HttpClient GetHttpClient(ApprovalApiClient apiClient)
        {
            var field = typeof(ApprovalApiClient).GetField(
                "_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
            return (HttpClient)field.GetValue(apiClient);
        }

        // ── TC-A-01 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-01] SetAuthToken()은 Authorization: Bearer 헤더를 설정해야 한다")]
        public void SetAuthToken_ValidToken_SetsBearerHeader()
        {
            var client = new ApprovalApiClient("http://localhost:5002/api");
            client.SetAuthToken("test.jwt.token");

            var httpClient = GetHttpClient(client);
            var auth = httpClient.DefaultRequestHeaders.Authorization;

            Assert.NotNull(auth);
            Assert.Equal("Bearer", auth.Scheme);
            Assert.Equal("test.jwt.token", auth.Parameter);
        }

        // ── TC-A-02 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-02] SetAuthToken(null)은 Authorization 헤더를 제거해야 한다")]
        public void SetAuthToken_Null_RemovesAuthHeader()
        {
            var client = new ApprovalApiClient("http://localhost:5002/api");
            client.SetAuthToken("initial.token");
            client.SetAuthToken(null);

            var httpClient = GetHttpClient(client);
            var auth = httpClient.DefaultRequestHeaders.Authorization;

            Assert.Null(auth);
        }

        // ── TC-A-03 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-03] SetAuthToken(\"\")은 Authorization 헤더를 제거해야 한다")]
        public void SetAuthToken_EmptyString_RemovesAuthHeader()
        {
            var client = new ApprovalApiClient("http://localhost:5002/api");
            client.SetAuthToken("some.token");
            client.SetAuthToken(string.Empty);

            var httpClient = GetHttpClient(client);
            Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
        }

        // ── TC-A-04 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-04] SetAuthToken()은 토큰 교체가 가능해야 한다")]
        public void SetAuthToken_Overwrite_UpdatesToken()
        {
            var client = new ApprovalApiClient("http://localhost:5002/api");
            client.SetAuthToken("token.v1");
            client.SetAuthToken("token.v2");

            var httpClient = GetHttpClient(client);
            var auth = httpClient.DefaultRequestHeaders.Authorization;

            Assert.NotNull(auth);
            Assert.Equal("token.v2", auth.Parameter);
        }

        // ── TC-A-05 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-05] ApprovalApiClient는 전달받은 URL을 그대로 사용해야 한다")]
        public void Constructor_CustomUrl_IsStored()
        {
            var client = new ApprovalApiClient("http://custom-host:9999/api");
            var field  = typeof(ApprovalApiClient).GetField(
                "_baseUrl", BindingFlags.NonPublic | BindingFlags.Instance);
            var url = field.GetValue(client) as string;

            Assert.Equal("http://custom-host:9999/api", url);
        }

        // ── TC-A-06 ──────────────────────────────────────────────────────────
        [Fact(DisplayName = "[A-06] ApprovalApiClient 기본 URL에 후행 슬래시가 없어야 한다")]
        public void Constructor_DefaultUrl_NoTrailingSlash()
        {
            var client = new ApprovalApiClient("http://localhost:5002/api/");
            var field  = typeof(ApprovalApiClient).GetField(
                "_baseUrl", BindingFlags.NonPublic | BindingFlags.Instance);
            var url = field.GetValue(client) as string;

            Assert.False(url.EndsWith("/"), $"후행 슬래시가 있습니다: {url}");
        }
    }
}
