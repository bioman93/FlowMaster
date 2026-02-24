using System;
using System.IO;
using System.Text;

namespace FlowMaster.Desktop.Services
{
    /// <summary>
    /// 간단한 파일 로거.
    /// 로그 위치: FlowMaster.exe 폴더\logs\FlowMaster_yyyyMMdd.log
    /// </summary>
    public static class AppLogger
    {
        private static readonly string LogDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        private static readonly object _lock = new object();

        private static string LogFile =>
            Path.Combine(LogDir, $"FlowMaster_{DateTime.Today:yyyyMMdd}.log");

        public static void Info(string message)          => Write("INFO ", message);
        public static void Warn(string message)          => Write("WARN ", message);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex = null)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDir);

                    var sb = new StringBuilder();
                    sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");

                    if (ex != null)
                    {
                        sb.AppendLine($"           Exception : {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                            sb.AppendLine($"           Inner     : {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        sb.AppendLine($"           StackTrace: {ex.StackTrace?.Trim()}");
                    }

                    File.AppendAllText(LogFile, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { /* 로그 기록 실패는 무시 */ }
        }
    }
}
