using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FlowMaster.Domain.Interfaces;
using FlowMaster.Domain.Models;
using Microsoft.Data.Sqlite;

namespace FlowMaster.Infrastructure.Repositories
{
    /// <summary>
    /// SQLite 기반 결재 문서 저장소.
    /// 테이블 접두사 FM_을 사용하여 emulator.db 공유 시 다른 프로젝트 테이블과 충돌을 방지합니다.
    /// 기본 DB: flowmaster_test.db (로컬), 공유 시: emulator.db 경로 지정
    /// </summary>
    public class SqliteApprovalRepository : IApprovalRepository
    {
        private readonly string _connectionString;

        public SqliteApprovalRepository(string dbPath = "flowmaster_test.db")
        {
            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var conn = GetConnection())
            {
                conn.Open();

                // FM_ApprovalDocuments: 결재 문서 (기안)
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_ApprovalDocuments (
                        DocId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT,
                        WriterId TEXT,
                        WriterName TEXT,
                        CreateDate TEXT,
                        UpdateDate TEXT,
                        Status INTEGER,
                        CurrentApproverId TEXT,
                        TableType TEXT,
                        GenType TEXT,
                        InjType TEXT,
                        Description TEXT,
                        ApproverComment TEXT,
                        ApprovalId TEXT,
                        ApprovalTime TEXT,
                        Version TEXT,
                        OutputPath TEXT,
                        SyncStatus INTEGER DEFAULT 0,
                        SyncRetryCount INTEGER DEFAULT 0,
                        SyncError TEXT
                    )");

                // 기존 DB 마이그레이션: SyncStatus 관련 컬럼이 없으면 추가
                MigrateAddSyncColumns(conn);

                // FM_ApprovalLines: 결재선
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_ApprovalLines (
                        LineId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocId INTEGER,
                        ApproverId TEXT,
                        ApproverName TEXT,
                        Sequence INTEGER,
                        Status INTEGER,
                        ActionDate TEXT,
                        Comment TEXT
                    )");

                // FM_TestResults: 테스트 결과
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_TestResults (
                        ResultId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocId INTEGER,
                        ProjectName TEXT,
                        Version TEXT,
                        TestDate TEXT,
                        TestCaseName TEXT,
                        IsPass INTEGER,
                        FailureReason TEXT,
                        Details TEXT,
                        BackupDbSource TEXT
                    )");

                // FM_ChecklistItems: 체크리스트
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_ChecklistItems (
                        ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocId INTEGER,
                        RowNo TEXT,
                        CheckItem TEXT,
                        OutputContent TEXT,
                        EvaluationCode TEXT,
                        Remarks TEXT,
                        DisplayOrder INTEGER
                    )");

                // FM_DocumentParticipants: 문서별 참여자
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_DocumentParticipants (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocId INTEGER NOT NULL,
                        UserId TEXT NOT NULL,
                        UserName TEXT
                    )");

                // FM_ParticipantGroups: MPI/GDI 참여자 그룹
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS FM_ParticipantGroups (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        GroupName TEXT NOT NULL,
                        UserId TEXT NOT NULL,
                        UserName TEXT
                    )");
            }
        }

