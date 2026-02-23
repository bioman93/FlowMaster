using System;
using System.IO;
using System.Net.Http;

namespace FlowMaster.Tests
{
    /// <summary>
    /// 공통 테스트 유틸리티
    /// </summary>
    internal static class TestHelpers
    {
        private static string _solutionRoot;

        /// <summary>
        /// FlowMaster.sln이 위치한 솔루션 루트 경로를 반환합니다.
        /// </summary>
        public static string GetSolutionRoot()
        {
            if (_solutionRoot != null) return _solutionRoot;

            // dotnet test는 .NET FW 어셈블리를 임시 경로에서 실행할 수 있으므로
            // 여러 시작 지점을 순서대로 시도합니다.
            var candidates = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetDirectoryName(typeof(TestHelpers).Assembly.Location),
                Environment.CurrentDirectory,
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;

                var dir = new DirectoryInfo(candidate);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "FlowMaster.sln")))
                    {
                        _solutionRoot = dir.FullName;
                        return _solutionRoot;
                    }
                    dir = dir.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                $"FlowMaster.sln을 찾을 수 없습니다. 탐색 경로: {AppDomain.CurrentDomain.BaseDirectory}");
        }

        /// <summary>
        /// Emulator(localhost:3900)가 현재 실행 중인지 확인합니다.
        /// </summary>
        public static bool IsEmulatorRunning()
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var response = client.GetAsync("http://localhost:3900/api/users")
                        .GetAwaiter().GetResult();
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ApprovalSystem(localhost:5002)가 현재 실행 중인지 확인합니다.
        /// </summary>
        public static bool IsApprovalSystemRunning()
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var response = client.GetAsync("http://localhost:5002/api/approvals/my-requests?userId=ping&pageSize=1")
                        .GetAwaiter().GetResult();
                    return (int)response.StatusCode < 500;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
