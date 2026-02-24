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
    /// AD Emulator(localhost:3900) 기반 인증 서비스.
    /// Emulator 실행 중: Emulator API에서 사용자 목록 조회.
    /// Emulator 미실행: 실제 Windows AD에서 사용자 조회 (AdAuthService 폴백).
    /// </summary>
    public class EmulatorAuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _adFallback;
        private readonly Action<string> _log;  // 로그 콜백 (AppLogger.Info 주입)

        private bool _isAvailable;
        private string _currentToken;

        // Circuit Breaker: ENABLE_AD=false(503) 응답 시 반복 호출 차단
        private bool _adLoginBlocked = false;
        private DateTime _adLoginBlockedUntil = DateTime.MinValue;
        private const int AdLoginBlockMinutes = 5;

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

        /// <param name="log">로그 출력 콜백. null이면 로그 비활성.</param>
        public EmulatorAuthService(string emulatorBaseUrl, IAuthService adFallback = null, Action<string> log = null)
        {
            _log = log ?? (_ => { });
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(emulatorBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(3)
            };
            _adFallback = adFallback ?? new MockAuthService();

            _log($"[EmulatorAuthService] 초기화: URL={emulatorBaseUrl}, Timeout=3s");
        }

        /// <summary>전체 사용자 목록 로드 — GET /api/users?enabled=true</summary>
        public async Task<List<User>> GetUsersAsync()
        {
            _log("[EmulatorAuthService] GetUsersAsync 호출");
            try
            {
                var response = await _httpClient.GetAsync("api/users?enabled=true");
                _log($"[EmulatorAuthService] Emulator 응답: {(int)response.StatusCode} {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _isAvailable = false;
                    _log("[EmulatorAuthService] 비정상 응답 → AD 폴백으로 전환");
                    return await _adFallback.GetUsersAsync();
                }

                var json = await response.Content.ReadAsStringAsync();
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
                _log($"[EmulatorAuthService] Emulator 사용자 {users.Count}명 로드 완료");
                return users;
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                _log($"[EmulatorAuthService] Emulator 연결 실패 ({ex.GetType().Name}: {ex.Message}) → AD 폴백으로 전환");
                return await _adFallback.GetUsersAsync();
            }
        }

        /// <summary>지정 계정으로 로그인 → JWT 반환.
        /// ENABLE_AD=false(503) 환경에서는 Circuit Breaker가 반복 호출을 차단하여 성능 저하 방지.</summary>
        public async Task<string> LoginAsync(string adAccount)
        {
            // Circuit Breaker: 이전에 503(AD 비활성)을 받았으면 차단 시간 동안 즉시 null 반환
            if (_adLoginBlocked && DateTime.Now < _adLoginBlockedUntil)
                return null;

            _log($"[EmulatorAuthService] LoginAsync: {adAccount}");
            try
            {
                var body = JsonConvert.SerializeObject(
                    new { username = adAccount, password = "test1234" });
                var content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/auth/login", content);

                // 503 = Emulator가 실행 중이지만 AD 기능 비활성(ENABLE_AD=false)
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    _adLoginBlocked = true;
                    _adLoginBlockedUntil = DateTime.Now.AddMinutes(AdLoginBlockMinutes);
                    _log($"[EmulatorAuthService] AD 비활성(503) → {AdLoginBlockMinutes}분간 로그인 시도 차단");
                    _currentToken = null;
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _log($"[EmulatorAuthService] 로그인 실패: {(int)response.StatusCode}");
                    _currentToken = null;
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                _currentToken = obj["token"]?.ToString();
                _isAvailable = true;
                _adLoginBlocked = false; // 성공 시 차단 해제
                _log($"[EmulatorAuthService] 로그인 성공, 토큰 발급 완료");
                return _currentToken;
            }
            catch (Exception ex)
            {
                _log($"[EmulatorAuthService] 로그인 예외 ({ex.GetType().Name}: {ex.Message}) → 무인증 모드");
                _isAvailable = false;
                _currentToken = null;
                return null;
            }
        }

        /// <summary>현재 컨텍스트 사용자 조회</summary>
        public async Task<User> GetCurrentContextUserAsync()
        {
            _log("[EmulatorAuthService] GetCurrentContextUserAsync 호출");
            try
            {
                var response = await _httpClient.GetAsync("api/context/user");
                _log($"[EmulatorAuthService] context/user 응답: {(int)response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _log("[EmulatorAuthService] 비정상 응답 → AD 폴백 사용");
                    return await _adFallback.GetCurrentContextUserAsync();
                }

                var json = await response.Content.ReadAsStringAsync();
                var item = JObject.Parse(json);
                var memberOf = item["memberOf"]?.ToObject<List<string>>() ?? new List<string>();
                var user = new User
                {
                    UserId    = item["sAMAccountName"]?.ToString(),
                    AdAccount = item["sAMAccountName"]?.ToString(),
                    Name      = item["displayName"]?.ToString(),
                    Email     = item["email"]?.ToString(),
                    Role      = ResolveRole(memberOf)
                };
                _log($"[EmulatorAuthService] 컨텍스트 사용자: {user.Name} ({user.AdAccount})");
                return user;
            }
            catch (Exception ex)
            {
                _log($"[EmulatorAuthService] GetCurrentContextUserAsync 예외 ({ex.GetType().Name}: {ex.Message}) → AD 폴백 사용");
                return await _adFallback.GetCurrentContextUserAsync();
            }
        }

        private static UserRole ResolveRole(List<string> memberOf)
        {
            foreach (var group in memberOf)
                if (GroupRoleMap.TryGetValue(group, out var role) && role == UserRole.Admin)
                    return UserRole.Admin;
            foreach (var group in memberOf)
                if (GroupRoleMap.TryGetValue(group, out var role) && role == UserRole.Approver)
                    return UserRole.Approver;
            return UserRole.GeneralUser;
        }
    }
}
