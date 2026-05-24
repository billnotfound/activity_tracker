# WinActivityTracker — Maintenance Guide

## Quick Start

```bash
# 1. Start backend (monitoring + API + native tray icon)
dotnet run --project WinActivityTracker.Service

# 2. Start frontend (dev mode with HMR) — optional, tray menu opens Web UI
cd WinActivityTracker.Web && pnpm dev

# 3. Use the tray icon or open:
# http://localhost:5000   (Vue dev server)
# http://localhost:5200   (API + embedded Web UI in tray mode)
```

## System Tray

The Service now starts with a **notification area icon** (the icon is a generic application icon):

| Action | Result |
|--------|--------|
| 双击托盘图标 | 在浏览器中打开仪表盘 |
| 右键 → 打开仪表盘 | 浏览器打开 `http://localhost:5200` |
| 右键 → 打开设置 | 浏览器打开 Web 设置页 |
| 右键 → 显示状态窗口 | 原生 WinForms 状态窗口 (当前焦点 + Top 5) |
| 右键 → 暂停/恢复追踪 | 切换 `trackingEnabled` |
| 右键 → 退出 | 停止服务并关闭托盘 |

Tray mode is automatically **disabled** when running as a Windows Service (no GUI available).

### Settings JSON with Comments

`settings.json` is written with `//` comment lines for hand-editing:

```jsonc
{
  // ===== 追踪控制 =====
  // 主开关 — false 时所有追踪器暂停，API 照常运行。相当于"暂停"功能。
  "TrackingEnabled": true,

  // ===== 轮询间隔 (秒) =====
  // 窗口/焦点追踪轮询间隔。影响焦点切换检测精度。最小 1。
  "WindowPollSeconds": 3,
  ...
}
```

- **Save**: API calls and Settings page write through `SettingsService.Save()` which adds comments
- **Load**: Comment lines (starting with `//`) are stripped before JSON parsing
- **Manual edit**: edit `%LOCALAPPDATA%\WinActivityTracker\settings.json` in Notepad, restart service

## Production Deployment

```bash
# Build frontend
cd WinActivityTracker.Web && pnpm build   # → wwwroot/

# Run backend only (serves API on :5200)
dotnet run --project WinActivityTracker.Service

# Optionally, serve the built frontend
dotnet run --project WinActivityTracker.Web   # serves wwwroot/ on default port
```

## Install as Windows Service

```powershell
# Create service (run as Administrator)
New-Service -Name WinActivityTracker `
  -BinaryPathName "C:\path\to\WinActivityTracker.Service.exe" `
  -Description "Windows Activity Tracker" `
  -StartupType Automatic

Start-Service WinActivityTracker

# Or via sc.exe
sc create WinActivityTracker binPath= "C:\path\to\WinActivityTracker.Service.exe" start= auto
sc start WinActivityTracker
```

## File Locations

| File | Path |
|------|------|
| Database | `%LOCALAPPDATA%\WinActivityTracker\activity.db` |
| Settings | `%LOCALAPPDATA%\WinActivityTracker\settings.json` |

## Database Management

### Check current size and growth

```bash
curl http://localhost:5200/api/db/stats
```

### Clean up old data

```bash
# Delete records older than 30 days
curl -X POST http://localhost:5200/api/db/cleanup?days=30

# Delete records older than 7 days (aggressive)
curl -X POST http://localhost:5200/api/db/cleanup?days=7
```

### Direct SQLite access

```bash
sqlite3 %LOCALAPPDATA%/WinActivityTracker/activity.db

# Useful queries:
.tables                           -- list all tables
SELECT COUNT(*) FROM WindowSnapshots;   -- fastest-growing table
SELECT processName, SUM(durationSeconds) AS s
  FROM FocusChanges
  GROUP BY processName ORDER BY s DESC LIMIT 10;
```

## Pausing / Resuming Tracking

The `trackingEnabled` setting acts as a master switch. When `false`, all trackers skip their polling logic — no data is written to the DB. The API continues to serve requests.

```bash
# Pause all tracking
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"trackingEnabled": false}'

# Resume tracking
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"trackingEnabled": true}'
```

Also available in the Web UI at **设置** → **追踪已启用** toggle.

## Tuning Polling Rates

Frequent polling = more data = faster DB growth. Adjust based on needs:

```bash
# Lightweight: save disk space
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"windowPollSeconds": 10, "processPollSeconds": 120}'

# High-resolution: capture everything
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"windowPollSeconds": 2, "processPollSeconds": 15}'
```

## Excluding Processes

Excluded processes are filtered from ALL trackers (focus, visible windows, background, media):

```bash
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"excludedProcesses": ["explorer", "SearchApp", "TextInputHost", "SystemSettings"]}'
```

Common exclusions:
- `explorer` — File Explorer windows
- `ApplicationFrameHost` — UWP app frame (the child process has the real name)
- `TextInputHost` — Windows touch keyboard
- `SearchHost` — Windows Search

## Troubleshooting

### Port 5200 already in use
```powershell
netstat -ano | findstr 5200    # Find PID
taskkill /F /PID <pid>          # Kill it
```

### MediaSession not working
The WinRT API sometimes fails to initialize on first poll. The tracker catches `InvalidCastException` and retries. If it never works, ensure:
- Windows 10 build 19041 or later
- .NET SDK 10.0 with Windows workload

### Database locked errors
SQLite uses file-level locking. Only one process should write to the DB at a time. If you're running `sqlite3` CLI while the Service is running, close the CLI or use read-only mode.

### Build fails with "file locked by another process"
The Service process is still running. Run:
```powershell
taskkill /F /IM WinActivityTracker.Service.exe
```

## Upgrading .NET Version

1. Update `TargetFramework` in all three `.csproj` files
2. Run `dotnet restore`
3. Verify with `dotnet build`
4. If the Windows SDK version changed, update the TFM suffix (e.g. `net10.0-windows10.0.22621.0`)
