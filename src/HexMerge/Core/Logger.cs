using System;
using System.IO;

namespace HexMerge.Core
{
    /// <summary>
    /// 简易文件日志器。日志写到程序目录下 logs\HexMerge.log，供出错时让用户提供排查。
    /// 线程安全（lock）；写日志失败一律静默——日志本身绝不应影响主程序运行。
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logPath;

        /// <summary>单个日志文件上限：超过则滚动（.log → .log.1）。</summary>
        private const long MaxLogBytes = 2 * 1024 * 1024; // 2MB

        /// <summary>初始化：在指定目录创建日志文件并写入起始行。应用启动时调用一次。</summary>
        public static void Init(string logDirectory)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(logDirectory);
                    _logPath = Path.Combine(logDirectory, "HexMerge.log");
                }
                catch
                {
                    _logPath = null; // 目录创建失败则禁用日志
                }
            }
            Info("===== 日志开始 =====");
        }

        /// <summary>当前日志文件完整路径（供向用户提示日志位置）。</summary>
        public static string LogFilePath { get { return _logPath; } }

        public static void Info(string msg) { Write("INFO ", msg, null); }
        public static void Warn(string msg) { Write("WARN ", msg, null); }
        public static void Error(string msg, Exception ex) { Write("ERROR", msg, ex); }

        private static void Write(string level, string msg, Exception ex)
        {
            if (_logPath == null) return;
            try
            {
                lock (_lock)
                {
                    RollIfNeeded(); // 超过阈值则滚动，避免日志无限增长
                    string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    using (StreamWriter sw = new StreamWriter(_logPath, append: true))
                    {
                        sw.WriteLine("[{0}] {1} {2}", time, level, msg);
                        if (ex != null)
                        {
                            sw.WriteLine("        异常类型: " + ex.GetType().FullName);
                            sw.WriteLine("        消息:     " + ex.Message);
                            sw.WriteLine("        堆栈:");
                            sw.WriteLine("        " + (ex.StackTrace ?? "(无堆栈)"));
                            if (ex.InnerException != null)
                            {
                                Exception ie = ex.InnerException;
                                sw.WriteLine("        内部异常: " + ie.GetType().FullName);
                                sw.WriteLine("        内部消息: " + ie.Message);
                                sw.WriteLine("        " + (ie.StackTrace ?? "(无堆栈)"));
                            }
                        }
                    }
                }
            }
            catch
            {
                // 日志写失败：静默，不影响主程序
            }
        }

        /// <summary>日志超过 MaxLogBytes 时滚动：当前 .log 重命名为 .log.1（删旧 .log.1），
        /// 之后的日志写入新的 .log。滚动失败（如文件被占用）则忽略、继续追加当前文件。</summary>
        private static void RollIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                if (new FileInfo(_logPath).Length < MaxLogBytes) return;
                string backup = _logPath + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_logPath, backup);
            }
            catch
            {
                // 滚动失败：忽略，继续用当前文件追加
            }
        }
    }
}
