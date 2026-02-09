using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace FlowMaster.Infrastructure.Utilities
{
    /// <summary>
    /// SQLite DB 스키마 추출 유틸리티
    /// </summary>
    public static class DbSchemaExtractor
    {
        /// <summary>
        /// SQLite DB 파일의 스키마를 추출하여 텍스트 파일로 저장합니다.
        /// </summary>
        /// <param name="dbFilePath">분석할 SQLite DB 파일 경로</param>
        /// <param name="outputFilePath">출력할 텍스트 파일 경로 (null이면 DB 파일과 같은 위치에 생성)</param>
        /// <returns>생성된 스키마 파일 경로</returns>
        public static string ExtractSchema(string dbFilePath, string outputFilePath = null)
        {
            if (!File.Exists(dbFilePath))
            {
                throw new FileNotFoundException($"DB 파일을 찾을 수 없습니다: {dbFilePath}");
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                outputFilePath = Path.ChangeExtension(dbFilePath, ".schema.txt");
            }

            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"SQLite DB Schema Report");
            sb.AppendLine($"Source: {dbFilePath}");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("========================================");
            sb.AppendLine();

            var connectionString = $"Data Source={dbFilePath};Mode=ReadOnly";

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // 1. 테이블 목록 조회
                sb.AppendLine("## 테이블 목록 (Tables)");
                sb.AppendLine("----------------------------------------");

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var tableName = reader.GetString(0);
                            sb.AppendLine($"  - {tableName}");
                        }
                    }
                }

                sb.AppendLine();

                // 2. 각 테이블의 상세 구조
                sb.AppendLine("## 테이블 상세 구조 (Table Details)");
                sb.AppendLine("========================================");

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var tableName = reader.GetString(0);
                            var createSql = reader.IsDBNull(1) ? "(no SQL)" : reader.GetString(1);

                            sb.AppendLine();
                            sb.AppendLine($"### {tableName}");
                            sb.AppendLine("```sql");
                            sb.AppendLine(createSql);
                            sb.AppendLine("```");

                            // 컬럼 정보 (PRAGMA 사용)
                            sb.AppendLine();
                            sb.AppendLine("| Column | Type | NotNull | PK |");
                            sb.AppendLine("|--------|------|---------|-----|");

                            using (var pragmaCmd = connection.CreateCommand())
                            {
                                pragmaCmd.CommandText = $"PRAGMA table_info('{tableName}')";
                                using (var pragmaReader = pragmaCmd.ExecuteReader())
                                {
                                    while (pragmaReader.Read())
                                    {
                                        var colName = pragmaReader.GetString(1);
                                        var colType = pragmaReader.GetString(2);
                                        var notNull = pragmaReader.GetInt32(3) == 1 ? "YES" : "NO";
                                        var pk = pragmaReader.GetInt32(5) > 0 ? "YES" : "";
                                        sb.AppendLine($"| {colName} | {colType} | {notNull} | {pk} |");
                                    }
                                }
                            }

                            // 행 개수
                            using (var countCmd = connection.CreateCommand())
                            {
                                countCmd.CommandText = $"SELECT COUNT(*) FROM '{tableName}'";
                                var rowCount = countCmd.ExecuteScalar();
                                sb.AppendLine();
                                sb.AppendLine($"*Row Count: {rowCount}*");
                            }
                        }
                    }
                }

                // 3. 인덱스 정보
                sb.AppendLine();
                sb.AppendLine("## 인덱스 (Indexes)");
                sb.AppendLine("----------------------------------------");

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, tbl_name, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL ORDER BY tbl_name, name";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var indexName = reader.GetString(0);
                            var tableName = reader.GetString(1);
                            var sql = reader.GetString(2);
                            sb.AppendLine($"- [{tableName}] {indexName}");
                            sb.AppendLine($"  ```sql");
                            sb.AppendLine($"  {sql}");
                            sb.AppendLine($"  ```");
                        }
                    }
                }
            }

            File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);
            return outputFilePath;
        }
    }
}