        private void MigrateAddSyncColumns(IDbConnection conn)
        {
            // PRAGMA table_info으로 컬럼 존재 여부 확인 후 없으면 ALTER TABLE로 추가
            var columns = new HashSet<string>(
                conn.Query<PragmaTableInfo>("PRAGMA table_info(FM_ApprovalDocuments)")
                    .Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);

            if (!columns.Contains("SyncStatus"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN SyncStatus INTEGER DEFAULT 0");

            if (!columns.Contains("SyncRetryCount"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN SyncRetryCount INTEGER DEFAULT 0");

            if (!columns.Contains("SyncError"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN SyncError TEXT");

            if (!columns.Contains("ApprovalTime"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN ApprovalTime TEXT");

            if (!columns.Contains("Version"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN Version TEXT");

            if (!columns.Contains("OutputPath"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN OutputPath TEXT");

            if (!columns.Contains("CurrentApproverName"))
                conn.Execute("ALTER TABLE FM_ApprovalDocuments ADD COLUMN CurrentApproverName TEXT");
        }

        private IDbConnection GetConnection() => new SqliteConnection(_connectionString);

        public async Task<int> CreateDocumentAsync(ApprovalDocument doc)
        {
            using (var conn = GetConnection())
            {
                var sql = @"
                    INSERT INTO FM_ApprovalDocuments
                        (Title, WriterId, WriterName, CreateDate, Status, CurrentApproverId, CurrentApproverName, ApprovalId, TableType, GenType, InjType, Description, Version, OutputPath)
                    VALUES
                        (@Title, @WriterId, @WriterName, @CreateDate, @Status, @CurrentApproverId, @CurrentApproverName, @ApprovalId, @TableType, @GenType, @InjType, @Description, @Version, @OutputPath);
                    SELECT last_insert_rowid();";
                return await conn.ExecuteScalarAsync<int>(sql, doc);
            }
        }

        public async Task UpdateDocumentAsync(ApprovalDocument doc)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    UPDATE FM_ApprovalDocuments
                    SET Title = @Title, UpdateDate = @UpdateDate, Status = @Status,
                        TableType = @TableType, GenType = @GenType, InjType = @InjType,
                        Description = @Description, ApproverComment = @ApproverComment,
                        CurrentApproverId = @CurrentApproverId, CurrentApproverName = @CurrentApproverName,
                        ApprovalId = @ApprovalId, ApprovalTime = @ApprovalTime,
                        Version = @Version, OutputPath = @OutputPath
                    WHERE DocId = @DocId", doc);
            }
        }

        public async Task DeleteDocumentAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync("DELETE FROM FM_ChecklistItems WHERE DocId = @DocId", new { DocId = docId });
                await conn.ExecuteAsync("DELETE FROM FM_ApprovalLines WHERE DocId = @DocId", new { DocId = docId });
                await conn.ExecuteAsync("DELETE FROM FM_TestResults WHERE DocId = @DocId", new { DocId = docId });
                await conn.ExecuteAsync("DELETE FROM FM_ApprovalDocuments WHERE DocId = @DocId", new { DocId = docId });
            }
        }

        public async Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "UPDATE FM_ApprovalDocuments SET Status = @Status, UpdateDate = @UpdateDate WHERE DocId = @DocId",
                    new { Status = status, UpdateDate = DateTime.Now, DocId = docId });
            }
        }

        public async Task<ApprovalDocument> GetDocumentAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                var doc = await conn.QueryFirstOrDefaultAsync<ApprovalDocument>(
                    "SELECT * FROM FM_ApprovalDocuments WHERE DocId = @DocId", new { DocId = docId });

                if (doc != null)
                {
                    doc.ApprovalLines = (await conn.QueryAsync<ApprovalLine>(
                        "SELECT * FROM FM_ApprovalLines WHERE DocId = @DocId", new { DocId = docId })).ToList();
                    doc.TestResults = (await conn.QueryAsync<TestResult>(
                        "SELECT * FROM FM_TestResults WHERE DocId = @DocId", new { DocId = docId })).ToList();
                    doc.ChecklistItems = (await conn.QueryAsync<ChecklistItem>(
                        "SELECT * FROM FM_ChecklistItems WHERE DocId = @DocId ORDER BY DisplayOrder", new { DocId = docId })).ToList();
                }
                return doc;
            }
        }

        public async Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ApprovalDocument>(
                    "SELECT * FROM FM_ApprovalDocuments WHERE WriterId = @UserId ORDER BY CreateDate DESC",
                    new { UserId = userId })).ToList();
            }
        }

