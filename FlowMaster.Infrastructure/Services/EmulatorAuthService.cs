using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// AD Emulator(localhost:3900) 기반 인증 서비스
    /// Emulator 미실행 시 Mock 사용자 4명으로 자동 폴백
    /// </summary>
    public class EmulatorAuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly MockUserRepository _mockFallback;

        private bool _isAvailable;
        private string _currentToken;

        public bool IsEmulatorAvailable => _isAvailable;
        public string CurrentToken => _currentToken;

        // Emulator AD 그룹 → FlowMaster UserRole 매핑
        private static readonly Dictionary<string, UserRole> GroupRoleMap =
            new Dictionary<string, UserRole>
            {
                { "GRP_Executives", UserRole.Admin },
                { "GRP_Managers",   UserRole.Approver },
                { "GRP_Approvers",  UserRole.Approver },
            };

        public EmulatorAuthService(string emulatorBaseUrl)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(emulatorBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(3)
            };
            _mockFallback = new MockUserRepository();
        }

        /// <summary>
        /// 전체 사용자 목록 로드
        /// GET /api/users?enabled=true
        /// </summary>
        public async Task<List<User>> GetUsersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/users?enabled=true");
                if (!response.IsSuccessStatusCode)
                {
                    _isAvailable = false;
                    return await _mockFallback.GetAllUsersAsync();
                }

                var json = await response.Content.ReadAsStringAsync();
                // Emulator API 응답 형식: { success, count, data: [...] }
                var root = JObject.Parse(json);
                var array = root["data"] as JArray ?? JArray.Parse(json);
                var users = new List<User>();

                foreach (var item in array)
                {
                    var memberOf = item["memberOf"]?.ToObject<List<string>>() ?? new List<string>();
                    users.Add(new User
                    {
                        UserId    = item["sAMAccountName"]?.ToString(),
                        AdAccount = item["sAMAccountName"]?.ToString(),
                        Name      = item["displayName"]?.ToString(),
                        Email     = item["email"]?.ToString(),
                        Role      = ResolveRole(memberOf)
                    });
                }

                _isAvailable = true;
                return users;
            }
            catch
            {
                // Emulator 미실행 시 Mock 폴백
                _isAvailable = false;
                return await _mockFallback.GetAllUsersAsync();
            }
        }

        /// <summary>
        /// 지정 계정으로 로그인 → JWT 반환
        /// POST /api/auth/login { username, password }
        /// Emulator 미실행 시 null 반환 (무인증 모드로 계속 동작)
        /// </summary>
        public async Task<string> LoginAsync(string adAccount)
        {
            try
            {
                var body = JsonConvert.SerializeObject(
                    new { username = adAccount, password = "test1234" });
                var content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/auth/login", content);
                if (!response.IsSuccessStatusCode)
                {
                    _currentToken = null;
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                _currentToken = obj["token"]?.ToString();
                _isAvailable = true;
                return _currentToken;
            }
            catch
            {
                // Emulator 미실행 시 null 반환 (ApprovalApiClient 무인증 모드)
                _isAvailable = false;
                _currentToken = null;
                return null;
            }
        }

        /// <summary>
        /// Emulator Current Context 사용자 조회
        /// GET /api/context/user — Emulator 대시보드에서 선택된 현재 사용자
        /// Emulator 미실행 시 null 반환
        /// </summary>
        public async Task<User> GetCurrentContextUserAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/context/user");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var item = JObject.Parse(json);
                var memberOf = item["memberOf"]?.ToObject<List<string>>() ?? new List<string>();
                return new User
                {
                    UserId    = item["sAMAccountName"]?.ToString(),
                    AdAccount = item["sAMAccountName"]?.ToString(),
                    Name      = item["displayName"]?.ToString(),
                    Email     = item["email"]?.ToString(),
                    Role      = ResolveRole(memberOf)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Emulator 그룹 목록으로 FlowMaster UserRole 계산
        /// 우선순위: Admin > Approver > GeneralUser
        /// </summary>
        private static UserRole ResolveRole(List<string> memberOf)
        {
            foreach (var group in memberOf)
            {
                if (GroupRoleMap.TryGetValue(group, out var role) && role == UserRole.Admin)
                    return UserRole.Admin;
            }
            foreach (var group in memberOf)
            {
                if (GroupRoleMap.TryGetValue(group, out var role) && role == UserRole.Approver)
                    return UserRole.Approver;
            }
            return UserRole.GeneralUser;
        }
    }
}
