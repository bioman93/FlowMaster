using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using Microsoft.Data.Sqlite;

namespace FlowMaster.Infrastructure.Repositories
{
    /// <summary>
    /// 앱 등록 사용자/그룹 저장소 (SQLite).
    /// IUserRepository + IAppGroupRepository 구현체.
    ///
    /// 테이블:
    ///   FM_AppUsers        - 관리자가 등록한 앱 사용자 (AD 계정 기반)
    ///   FM_AppGroups       - 앱 자체 그룹 (AD 그룹과 무관)
    ///   FM_AppGroupMembers - 그룹 멤버십
    /// </summary>
    public class SqliteAppUserRepository : IUserRepository, IAppGroupRepository
    {
        private readonly string _connectionString;

        // Dapper 매핑용 내부 DTO (dynamic 대신 강타입 사용 - netstandard2.0 호환)
        private class UserRow
        {
            public string UserId      { get; set; }
            public string DisplayName { get; set; }
            public string Email       { get; set; }
            public long   Role        { get; set; }
            public long   IsEnabled   { get; set; }
        }

        private class GroupRow
        {
            public long   GroupId     { get; set; }
            public string GroupName   { get; set; }
            public string Description { get; set; }
            public long   IsDefault   { get; set; }
            public string CreatedAt   { get; set; }
        }

        public SqliteAppUserRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
            InitializeTables();
        }

        private void InitializeTables()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();

                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_AppUsers (
                        UserId      TEXT PRIMARY KEY,
                        DisplayName TEXT NOT NULL,
                        Email       TEXT,
                        Role        INTEGER NOT NULL DEFAULT 0,
                        IsEnabled   INTEGER NOT NULL DEFAULT 1,
                        CreatedAt   TEXT NOT NULL,
                        UpdatedAt   TEXT
                    )");

                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_AppGroups (
                        GroupId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        GroupName   TEXT NOT NULL UNIQUE,
                        Description TEXT,
                        IsDefault   INTEGER NOT NULL DEFAULT 0,
                        CreatedAt   TEXT NOT NULL
                    )");

                // 기존 DB 마이그레이션: Role → IsDefault 컬럼 교체
                try { conn.Execute("ALTER TABLE FM_AppGroups ADD COLUMN IsDefault INTEGER NOT NULL DEFAULT 0"); }
                catch { /* 이미 존재 */ }

