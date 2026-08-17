// Task 10: Toast 激活转发管道。
// 主实例监听命名管道，接收 toast 激活进程（-Embedding 拉起的新实例）转发的
// "open-time" 请求；ToastActivationClient 由激活进程调用发送。relay 触发
// OpenTimeRequested 事件 → TrayApplicationContext 打开 /time 面板。
using System.IO.Pipes;

namespace WinActivityTracker.Service.Native;

/// <summary>主实例监听命名管道，接收 toast 激活进程的转发请求。</summary>
public sealed class ToastActivationRelay : IDisposable
{
    public const string PipeName = "taskmonitor114-toast-activation";
    public event Action<string>? OpenTimeRequested;
    private readonly CancellationTokenSource _cts = new();

    public void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server);
                    var msg = await reader.ReadLineAsync(_cts.Token);
                    if (msg == "open-time") OpenTimeRequested?.Invoke(msg);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // 客户端断开 / 管道名被占用（重复实例）等：短暂退避再重试，避免空转占满 CPU。
                    try { await Task.Delay(200, _cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        });
    }

    public void Dispose() => _cts.Cancel();
}

/// <summary>toast 激活进程（-Embedding）：把 "open-time" 转发给主实例后退出。</summary>
public static class ToastActivationClient
{
    public static async Task SendAsync()
    {
        await using var client = new NamedPipeClientStream(".", ToastActivationRelay.PipeName, PipeDirection.Out);
        await client.ConnectAsync(1000);
        await using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("open-time");
    }
}
