using System.Collections.Generic;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Infrastructure.Services
{
    /// <summary>
    /// 최소 폴백 인증 서비스 (AdAuthService 미주입 시 사용).
    /// MockUserRepository의 테스트 계정 4명을 반환합니다.
    /// </summary>
    internal class MockAuthService : IAuthService
    {
        private readonly MockUserRepository _repo = new MockUserRepository();

        public bool IsEmulatorAvailable => false;
        public string CurrentToken => null;

        public Task<string> LoginAsync(string adAccount) => Task.FromResult<string>(null);

        public Task<List<User>> GetUsersAsync() => _repo.GetAllUsersAsync();

        public Task<User> GetCurrentContextUserAsync() =>
            Task.FromResult(_repo.GetAllTestUsers().Count > 0 ? _repo.GetAllTestUsers()[0] : null);
    }
}
