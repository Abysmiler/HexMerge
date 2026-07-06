using System.Reflection;

namespace HexMerge
{
    /// <summary>
    /// 应用级常量。版本号取自程序集（AssemblyInfo.cs 的 AssemblyVersion），
    /// 窗口标题统一为 "HexMerge V{版本}"。
    /// 改版本只需改 AssemblyInfo.cs，此处与所有窗口标题自动同步。
    /// </summary>
    public static class AppInfo
    {
        /// <summary>当前版本号（如 "1.0.0.1"），来自程序集 AssemblyVersion。</summary>
        public static string Version
        {
            get { return typeof(AppInfo).Assembly.GetName().Version.ToString(); }
        }

        /// <summary>统一窗口标题："HexMerge V{版本}"。</summary>
        public static string Title
        {
            get { return "HexMerge V" + Version; }
        }
    }
}
