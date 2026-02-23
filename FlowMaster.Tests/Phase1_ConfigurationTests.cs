using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using FlowMaster.Infrastructure.Services;
using Xunit;

namespace FlowMaster.Tests
{
    /// <summary>
    /// Phase 1: 포트 충돌 수정 및 설정 파일 검증
    /// </summary>
    public class Phase1_ConfigurationTests
    {
        private static XDocument LoadAppConfig()
        {
            var path = Path.Combine(
                TestHelpers.GetSolutionRoot(),
                "FlowMaster.Desktop", "App.config");
            return XDocument.Load(path);
        }

        private static string GetAppSetting(XDocument doc, string key)
        {
            return doc.Root
                ?.Element("appSettings")
                ?.Elements("add")
                .FirstOrDefault(e => e.Attribute("key")?.Value == key)
                ?.Attribute("value")?.Value;
        }

        // ── TC-P1-01 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-01] App.config 파일이 존재해야 한다")]
        public void AppConfig_FileExists()
        {
            var path = Path.Combine(
                TestHelpers.GetSolutionRoot(),
                "FlowMaster.Desktop", "App.config");
            Assert.True(File.Exists(path), $"App.config 파일이 없습니다: {path}");
        }

        // ── TC-P1-02 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-02] ApprovalApi:BaseUrl이 포트 5002로 설정되어야 한다")]
        public void AppConfig_ApprovalApiBaseUrl_Is5002()
        {
            var doc = LoadAppConfig();
            var url = GetAppSetting(doc, "ApprovalApi:BaseUrl");

            Assert.NotNull(url);
            Assert.Contains("5002", url);
            Assert.Equal("http://localhost:5002/api", url);
        }

        // ── TC-P1-03 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-03] ApprovalApi:BaseUrl에 포트 5001이 없어야 한다 (충돌 방지)")]
        public void AppConfig_ApprovalApiBaseUrl_NotContains5001()
        {
            var doc = LoadAppConfig();
            var url = GetAppSetting(doc, "ApprovalApi:BaseUrl");

            Assert.NotNull(url);
            Assert.DoesNotContain("5001", url);
        }

        // ── TC-P1-04 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-04] Emulator:BaseUrl이 포트 3900으로 설정되어야 한다")]
        public void AppConfig_EmulatorBaseUrl_Is3900()
        {
            var doc = LoadAppConfig();
            var url = GetAppSetting(doc, "Emulator:BaseUrl");

            Assert.NotNull(url);
            Assert.Equal("http://localhost:3900", url);
        }

        // ── TC-P1-05 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-05] ApprovalApiClient 기본 URL이 5002를 포함해야 한다")]
        public void ApprovalApiClient_DefaultUrl_Contains5002()
        {
            var client = new ApprovalApiClient();

            // private 필드 _baseUrl 검사 (Reflection)
            var field = typeof(ApprovalApiClient).GetField(
                "_baseUrl", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(field);
            var url = field.GetValue(client) as string;
            Assert.NotNull(url);
            Assert.Contains("5002", url);
        }

        // ── TC-P1-06 ─────────────────────────────────────────────────────────
        [Fact(DisplayName = "[P1-06] start-approval-api.bat 파일이 5002 포트를 사용해야 한다")]
        public void StartApprovalApiBat_Uses5002Port()
        {
            var path = Path.Combine(
                TestHelpers.GetSolutionRoot(),
                "scripts", "start-approval-api.bat");

            Assert.True(File.Exists(path), "start-approval-api.bat이 없습니다.");

            var content = File.ReadAllText(path);
            Assert.Contains("5002", content);
            Assert.DoesNotContain("localhost:5001\"", content); // 5001 하드코딩 제거 확인
        }
    }
}
