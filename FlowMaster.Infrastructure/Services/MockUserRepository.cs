using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;

namespace FlowMaster.Infrastructure.Services
{
    public class MockUserRepository : IUserRepository
    {
        private readonly List<User> _users;

        public MockUserRepository()
        {
            _users = new List<User>
            {
                new User { UserId = "U001", AdAccount = "user", Name = "일반사용자", Role = UserRole.GeneralUser, Email = "user@test.com" },
                new User { UserId = "U002", AdAccount = "approver", Name = "김부장", Role = UserRole.Approver, Email = "approver@test.com" },
                new User { UserId = "U003", AdAccount = "admin", Name = "관리자", Role = UserRole.Admin, Email = "admin@test.com" },
                new User { UserId = "U004", AdAccount = "approver2", Name = "이이사", Role = UserRole.Approver, Email = "approver2@test.com" }
            };
        }

        public Task<User> GetUserByAdAccountAsync(string adAccount)
        {
            var user = _users.FirstOrDefault(u => u.AdAccount.Equals(adAccount, System.StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user);
        }

        public Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            var results = _users.Where(u => u.Role == role).ToList();
            return Task.FromResult(results);
        }

        public Task AddUserAsync(User user)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        // Test Helper: Get all test users
        public List<User> GetAllTestUsers()
        {
            return _users;
        }
    }
}
