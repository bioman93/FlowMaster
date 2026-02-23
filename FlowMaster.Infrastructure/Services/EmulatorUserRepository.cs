using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// IUserRepository 구현체: Emulator에서 사용자 목록을 로드합니다.
    /// Emulator 미실행 시 EmulatorAuthService의 Mock 폴백을 그대로 사용합니다.
    /// 최초 로드 후 메모리에 캐싱하여 반복 API 호출을 방지합니다.
    /// </summary>
    public class EmulatorUserRepository : IUserRepository
    {
        private readonly IAuthService _authService;
        private List<User> _cachedUsers;

        public EmulatorUserRepository(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// 전체 사용자 목록을 반환합니다.
        /// 캐시 없으면 Emulator(또는 Mock)에서 로드합니다.
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            if (_cachedUsers == null)
                _cachedUsers = await _authService.GetUsersAsync();
            return _cachedUsers;
        }

        public async Task<User> GetUserByAdAccountAsync(string adAccount)
        {
            var users = await GetAllUsersAsync();
            return users.FirstOrDefault(u =>
                u.AdAccount.Equals(adAccount, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            var users = await GetAllUsersAsync();
            return users.Where(u => u.Role == role).ToList();
        }

        public Task AddUserAsync(User user)
        {
            // Emulator 환경에서는 JSON 파일 직접 수정이므로 앱에서 추가 불필요
            return Task.CompletedTask;
        }
    }
}