        public async Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ApprovalDocument>(
                    "SELECT * FROM FM_ApprovalDocuments WHERE CurrentApproverId = @ApproverId AND Status = @Status ORDER BY CreateDate DESC",
                    new { ApproverId = approverId, Status = ApprovalStatus.Pending })).ToList();
            }
        }

        public async Task AddApprovalLineAsync(ApprovalLine line)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO FM_ApprovalLines (DocId, ApproverId, ApproverName, Sequence, Status, ActionDate, Comment)
                    VALUES (@DocId, @ApproverId, @ApproverName, @Sequence, @Status, @ActionDate, @Comment)", line);
            }
        }

        public async Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "UPDATE FM_ApprovalLines SET Status = @Status, Comment = @Comment, ActionDate = @ActionDate WHERE LineId = @LineId",
                    new { Status = status, Comment = comment, ActionDate = DateTime.Now, LineId = lineId });
            }
        }

        public async Task AddTestResultAsync(TestResult result)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO FM_TestResults
                        (DocId, ProjectName, Version, TestDate, TestCaseName, IsPass, FailureReason, Details, BackupDbSource)
                    VALUES
                        (@DocId, @ProjectName, @Version, @TestDate, @TestCaseName, @IsPass, @FailureReason, @Details, @BackupDbSource)", result);
            }
        }

        public async Task<List<TestResult>> GetTestResultsAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<TestResult>(
                    "SELECT * FROM FM_TestResults WHERE DocId = @DocId", new { DocId = docId })).ToList();
            }
        }

        public async Task SaveChecklistItemsAsync(int docId, List<ChecklistItem> items)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM FM_ChecklistItems WHERE DocId = @DocId", new { DocId = docId }, tx);

                    foreach (var item in items)
                    {
                        item.DocId = docId;
                        await conn.ExecuteAsync(@"
                            INSERT INTO FM_ChecklistItems
                                (DocId, RowNo, CheckItem, OutputContent, EvaluationCode, Remarks, DisplayOrder)
                            VALUES
                                (@DocId, @RowNo, @CheckItem, @OutputContent, @EvaluationCode, @Remarks, @DisplayOrder)",
                            item, tx);
                    }
                    tx.Commit();
                }
            }
        }

        public async Task<List<ChecklistItem>> GetChecklistItemsAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ChecklistItem>(
                    "SELECT * FROM FM_ChecklistItems WHERE DocId = @DocId ORDER BY DisplayOrder",
                    new { DocId = docId })).ToList();
            }
        }

        public async Task UpdateApprovalIdAsync(int docId, string approvalId)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "UPDATE FM_ApprovalDocuments SET ApprovalId = @ApprovalId, UpdateDate = @UpdateDate WHERE DocId = @DocId",
                    new { ApprovalId = approvalId, UpdateDate = DateTime.Now, DocId = docId });
            }
        }

        public async Task<ApprovalDocument> GetDocumentByApprovalIdAsync(string approvalId)
        {
            using (var conn = GetConnection())
            {
                return await conn.QueryFirstOrDefaultAsync<ApprovalDocument>(
                    "SELECT * FROM FM_ApprovalDocuments WHERE ApprovalId = @ApprovalId",
                    new { ApprovalId = approvalId });
            }
        }

        public async Task<List<ApprovalDocument>> GetAllDocumentsAsync()
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ApprovalDocument>(
                    "SELECT * FROM FM_ApprovalDocuments ORDER BY CreateDate DESC")).ToList();
            }
        }

        public async Task<List<ApprovalDocument>> GetUnsyncedDocumentsAsync()
        {
            using (var conn = GetConnection())
            {
                // SyncStatus = Pending(1) 또는 Failed(2), 재시도 횟수가 3 미만인 문서
                return (await conn.QueryAsync<ApprovalDocument>(@"
                    SELECT * FROM FM_ApprovalDocuments
                    WHERE SyncStatus IN (1, 2) AND SyncRetryCount < 3
                    ORDER BY CreateDate ASC")).ToList();
            }
        }

        public async Task UpdateSyncStatusAsync(int docId, SyncStatus status, int retryCount, string error)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    UPDATE FM_ApprovalDocuments
                    SET SyncStatus = @SyncStatus, SyncRetryCount = @SyncRetryCount,
                        SyncError = @SyncError, UpdateDate = @UpdateDate
                    WHERE DocId = @DocId",
                    new { SyncStatus = status, SyncRetryCount = retryCount, SyncError = error,
                          UpdateDate = DateTime.Now, DocId = docId });
            }
        }

        #region Participant Methods

        public async Task<List<User>> GetDocumentParticipantsAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<User>(
                    "SELECT UserId, UserName as Name FROM FM_DocumentParticipants WHERE DocId = @DocId",
                    new { DocId = docId })).ToList();
            }
        }

        public async Task SaveDocumentParticipantsAsync(int docId, List<User> participants)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM FM_DocumentParticipants WHERE DocId = @DocId", new { DocId = docId }, tx);
                    foreach (var u in participants)
                    {
                        await conn.ExecuteAsync(
                            "INSERT INTO FM_DocumentParticipants (DocId, UserId, UserName) VALUES (@DocId, @UserId, @Name)",
                            new { DocId = docId, u.UserId, u.Name }, tx);
                    }
                    tx.Commit();
                }
            }
        }

        public async Task AddDocumentParticipantAsync(int docId, User user)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "INSERT OR IGNORE INTO FM_DocumentParticipants (DocId, UserId, UserName) VALUES (@DocId, @UserId, @Name)",
                    new { DocId = docId, user.UserId, user.Name });
            }
        }

        public async Task RemoveDocumentParticipantAsync(int docId, string userId)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "DELETE FROM FM_DocumentParticipants WHERE DocId = @DocId AND UserId = @UserId",
                    new { DocId = docId, UserId = userId });
            }
        }

        public async Task<List<User>> GetParticipantGroupAsync(string groupName)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<User>(
                    "SELECT UserId, UserName as Name FROM FM_ParticipantGroups WHERE GroupName = @GroupName",
                    new { GroupName = groupName })).ToList();
            }
        }

        public async Task AddParticipantGroupMemberAsync(string groupName, User user)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "INSERT OR IGNORE INTO FM_ParticipantGroups (GroupName, UserId, UserName) VALUES (@GroupName, @UserId, @Name)",
                    new { GroupName = groupName, user.UserId, user.Name });
            }
        }

        public async Task RemoveParticipantGroupMemberAsync(string groupName, string userId)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(
                    "DELETE FROM FM_ParticipantGroups WHERE GroupName = @GroupName AND UserId = @UserId",
                    new { GroupName = groupName, UserId = userId });
            }
        }

        #endregion

        /// <summary>PRAGMA table_info 결과 매핑용 내부 클래스 (.NET Framework dynamic 대체)</summary>
        private class PragmaTableInfo
        {
            public int Cid { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public int NotNull { get; set; }
            public string DfltValue { get; set; }
            public int Pk { get; set; }
        }
    }
}
