using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using FlowMaster.Domain.Models;
using Microsoft.Data.Sqlite;

namespace FlowMaster.Infrastructure.Repositories
{
    /// <summary>
    /// 외부 DB 연동 Repository (읽기/쓰기 모두 지원)
    /// </summary>
    public class ExternalDbRepository : IDisposable
    {
        private SqliteConnection _connection;
        private string _dbPath;

        public bool IsConnected => _connection != null && _connection.State == System.Data.ConnectionState.Open;
        public string CurrentDbPath => _dbPath;

        /// <summary>
        /// 외부 DB 파일에 연결합니다.
        /// </summary>
        public void Connect(string dbFilePath)
        {
            if (!File.Exists(dbFilePath))
            {
                throw new FileNotFoundException($"DB 파일을 찾을 수 없습니다: {dbFilePath}");
            }

            _dbPath = dbFilePath;
            var connectionString = $"Data Source={dbFilePath}";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
        }

        /// <summary>
        /// 연결을 닫습니다.
        /// </summary>
        public void Disconnect()
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
            _dbPath = null;
        }

        /// <summary>
        /// 모든 문서 목록을 조회합니다.
        /// </summary>
        public async Task<List<ApprovalDocument>> GetAllDocumentsAsync()
        {
            EnsureConnected();

            var sql = @"
                SELECT 
                    doc_id as DocId,
                    issue_key as IssueKey,
                    title as Title,
                    table_type as TableType,
                    gen_type as GenType,
                    inj_type as InjType,
                    creator_name as WriterName,
                    created_date as CreateDate,
                    approver_name as CurrentApproverId,
                    approval_time as ApprovalTime,
                    status as Status,
                    approver_comment as ApproverComment,
                    description as Description,
                    participants as Participants
                FROM approval_documents
                ORDER BY doc_id DESC";

            var docs = await _connection.QueryAsync<ApprovalDocument>(sql);
            return docs.AsList();
        }

        /// <summary>
        /// 특정 문서와 체크리스트 항목을 조회합니다.
        /// </summary>
        public async Task<ApprovalDocument> GetDocumentWithChecklistAsync(int docId)
        {
            EnsureConnected();

            var docSql = @"
                SELECT 
                    doc_id as DocId,
                    issue_key as IssueKey,
                    title as Title,
                    table_type as TableType,
                    gen_type as GenType,
                    inj_type as InjType,
                    creator_name as WriterName,
                    created_date as CreateDate,
                    approver_name as CurrentApproverId,
                    approval_time as ApprovalTime,
                    status as Status,
                    approver_comment as ApproverComment,
                    description as Description,
                    participants as Participants
                FROM approval_documents
                WHERE doc_id = @DocId";

            var doc = await _connection.QueryFirstOrDefaultAsync<ApprovalDocument>(docSql, new { DocId = docId });
            
            if (doc != null)
            {
                var checklistSql = @"
                    SELECT 
                        item_id as ItemId,
                        doc_id as DocId,
                        row_no as RowNo,
                        check_item as CheckItem,
                        output_content as OutputContent,
                        evaluation_code as EvaluationCode,
                        remarks as Remarks,
                        display_order as DisplayOrder
                    FROM checklist_items
                    WHERE doc_id = @DocId
                    ORDER BY display_order";

                var items = await _connection.QueryAsync<ChecklistItem>(checklistSql, new { DocId = docId });
                doc.ChecklistItems = items.AsList();
            }

            return doc;
        }

        /// <summary>
        /// 문서를 저장합니다 (Insert or Update).
        /// </summary>
        public async Task<int> SaveDocumentAsync(ApprovalDocument doc)
        {
            EnsureConnected();

            if (doc.DocId == 0)
            {
                // Insert
                var insertSql = @"
                    INSERT INTO approval_documents 
                        (issue_key, title, table_type, gen_type, inj_type, creator_name, 
                         created_date, approver_name, status, description, participants)
                    VALUES 
                        (@IssueKey, @Title, @TableType, @GenType, @InjType, @WriterName,
                         @CreateDate, @CurrentApproverId, @Status, @Description, @Participants);
                    SELECT last_insert_rowid();";

                doc.DocId = await _connection.ExecuteScalarAsync<int>(insertSql, new
                {
                    doc.IssueKey,
                    doc.Title,
                    doc.TableType,
                    doc.GenType,
                    doc.InjType,
                    doc.WriterName,
                    CreateDate = doc.CreateDate.ToString("yyyy-MM-dd"),
                    doc.CurrentApproverId,
                    Status = doc.Status.ToString(),
                    doc.Description,
                    doc.Participants
                });
            }
            else
            {
                // Update
                var updateSql = @"
                    UPDATE approval_documents SET
                        title = @Title,
                        table_type = @TableType,
                        gen_type = @GenType,
                        inj_type = @InjType,
                        approver_name = @CurrentApproverId,
                        status = @Status,
                        approver_comment = @ApproverComment,
                        description = @Description,
                        participants = @Participants
                    WHERE doc_id = @DocId";

                await _connection.ExecuteAsync(updateSql, new
                {
                    doc.DocId,
                    doc.Title,
                    doc.TableType,
                    doc.GenType,
                    doc.InjType,
                    doc.CurrentApproverId,
                    Status = doc.Status.ToString(),
                    doc.ApproverComment,
                    doc.Description,
                    doc.Participants
                });
            }

            return doc.DocId;
        }

        /// <summary>
        /// 체크리스트 항목들을 저장합니다.
        /// </summary>
        public async Task SaveChecklistItemsAsync(int docId, List<ChecklistItem> items)
        {
            EnsureConnected();

            // 기존 항목 삭제
            await _connection.ExecuteAsync("DELETE FROM checklist_items WHERE doc_id = @DocId", new { DocId = docId });

            // 새 항목 삽입
            var insertSql = @"
                INSERT INTO checklist_items 
                    (doc_id, row_no, check_item, output_content, evaluation_code, remarks, display_order)
                VALUES 
                    (@DocId, @RowNo, @CheckItem, @OutputContent, @EvaluationCode, @Remarks, @DisplayOrder)";

            foreach (var item in items)
            {
                item.DocId = docId;
                await _connection.ExecuteAsync(insertSql, item);
            }
        }

        /// <summary>
        /// Clone용: 기존 문서의 체크리스트를 복사합니다.
        /// </summary>
        public async Task<List<ChecklistItem>> CloneChecklistFromDocumentAsync(int sourceDocId)
        {
            EnsureConnected();

            var sql = @"
                SELECT 
                    row_no as RowNo,
                    check_item as CheckItem,
                    output_content as OutputContent,
                    evaluation_code as EvaluationCode,
                    remarks as Remarks,
                    display_order as DisplayOrder
                FROM checklist_items
                WHERE doc_id = @DocId
                ORDER BY display_order";

            var items = await _connection.QueryAsync<ChecklistItem>(sql, new { DocId = sourceDocId });
            return items.AsList();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("DB에 연결되지 않았습니다. Connect() 메서드를 먼저 호출하세요.");
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
