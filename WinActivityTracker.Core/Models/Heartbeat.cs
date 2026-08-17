// Single-row table — last time the program was alive.
// Updated every 30s by HeartbeatService. On startup, if the gap
// exceeds 33s (30s + 3s tolerance), HeartbeatService records
// Sleep+Wake or Shutdown+Start SystemEvents.
namespace WinActivityTracker.Core.Models;

public class Heartbeat
{
    public int Id { get; set; } = 1;  // single row
    public DateTime LastTick { get; set; }
    public ulong LastMonotonicMs { get; set; }  // 上次心跳的 GetTickCount64；0 = 未初始化
    public DateTime BootWallTime { get; set; }  // 当前 boot 的墙钟起点（服务启动时锚定）；default = 旧数据
}