                // 기본 그룹 자동 시딩
                var defaultGroups = new[]
                {
                    ("GeneralUser", "일반 사용자"),
                    ("Approver",    "결재자"),
                    ("Admin",       "시스템 관리자")
                };
                foreach (var (name, desc) in defaultGroups)
                {
                    conn.Execute(
                        @"INSERT OR IGNORE INTO FM_AppGroups (GroupName, Description, IsDefault, CreatedAt)
                          VALUES (@Name, @Desc, 1, datetime('now'))",
                        new { Name = name, Desc = desc });
                    conn.Execute(
                        "UPDATE FM_AppGroups SET IsDefault = 1 WHERE GroupName = @Name",
                        new { Name = name });
                }

                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_AppGroupMembers (
                        GroupId  INTEGER NOT NULL,
                        UserId   TEXT NOT NULL,
                        AddedAt  TEXT NOT NULL,
                        PRIMARY KEY (GroupId, UserId)
                    )");
            }
        }

        // ── IUserRepository ────────────────────────────────────────────

        public async Task<List<User>> GetAllUsersAsync()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var rows = await conn.QueryAsync<UserRow>(
                    "SELECT UserId, DisplayName, Email, Role FROM FM_AppUsers WHERE IsEnabled = 1 ORDER BY DisplayName");
                return rows.Select(MapUser).ToList();
            }
        }

        /// <summary>활성/비활성 모두 포함하여 조회합니다 (관리자 화면용).</summary>
        public async Task<List<User>> GetAllUsersIncludeDisabledAsync()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var rows = await conn.QueryAsync<UserRow>(
                    "SELECT UserId, DisplayName, Email, Role, IsEnabled FROM FM_AppUsers ORDER BY DisplayName");
                return rows.Select(MapUser).ToList();
            }
        }

        public async Task<User> GetUserByAdAccountAsync(string adAccount)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var row = await conn.QueryFirstOrDefaultAsync<UserRow>(
                    "SELECT UserId, DisplayName, Email, Role FROM FM_AppUsers WHERE UserId = @Id",
                    new { Id = adAccount });
                return row == null ? null : MapUser(row);
            }
        }

        public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var rows = await conn.QueryAsync<UserRow>(
                    "SELECT UserId, DisplayName, Email, Role FROM FM_AppUsers WHERE Role = @Role AND IsEnabled = 1",
                    new { Role = (int)role });
                return rows.Select(MapUser).ToList();
            }
        }

        public async Task AddUserAsync(User user)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    @"INSERT OR REPLACE INTO FM_AppUsers
                        (UserId, DisplayName, Email, Role, IsEnabled, CreatedAt)
                      VALUES (@UserId, @DisplayName, @Email, @Role, 1, @CreatedAt)",
                    new
                    {
                        UserId      = user.AdAccount,
                        DisplayName = user.Name,
                        Email       = user.Email,
                        Role        = (int)user.Role,
                        CreatedAt   = DateTime.UtcNow.ToString("o")
                    });
            }
        }

        public async Task UpdateUserAsync(User user)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    @"UPDATE FM_AppUsers
                      SET DisplayName = @DisplayName,
                          Email       = @Email,
                          Role        = @Role,
                          UpdatedAt   = @UpdatedAt
                      WHERE UserId = @UserId",
                    new
                    {
                        UserId      = user.AdAccount,
                        DisplayName = user.Name,
                        Email       = user.Email,
                        Role        = (int)user.Role,
                        UpdatedAt   = DateTime.UtcNow.ToString("o")
                    });
            }
        }

        public async Task DeleteUserAsync(string userId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    "DELETE FROM FM_AppGroupMembers WHERE UserId = @UserId",
                    new { UserId = userId });
                await conn.ExecuteAsync(
                    "DELETE FROM FM_AppUsers WHERE UserId = @UserId",
                    new { UserId = userId });
            }
        }

        // ── IAppGroupRepository ────────────────────────────────────────

        public async Task<List<AppGroup>> GetAllGroupsAsync()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var rows = await conn.QueryAsync<GroupRow>(
                    "SELECT GroupId, GroupName, Description, IsDefault, CreatedAt FROM FM_AppGroups ORDER BY IsDefault DESC, GroupName");
                return rows.Select(MapGroup).ToList();
            }
        }

        public async Task<AppGroup> GetGroupWithMembersAsync(int groupId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var groupRow = await conn.QueryFirstOrDefaultAsync<GroupRow>(
                    "SELECT GroupId, GroupName, Description, IsDefault, CreatedAt FROM FM_AppGroups WHERE GroupId = @GroupId",
                    new { GroupId = groupId });
                if (groupRow == null) return null;

                var group = MapGroup(groupRow);
                var memberRows = await conn.QueryAsync<UserRow>(
                    @"SELECT u.UserId, u.DisplayName, u.Email, u.Role
                      FROM FM_AppGroupMembers m
                      JOIN FM_AppUsers u ON m.UserId = u.UserId
                      WHERE m.GroupId = @GroupId
                      ORDER BY u.DisplayName",
                    new { GroupId = groupId });
                group.Members = memberRows.Select(MapUser).ToList();
                return group;
            }
        }

        public async Task<int> AddGroupAsync(AppGroup group)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                return await conn.QuerySingleAsync<int>(
                    @"INSERT INTO FM_AppGroups (GroupName, Description, IsDefault, CreatedAt)
                      VALUES (@GroupName, @Description, @IsDefault, @CreatedAt);
                      SELECT last_insert_rowid();",
                    new
                    {
                        GroupName   = group.GroupName,
                        Description = group.Description,
                        IsDefault   = group.IsDefault ? 1 : 0,
                        CreatedAt   = DateTime.UtcNow.ToString("o")
                    });
            }
        }

        public async Task UpdateGroupAsync(AppGroup group)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    @"UPDATE FM_AppGroups
                      SET GroupName = @GroupName, Description = @Description
                      WHERE GroupId = @GroupId AND IsDefault = 0",
                    new
                    {
                        GroupId     = group.GroupId,
                        GroupName   = group.GroupName,
                        Description = group.Description
                    });
            }
        }

        public async Task DeleteGroupAsync(int groupId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                var isDefault = await conn.ExecuteScalarAsync<long>(
                    "SELECT IsDefault FROM FM_AppGroups WHERE GroupId = @GroupId",
                    new { GroupId = groupId });
                if (isDefault == 1)
                    throw new InvalidOperationException("기본 그룹은 삭제할 수 없습니다.");

                await conn.ExecuteAsync(
                    "DELETE FROM FM_AppGroupMembers WHERE GroupId = @GroupId",
                    new { GroupId = groupId });
                await conn.ExecuteAsync(
                    "DELETE FROM FM_AppGroups WHERE GroupId = @GroupId",
                    new { GroupId = groupId });
            }
        }

        public async Task AddGroupMemberAsync(int groupId, string userId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    @"INSERT OR IGNORE INTO FM_AppGroupMembers (GroupId, UserId, AddedAt)
                      VALUES (@GroupId, @UserId, @AddedAt)",
                    new { GroupId = groupId, UserId = userId, AddedAt = DateTime.UtcNow.ToString("o") });
            }
        }

        public async Task RemoveGroupMemberAsync(int groupId, string userId)
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                await conn.ExecuteAsync(
                    "DELETE FROM FM_AppGroupMembers WHERE GroupId = @GroupId AND UserId = @UserId",
                    new { GroupId = groupId, UserId = userId });
            }
        }

        // ── 매핑 헬퍼 ─────────────────────────────────────────────────

        private static User MapUser(UserRow r) => new User
        {
            UserId    = r.UserId,
            AdAccount = r.UserId,
            Name      = r.DisplayName,
            Email     = r.Email,
            Role      = (UserRole)(int)r.Role
        };

        private static AppGroup MapGroup(GroupRow r) => new AppGroup
        {
            GroupId     = (int)r.GroupId,
            GroupName   = r.GroupName,
            Description = r.Description,
            IsDefault   = r.IsDefault == 1,
            CreatedAt   = DateTime.TryParse(r.CreatedAt, out var dt) ? dt : DateTime.MinValue
        };
    }
}
