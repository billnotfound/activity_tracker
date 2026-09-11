// 服务模式通知的 helper 进程入口。
//
// --toast-helper 模式：由服务（会话 0）CreateProcessAsUser 投递到交互会话的同 exe
// 分支。只弹 toast 并处理点击（开浏览器到 /time），不启动追踪器、不做单实例检查。
//
// 服务进程把异常 ID / 偏移 / API 端口随命令行传入（LocalSystem 的 DB 与设置路径在
// 会话 0 侧；helper 在用户桌面侧重解析 AppPaths 会读到交互用户的目录，拿不到服务侧
// 的异常/端口）——helper 不读 settings.json / DB，仅用命令行参数弹 toast 并转发点击。
//
// 点击路由：toast launch 参数内嵌 action=open-time → 系统以
// -Embedding 拉起同 exe → ToastActivationClient 命名管道转发 open-time → 本进程
// 自带的 ToastActivationRelay 触发事件 → Process.Start 打开 http://localhost:{port}/time
// （API 在服务模式常开，端口由服务侧经 --port 传入）。免打包 exe 无 AUMID 注册，
// 实际点击投递需在交互桌面环境验证。
using System.Diagnostics;
using System.Globalization;
using WinActivityTracker.Core.Models;
using WinActivityTracker.Core.Services;

namespace WinActivityTracker.Service.Native;

public static class ToastHelper
{
    public static bool IsHelperMode(string[] args) => args.Contains("--toast-helper");

    public static long? ParseArgs(string[] args)
    {
        var v = GetArgValue(args, "--toast-helper");
        return v != null && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id : null;
    }

    public static double? ParseOffset(string[] args)
    {
        var v = GetArgValue(args, "--offset");
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
            ? offset : null;
    }

    public static int? ParsePort(string[] args)
    {
        var v = GetArgValue(args, "--port");
        return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            ? port : null;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        if (idx < 0 || idx + 1 >= args.Length) return null;
        return args[idx + 1];
    }

    /// <summary>
    /// 服务模式 helper 入口（交互会话内、无窗口）：
    /// 1. 校验 --toast-helper/--offset/--port 参数；缺任一则记日志退出（不读 DB / 设置）
    /// 2. 用命令行传入的偏移弹确认 toast（立即 + 2 分钟提醒）
    /// 3. 宿主 ToastActivationRelay：toast 点击 → -Embedding 拉起 → 管道转发 →
    ///    开 http://localhost:{port}/time
    /// 4. 无窗口消息泵 Application.Run()；3 分钟后自动退出（覆盖初始 toast +
    ///    2 分钟提醒的展示窗口，避免 helper 常驻堆积）
    /// </summary>
    public static void Run(string[] args)
    {
        var anomalyId = ParseArgs(args);
        var offset = ParseOffset(args);
        var port = ParsePort(args);
        if (anomalyId == null || offset == null || port == null)
        {
            Console.Error.WriteLine(
                "[toast-helper] missing/invalid args (need --toast-helper <id> --offset <seconds> --port <port>); exiting.");
            return;
        }

        // helper 分支在 Program.cs 的 i18n 初始化之前执行，此处补初始化，
        // 否则 toast 标题/正文退回显示原始 key。
        var lang = CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "zh-CN" : "en-US";
        _ = new I18nService(lang);

        using var relay = new ToastActivationRelay();
        relay.OpenTimeRequested += _ =>
        {
            try
            {
                Process.Start(new ProcessStartInfo($"http://localhost:{port.Value}/time")
                {
                    UseShellExecute = true
                });
            }
            catch { /* 浏览器启动失败静默 */ }
        };
        relay.Start();

        var notifier = new ToastNotifier();
        notifier.NotifyConfirmed(new TimeAnomaly
        {
            Id = anomalyId.Value,
            OffsetSeconds = offset.Value
        });

        // 无窗口消息泵：保持进程存活以展示 toast 和处理点击
        var timer = new System.Windows.Forms.Timer { Interval = 180_000 };
        timer.Tick += (_, _) => { timer.Stop(); System.Windows.Forms.Application.ExitThread(); };
        timer.Start();
        System.Windows.Forms.Application.Run();
    }
}
