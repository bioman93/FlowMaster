using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Desktop.Services
{
    /// <summary>
    /// 실제 Windows Active Directory 기반 인증 서비스.
    /// GetCurrentContextUserAsync(): 현재 Windows 로그인 사용자를 AD에서 단일 조회합니다.
    /// GetUsersAsync(): 현재 사용자 1명만 반환 (앱 등록 사용자 목록은 SqliteAppUserRepository 사용).
    /// LookupAdUserAsync(): 관리자 화면에서 특정 AD 계정을 조회할 때 사용합니다.
    /// </summary>
    public class AdAuthService : IAuthService
    {
        private readonly IReadOnlyList<string> _adminGroups;
        private readonly IReadOnlyList<string> _approverGroups;

        public AdAuthService(string adminGroups = null, string approverGroups = null)
        {
            _adminGroups    = Split(adminGroups    ?? "GRP_Executives");
            _approverGroups = Split(approverGroups ?? "GRP_Managers,GRP_Approvers");

            AppLogger.Info("[AdAuthService] 초기화 완료");
            AppLogger.Info($"[AdAuthService] AdminGroups    : {string.Join(", ", _adminGroups)}");
            AppLogger.Info($"[AdAuthService] ApproverGroups : {string.Join(", ", _approverGroups)}");
        }

        public bool IsEmulatorAvailable => false;
        public string CurrentToken => null;

        public Task<string> LoginAsync(string adAccount) => Task.FromResult<string>(null);

        /// <summary>
        /// 도메인 가입 여부 확인.
        /// 로컬 계정(비도메인)이면 false → PrincipalContext(Domain) 호출 생략.
        /// </summary>
        private static bool IsDomainJoined() =>
            !string.Equals(Environment.UserDomainName, Environment.MachineName,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>현재 Windows 로그인 계정을 AD에서 조회합니다.</summary>
        public async Task<User> GetCurrentContextUserAsync()
        {
            try
            {
                var sam = GetCurrentSamAccountName();
                AppLogger.Info($"[AdAuthService] GetCurrentContextUserAsync: Windows 계정 = {sam}");

                // 도메인 미가입 머신에서는 PrincipalContext(Domain) 호출 시 무한 대기 발생
                // → 즉시 로컬 사용자 반환
                if (!IsDomainJoined())
                {
                    AppLogger.Info("[AdAuthService] 도메인 미가입 머신 → 로컬 사용자 즉시 반환");
                    return CreateLocalUser(sam);
                }

                var user = await Task.Run(() => QuerySingleUser(sam));
                if (user != null)
                    AppLogger.Info($"[AdAuthService] 현재 사용자 AD 조회 성공: {user.Name} / Role={user.Role}");
                else
                    AppLogger.Warn("[AdAuthService] 현재 사용자 AD 조회 실패 → 로컬 계정으로 폴백");

                return user ?? CreateLocalUser(sam);
            }
            catch (Exception ex)
            {
                AppLogger.Error("[AdAuthService] GetCurrentContextUserAsync 예외", ex);
                try { return CreateLocalUser(GetCurrentSamAccountName()); } catch { return null; }
            }
        }

        /// <summary>
        /// 현재 사용자 1명만 반환합니다.
        /// 앱 등록 사용자 전체 목록은 SqliteAppUserRepository를 사용하세요.
        /// </summary>
        public async Task<List<User>> GetUsersAsync()
        {
            var user = await GetCurrentContextUserAsync();
            return user != null ? new List<User> { user } : new List<User>();
        }

        /// <summary>
        /// 관리자 화면에서 특정 AD 계정을 조회합니다 (단일 조회).
        /// 계정이 AD에 없거나 도메인 미가입이면 null을 반환합니다.
        /// </summary>
        public async Task<User> LookupAdUserAsync(string samAccountName)
        {
            if (string.IsNullOrWhiteSpace(samAccountName)) return null;
            if (!IsDomainJoined())
            {
                AppLogger.Info("[AdAuthService] LookupAdUserAsync: 도메인 미가입 → null 반환");
                return null;
            }
            AppLogger.Info($"[AdAuthService] LookupAdUserAsync: {samAccountName}");
            return await Task.Run(() => QuerySingleUser(samAccountName));
        }

        /// <summary>
        /// AD에서 query로 시작하는 계정/이름을 검색합니다 (자동완성용).
        /// 도메인 미가입이면 빈 목록을 반환합니다.
        /// </summary>
        public async Task<List<User>> SearchAdUsersAsync(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<User>();
            if (!IsDomainJoined())
            {
                AppLogger.Info("[AdAuthService] SearchAdUsersAsync: 도메인 미가입 → 빈 목록 반환");
                return new List<User>();
            }

            AppLogger.Info($"[AdAuthService] SearchAdUsersAsync: '{query}'");
            return await Task.Run(() => SearchUsersInAd(query, maxResults));
        }

        private List<User> SearchUsersInAd(string query, int maxResults)
        {
            var result = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var ctx = new PrincipalContext(ContextType.Domain))
                {
                    // 1. sAMAccountName 시작 검색 (인덱스 사용 → 빠름)
                    AddByPrincipalFilter(ctx,
                        new UserPrincipal(ctx) { SamAccountName = query + "*" },
                        result, maxResults);

                    // 2. DisplayName 시작 검색 (자리 여유 있을 때)
                    if (result.Count < maxResults)
                        AddByPrincipalFilter(ctx,
                            new UserPrincipal(ctx) { DisplayName = query + "*" },
                            result, maxResults);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[AdAuthService] SearchUsersInAd 실패: {ex.Message}");
            }
            AppLogger.Info($"[AdAuthService] 검색 결과: {result.Count}명");
            return result.Values.OrderBy(u => u.Name).Take(maxResults).ToList();
        }

        private static void AddByPrincipalFilter(PrincipalContext ctx, UserPrincipal filter,
            Dictionary<string, User> result, int maxResults)
        {
            try
            {
                using (var searcher = new PrincipalSearcher(filter))
                {
                    foreach (var p in searcher.FindAll())
                    {
                        if (result.Count >= maxResults) break;
                        if (!(p is UserPrincipal up)) continue;
                        if (string.IsNullOrWhiteSpace(up.SamAccountName)) continue;
                        if (result.ContainsKey(up.SamAccountName)) continue;
                        result[up.SamAccountName] = new User
                        {
                            UserId    = up.SamAccountName,
                            AdAccount = up.SamAccountName,
                            Name      = up.DisplayName ?? up.SamAccountName,
                            Email     = up.EmailAddress,
                            Role      = UserRole.GeneralUser
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[AdAuthService] 필터 검색 실패: {ex.Message}");
            }
            finally
            {
                filter?.Dispose();
            }
        }

        // ── Private ──────────────────────────────────────────────────────

        /// <summary>단일 사용자 조회 (새 PrincipalContext 생성)</summary>
        private User QuerySingleUser(string samAccountName)
        {
            try
            {
                using (var ctx = new PrincipalContext(ContextType.Domain))
                {
                    var up = UserPrincipal.FindByIdentity(ctx, samAccountName);
                    if (up == null) return null;

                    List<string> groups;
                    try { groups = up.GetGroups().Select(g => g.Name).ToList(); }
                    catch { groups = new List<string>(); }

                    var role = ResolveRole(groups);
                    AppLogger.Info($"[AdAuthService] {samAccountName} 조회 성공 - 소속 그룹: {string.Join(", ", groups)}");

                    return new User
                    {
                        UserId    = up.SamAccountName,
                        AdAccount = up.SamAccountName,
                        Name      = up.DisplayName ?? up.SamAccountName,
                        Email     = up.EmailAddress,
                        Role      = role
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[AdAuthService] {samAccountName} 조회 실패: {ex.Message}");
                return null;
            }
        }

        private static string GetCurrentSamAccountName()
        {
            var name = WindowsIdentity.GetCurrent().Name; // "DOMAIN\username" 또는 "username"
            return name.Contains('\\') ? name.Split('\\')[1] : name;
        }

        private static User CreateLocalUser(string samAccountName)
        {
            return new User
            {
                UserId    = samAccountName,
                AdAccount = samAccountName,
                Name      = samAccountName,
                Email     = null,
                Role      = UserRole.GeneralUser
            };
        }

        private UserRole ResolveRole(List<string> groups)
        {
            if (groups.Any(g => _adminGroups.Contains(g, StringComparer.OrdinalIgnoreCase)))
                return UserRole.Admin;
            if (groups.Any(g => _approverGroups.Contains(g, StringComparer.OrdinalIgnoreCase)))
                return UserRole.Approver;
            return UserRole.GeneralUser;
        }

        private static List<string> Split(string csv) =>
            csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(s => s.Trim())
               .ToList();
    }
}
