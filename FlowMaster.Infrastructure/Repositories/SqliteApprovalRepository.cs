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
                        ApprovalId TEXT
                    )");

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
            }
        }

        private IDbConnection GetConnection() => new SqliteConnection(_connectionString);

        public async Task<int> CreateDocumentAsync(ApprovalDocument doc)
        {
            using (var conn = GetConnection())
            {
                var sql = @"
                    INSERT INTO FM_ApprovalDocuments
                        (Title, WriterId, WriterName, CreateDate, Status, CurrentApproverId, ApprovalId, TableType, GenType, InjType, Description)
                    VALUES
                        (@Title, @WriterId, @WriterName, @CreateDate, @Status, @CurrentApproverId, @ApprovalId, @TableType, @GenType, @InjType, @Description);
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
                        CurrentApproverId = @CurrentApproverId, ApprovalId = @ApprovalId
                    WHERE DocId = @DocId", doc);
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
    }
}
