using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using FlowMaster.Infrastructure.Services;

namespace FlowMaster.Infrastructure.Repositories
{
    /// <summary>
    /// 앱 등록 사용자/그룹 API 리포지토리.
    /// IUserRepository + IAppGroupRepository 구현체.
    /// ApprovalSystem /api/fm/app-users, /api/fm/app-groups 엔드포인트를 통해
    /// FM_AppUsers, FM_AppGroups, FM_AppGroupMembers 테이블에 접근합니다.
    /// 모든 PC에서 동일한 사용자 등록 정보를 공유합니다.
    /// </summary>
    public class ApiAppUserRepository : IUserRepository, IAppGroupRepository
    {
        private readonly ApprovalApiClient _client;

        public ApiAppUserRepository(ApprovalApiClient client)
        {
            _client = client;
        }

        // ── IUserRepository ────────────────────────────────────────────

        public async Task<List<User>> GetAllUsersAsync()
        {
            var dtos = await _client.FmGetAllAppUsersAsync(includeDisabled: false);
            return dtos.Select(MapUser).ToList();
        }

        public async Task<List<User>> GetAllUsersIncludeDisabledAsync()
        {
            var dtos = await _client.FmGetAllAppUsersAsync(includeDisabled: true);
            return dtos.Select(MapUser).ToList();
        }

        public async Task<User> GetUserByAdAccountAsync(string adAccount)
        {
            var users = await GetAllUsersAsync();
            return users.FirstOrDefault(u =>
                string.Equals(u.AdAccount, adAccount, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            // 그룹명으로 필터링 (Role은 그룹 멤버십에서 파생)
            var groupName = role.ToString(); // "GeneralUser", "Approver", "Admin"
            var dtos = await _client.FmGetAllAppUsersAsync(includeDisabled: false);
            return dtos
                .Where(d => d.Groups != null && d.Groups.Contains(groupName))
                .Select(MapUser)
                .ToList();
        }

        public async Task AddUserAsync(User user)
        {
            await _client.FmUpsertAppUserAsync(ToUserDto(user));
        }

        public async Task UpdateUserAsync(User user)
        {
            await _client.FmUpdateAppUserAsync(user.AdAccount, ToUserDto(user));
        }

        public async Task DeleteUserAsync(string userId)
        {
            await _client.FmDeleteAppUserAsync(userId);
        }

        // ── IAppGroupRepository ────────────────────────────────────────

        public async Task<List<AppGroup>> GetAllGroupsAsync()
        {
            var dtos = await _client.FmGetAllAppGroupsAsync();
            return dtos.Select(MapGroup).ToList();
        }

        public async Task<AppGroup> GetGroupWithMembersAsync(int groupId)
        {
            var dto = await _client.FmGetAppGroupWithMembersAsync(groupId);
            return dto == null ? null : MapGroupWithMembers(dto);
        }

        public async Task<int> AddGroupAsync(AppGroup group)
        {
            return await _client.FmAddAppGroupAsync(ToGroupDto(group));
        }

        public async Task UpdateGroupAsync(AppGroup group)
        {
            await _client.FmUpdateAppGroupAsync(ToGroupDto(group));
        }

        public async Task DeleteGroupAsync(int groupId)
        {
            await _client.FmDeleteAppGroupAsync(groupId);
        }

        public async Task AddGroupMemberAsync(int groupId, string userId)
        {
            await _client.FmAddAppGroupMemberAsync(groupId, userId);
        }

        public async Task RemoveGroupMemberAsync(int groupId, string userId)
        {
            await _client.FmRemoveAppGroupMemberAsync(groupId, userId);
        }

        // ── 매핑 헬퍼 ─────────────────────────────────────────────────

        private static User MapUser(FmAppUserDto d) => new User
        {
            UserId    = d.UserId,
            AdAccount = d.UserId,
            Name      = d.DisplayName,
            Email     = d.Email,
            Groups    = d.Groups ?? new List<string>()
        };

        private static FmAppUserDto ToUserDto(User u) => new FmAppUserDto
        {
            UserId      = u.AdAccount,
            DisplayName = u.Name,
            Email       = u.Email,
            IsEnabled   = 1
        };

        private static AppGroup MapGroup(FmAppGroupDto d) => new AppGroup
        {
            GroupId     = d.GroupId,
            GroupName   = d.GroupName,
            Description = d.Description,
            IsDefault   = d.IsDefault,
            CreatedAt   = DateTime.TryParse(d.CreatedAt, out var dt) ? dt : DateTime.MinValue
        };

        private static AppGroup MapGroupWithMembers(FmAppGroupDto d)
        {
            var g = MapGroup(d);
            g.Members = (d.Members ?? new List<FmAppGroupMemberDto>())
                .Select(m => new User
                {
                    UserId    = m.UserId,
                    AdAccount = m.UserId,
                    Name      = m.DisplayName,
                    Email     = m.Email
                }).ToList();
            return g;
        }

        private static FmAppGroupDto ToGroupDto(AppGroup g) => new FmAppGroupDto
        {
            GroupId     = g.GroupId,
            GroupName   = g.GroupName,
            Description = g.Description
        };
    }
}
