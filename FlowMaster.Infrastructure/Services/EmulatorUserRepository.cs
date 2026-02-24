using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// IUserRepository 구현체.
    /// Emulator 실행 중: Emulator API에서 사용자 목록 로드.
    /// Emulator 미실행 : localFallback(SqliteAppUserRepository)에서 앱 등록 사용자 로드.
    /// 최초 로드 후 메모리 캐싱하여 반복 API 호출 방지.
    /// </summary>
    public class EmulatorUserRepository : IUserRepository
    {
        private readonly IAuthService _authService;
        private readonly IUserRepository _localFallback;
        private List<User> _cachedUsers;

        public EmulatorUserRepository(IAuthService authService, IUserRepository localFallback = null)
        {
            _authService   = authService;
            _localFallback = localFallback;
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            if (_cachedUsers != null) return _cachedUsers;

            // Emulator 연결 시도 (IsEmulatorAvailable은 GetUsersAsync 호출 후 설정됨)
            var emulatorUsers = await _authService.GetUsersAsync();

            if (_authService.IsEmulatorAvailable)
            {
                // Emulator 정상 응답 → Emulator 사용자 목록 사용
                _cachedUsers = emulatorUsers;
            }
            else if (_localFallback != null)
            {
                // Emulator 미실행 → 앱 등록 사용자(SQLite) 사용
                _cachedUsers = await _localFallback.GetAllUsersAsync();
            }
            else
            {
                _cachedUsers = emulatorUsers; // 폴백 없으면 빈 목록
            }

            return _cachedUsers;
        }

        public async Task<User> GetUserByAdAccountAsync(string adAccount)
        {
            if (_localFallback != null && !_authService.IsEmulatorAvailable)
                return await _localFallback.GetUserByAdAccountAsync(adAccount);

            var users = await GetAllUsersAsync();
            return users.FirstOrDefault(u =>
                u.AdAccount.Equals(adAccount, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            if (_localFallback != null && !_authService.IsEmulatorAvailable)
                return await _localFallback.GetUsersByRoleAsync(role);

            var users = await GetAllUsersAsync();
            return users.Where(u => u.Role == role).ToList();
        }

        public async Task AddUserAsync(User user)
        {
            _cachedUsers = null; // 캐시 무효화
            if (_localFallback != null)
                await _localFallback.AddUserAsync(user);
        }

        public async Task UpdateUserAsync(User user)
        {
            _cachedUsers = null;
            if (_localFallback != null)
                await _localFallback.UpdateUserAsync(user);
        }

        public async Task DeleteUserAsync(string userId)
        {
            _cachedUsers = null;
            if (_localFallback != null)
                await _localFallback.DeleteUserAsync(userId);
        }
    }
}
