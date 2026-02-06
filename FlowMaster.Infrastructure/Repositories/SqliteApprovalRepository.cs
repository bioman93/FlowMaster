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
                
                // ApprovalDocuments Table
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS ApprovalDocuments (
                        DocId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT,
                        WriterId TEXT,
                        WriterName TEXT,
                        CreateDate TEXT,
                        UpdateDate TEXT,
                        Status INTEGER,
                        CurrentApproverId TEXT
                    )");

                // ApprovalLines Table
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS ApprovalLines (
                        LineId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DocId INTEGER,
                        ApproverId TEXT,
                        ApproverName TEXT,
                        Sequence INTEGER,
                        Status INTEGER,
                        ActionDate TEXT,
                        Comment TEXT
                    )");

                // TestResults Table
                conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS TestResults (
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
            }
        }

        private IDbConnection GetConnection() => new SqliteConnection(_connectionString);

        public async Task<int> CreateDocumentAsync(ApprovalDocument doc)
        {
            using (var conn = GetConnection())
            {
                var sql = @"
                    INSERT INTO ApprovalDocuments (Title, WriterId, WriterName, CreateDate, Status, CurrentApproverId)
                    VALUES (@Title, @WriterId, @WriterName, @CreateDate, @Status, @CurrentApproverId);
                    SELECT last_insert_rowid();";
                return await conn.ExecuteScalarAsync<int>(sql, doc);
            }
        }

        public async Task UpdateDocumentStatusAsync(int docId, ApprovalStatus status)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync("UPDATE ApprovalDocuments SET Status = @Status, UpdateDate = @UpdateDate WHERE DocId = @DocId", 
                    new { Status = status, UpdateDate = DateTime.Now, DocId = docId });
            }
        }

        public async Task<ApprovalDocument> GetDocumentAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                var doc = await conn.QueryFirstOrDefaultAsync<ApprovalDocument>("SELECT * FROM ApprovalDocuments WHERE DocId = @DocId", new { DocId = docId });
                if (doc != null)
                {
                    // Fetch related data
                    doc.ApprovalLines = (await conn.QueryAsync<ApprovalLine>("SELECT * FROM ApprovalLines WHERE DocId = @DocId", new { DocId = docId })).ToList();
                    doc.TestResults = (await conn.QueryAsync<TestResult>("SELECT * FROM TestResults WHERE DocId = @DocId", new { DocId = docId })).ToList();
                }
                return doc;
            }
        }

        public async Task<List<ApprovalDocument>> GetMyDraftsAsync(string userId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ApprovalDocument>(
                    "SELECT * FROM ApprovalDocuments WHERE WriterId = @UserId ORDER BY CreateDate DESC", 
                    new { UserId = userId })).ToList();
            }
        }

        public async Task<List<ApprovalDocument>> GetPendingApprovalsAsync(string approverId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<ApprovalDocument>(
                    "SELECT * FROM ApprovalDocuments WHERE CurrentApproverId = @ApproverId AND Status = @Status ORDER BY CreateDate DESC", 
                    new { ApproverId = approverId, Status = ApprovalStatus.Pending })).ToList();
            }
        }

        public async Task AddApprovalLineAsync(ApprovalLine line)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO ApprovalLines (DocId, ApproverId, ApproverName, Sequence, Status, ActionDate, Comment)
                    VALUES (@DocId, @ApproverId, @ApproverName, @Sequence, @Status, @ActionDate, @Comment)", line);
            }
        }

        public async Task UpdateApprovalLineStatusAsync(int lineId, ApprovalStepStatus status, string comment)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync("UPDATE ApprovalLines SET Status = @Status, Comment = @Comment, ActionDate = @ActionDate WHERE LineId = @LineId",
                    new { Status = status, Comment = comment, ActionDate = DateTime.Now, LineId = lineId });
            }
        }

        public async Task AddTestResultAsync(TestResult result)
        {
            using (var conn = GetConnection())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO TestResults (DocId, ProjectName, Version, TestDate, TestCaseName, IsPass, FailureReason, Details, BackupDbSource)
                    VALUES (@DocId, @ProjectName, @Version, @TestDate, @TestCaseName, @IsPass, @FailureReason, @Details, @BackupDbSource)", result);
            }
        }

        public async Task<List<TestResult>> GetTestResultsAsync(int docId)
        {
            using (var conn = GetConnection())
            {
                return (await conn.QueryAsync<TestResult>("SELECT * FROM TestResults WHERE DocId = @DocId", new { DocId = docId })).ToList();
            }
        }
    }
}
