using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FTHexMerge.Core;
using FTHexMerge.Views;

namespace FTHexMerge
{
    /// <summary>
    /// App.xaml 的交互逻辑。
    /// 启动时初始化日志，并注册全局异常捕获——确保任何未处理异常（UI 线程 / 后台线程 / Task）
    /// 都记入日志后再提示用户，方便事后排查。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 日志初始化：写到程序目录下 logs\FTHexMerge.log
            Logger.Init(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));
            Logger.Info("FTHexMerge 启动，版本 V" + AppInfo.Version);

            // 全局异常兜底：UI 线程
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            // 全局异常兜底：非 UI 线程（后台 Task 等）
            AppDomain.CurrentDomain.UnhandledException += App_DomainUnhandledException;
            // 全局异常兜底：未观察的 Task 异常
            TaskScheduler.UnobservedTaskException += App_UnobservedTaskException;
        }

        // ===== 循环崩溃防护 =====
        // 渲染/布局异常会在每帧重复抛出，若每次都弹框会把用户困在弹窗里（恶性问题）。
        // 两层防护：① 同类异常短时间去重（减少刷屏）；② 短时间累计过多 → 判定状态已坏，直接退出。
        private static int _recentErrors;
        private static DateTime _windowStart = DateTime.MinValue;
        private static string _lastErrorKey;
        private static DateTime _lastBoxTime = DateTime.MinValue;
        private const int MaxRecentErrors = 5;   // 5 秒内累计 5 次 → 退出
        private const double WindowSeconds = 5.0;
        private const double DedupSeconds = 3.0; // 同类异常 3 秒内只弹一次

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("UI 线程未处理异常", e.Exception);

            DateTime now = DateTime.Now;
            // 1) 滑动窗口计数：5 秒内的异常次数
            if ((now - _windowStart).TotalSeconds > WindowSeconds) { _windowStart = now; _recentErrors = 0; }
            _recentErrors++;

            // 2) 短时间异常过多 = 循环崩溃，程序状态已坏 → 退出，避免反复弹框把用户困住
            if (_recentErrors >= MaxRecentErrors)
            {
                Logger.Error("短时间内异常过多，判定为循环崩溃，程序退出", null);
                CardDialog.Show("FTHexMerge", "程序遇到严重错误，将退出以避免反复弹窗。\n请联系开发人员提供日志（logs 文件夹）。");
                Environment.Exit(1);
                return;
            }

            // 3) 去重：同类异常（类型+消息）3 秒内只弹一次，减少刷屏
            string key = e.Exception != null ? e.Exception.GetType().Name + "|" + e.Exception.Message : "null";
            if (key == _lastErrorKey && (now - _lastBoxTime).TotalSeconds < DedupSeconds)
            {
                e.Handled = true;
                return;
            }
            _lastErrorKey = key;
            _lastBoxTime = now;

            ShowError();
            e.Handled = true;
        }

        private void App_DomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error("AppDomain 未处理异常（" + (e.IsTerminating ? "终止中" : "非终止") + "）", e.ExceptionObject as Exception);
        }

        private void App_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Task 未观察异常", e.Exception);
            e.SetObserved(); // 标记已观察，阻止进程终止
        }

        /// <summary>向用户显示简单的出错提示（详细信息已写入日志，由开发人员排查）。</summary>
        private static void ShowError()
        {
            CardDialog.Show("FTHexMerge", "程序遇到错误，请稍后重试。");
        }
    }
}
