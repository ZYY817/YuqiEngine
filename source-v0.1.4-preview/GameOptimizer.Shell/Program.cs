// File: GameOptimizer.Shell/Program.cs
// Why: WPF 自动生成的 Main() 没有任何异常保护，静态字段初始化失败时进程直接崩溃无痕迹。
// 自定义入口在最外层包裹 try-catch，并在 exe 同目录写启动诊断日志。
using System;
using System.IO;
using System.Runtime.InteropServices;
using GameOptimizer.Core.Constants;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell;

public static class Program
{
    private static string DiagnosticLogPath => Path.Combine(AppPaths.LogsDir, "startup_diagnostic.log");

    [STAThread]
    public static int Main(string[] args)
    {
        // Why: .NET 8 默认不带 GBK/Big5 等非 UTF 代码页，GetEncoding(936) 未注册 provider 会
        // 抛 ArgumentException——schtasks/powercfg/ipconfig 等 OEM 输出解码点散落多处，
        // 统一在入口注册一次，避免"冷启动首个调用即炸/回退 UTF-8 乱码"。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        ConfigureDataRootForProcess(args);
        if (args.Length == 5
            && string.Equals(args[0], "--system-restore-writer", StringComparison.Ordinal)
            && string.Equals(args[1], "--request", StringComparison.Ordinal)
            && string.Equals(args[3], "--response", StringComparison.Ordinal))
        {
            return SystemRestorePointWriterEntryPoint.RunFiles(args[2], args[4]);
        }

        try
        {
            WriteDiagnostic("=== YuqiEngine Startup ===");
            WriteDiagnostic($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteDiagnostic($"OS: {Environment.OSVersion}");
            WriteDiagnostic($"64bit: {Environment.Is64BitOperatingSystem}");
            WriteDiagnostic($"CLR: {Environment.Version}");
            WriteDiagnostic($"BaseDir: {AppDomain.CurrentDomain.BaseDirectory}");

            if (!RunEnvironmentPreCheck())
                return 1;

            WriteDiagnostic("Environment pre-check passed, launching WPF...");
            LaunchWpfApp();
            return 0;
        }
        catch (Exception ex)
        {
            WriteDiagnostic($"FATAL: {ex}");
            ShowNativeMessageBox(
                $"YuqiEngine 启动失败 / Failed to start\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"诊断日志 / Diagnostic log:\n{DiagnosticLogPath}",
                "YuqiEngine - Startup Error");
            return 1;
        }
    }

    private static void LaunchWpfApp()
    {
        WriteDiagnostic("LaunchWpfApp: constructing App");
        var app = new App();
        WriteDiagnostic("LaunchWpfApp: App constructed; initializing components");
        app.InitializeComponent();
        WriteDiagnostic("LaunchWpfApp: InitializeComponent completed; entering app.Run");
        app.Run();
        WriteDiagnostic("LaunchWpfApp: app.Run returned");
    }

    /// <summary>
    /// 启动前环境预检 — 在 WPF 框架初始化之前，用纯 Win32 弹窗报告问题
    /// </summary>
    private static bool RunEnvironmentPreCheck()
    {
        // 1. Windows 版本检查（需要 10.0.19041+）
        var os = Environment.OSVersion;
        if (os.Version < new Version(10, 0, 19041))
        {
            var msg = $"YuqiEngine 需要 Windows 10 2004 (19041) 或更高版本。\n" +
                      $"当前系统: {os.VersionString}\n\n" +
                      $"YuqiEngine requires Windows 10 2004 (19041) or later.\n" +
                      $"Current: {os.VersionString}";
            WriteDiagnostic($"BLOCKED: OS version too old: {os.VersionString}");
            ShowNativeMessageBox(msg, "YuqiEngine - 系统版本不兼容");
            return false;
        }
        WriteDiagnostic($"OS version OK: {os.VersionString}");

        try
        {
            var testDir = AppPaths.AppDataRoot;
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, $".startup_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            WriteDiagnostic($"AppData writable: {testDir}");
        }
        catch (Exception ex)
        {
            WriteDiagnostic($"BLOCKED: AppData not writable: {ex.Message}");
            ShowNativeMessageBox(
                $"无法写入用户数据目录:\n{AppPaths.AppDataRoot}\n\n错误: {ex.Message}\n\n" +
                "请尝试以管理员身份运行，或检查磁盘权限。",
                "YuqiEngine - 权限不足");
            return false;
        }

        // 3. 关键 DLL 存在性检查（self-contained 完整性）
        // Why: framework-dependent 模式下 coreclr/clrjit/hostpolicy 由系统运行时提供，不在 bin 目录
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var isSelfContained = File.Exists(Path.Combine(baseDir, "coreclr.dll"));
        var criticalFiles = isSelfContained
            ? new[] { "coreclr.dll", "clrjit.dll", "hostpolicy.dll", "YuqiEngine.dll" }
            : new[] { "YuqiEngine.dll" };
        foreach (var dll in criticalFiles)
        {
            var path = Path.Combine(baseDir, dll);
            if (!File.Exists(path))
            {
                WriteDiagnostic($"BLOCKED: Missing critical file: {dll}");
                ShowNativeMessageBox(
                    $"缺少关键文件: {dll}\n\n请重新解压完整安装包，确保所有文件完整。\n" +
                    "如果使用杀毒软件，请将 YuqiEngine 文件夹加入白名单。\n\n" +
                    $"Missing critical file: {dll}\nPlease re-extract the full package.",
                    "YuqiEngine - 文件缺失");
                return false;
            }
        }
        WriteDiagnostic("Critical DLLs present");

        return true;
    }

    private static void ConfigureDataRootForProcess(string[] args)
    {
        if (args.Any(argument => argument.Equals("--acceptance-ui", StringComparison.OrdinalIgnoreCase)))
        {
            var session = args.FirstOrDefault(argument =>
                    argument.StartsWith("--acceptance-session=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=', 2)[1];
            session = string.IsNullOrWhiteSpace(session)
                ? "default"
                : new string(session.Where(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_').Take(32).ToArray());
            if (string.IsNullOrWhiteSpace(session)) session = "default";
            Environment.SetEnvironmentVariable(
                "YUQIENGINE_DATA_ROOT",
                Path.Combine(AppPaths.PreferredAppDataRoot, "Acceptance", session));
        }
        else if (args.Any(argument => argument.Equals("--recovery-dev", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.SetEnvironmentVariable(
                "YUQIENGINE_DATA_ROOT",
                Path.Combine(AppPaths.PreferredAppDataRoot, "RecoveryDevelopment"));
        }
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticLogPath)!);
            File.AppendAllText(DiagnosticLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 连日志都写不了就算了，至少不能因为写日志而崩溃
        }
    }

    /// <summary>
    /// 使用 Win32 原生 MessageBox，不依赖 WPF 框架
    /// Why: 如果 WPF 初始化本身就失败了，System.Windows.MessageBox 也用不了
    /// </summary>
    private static void ShowNativeMessageBox(string text, string caption)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, 0x00000010); // MB_ICONERROR
        }
        catch
        {
            // P/Invoke 也失败的话，日志里已经有记录了
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
