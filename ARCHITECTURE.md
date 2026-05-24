# WinActivityTracker — Architecture

## Overview

A Windows activity tracker that records which applications you use and for how long. Three-layer architecture:

```
Win32 API  →  Trackers (BackgroundService)  →  SQLite  →  REST API (:5200)  →  Vue SPA (:5000)
```

## Project Layout

| Project | SDK | Role |
|---------|-----|------|
| `WinActivityTracker.Core/` | `Microsoft.NET.Sdk` (classlib) | Shared library: models, Win32 interop, trackers, settings |
| `WinActivityTracker.Service/` | `Microsoft.NET.Sdk.Web` | Backend process: hosts trackers + REST API |
| `WinActivityTracker.Web/` | `Microsoft.NET.Sdk.Web` + Vite | Frontend: Vue 3 SPA, production static file server |

Target: `net10.0-windows10.0.19041.0` — required for WinRT MediaSession and Win32 P/Invoke.

## Data Flow

1. **WindowTracker** (every N seconds, default 3s):
   - Calls `GetForegroundWindow()` — if the window changed, writes a `FocusChange` record for the *previous* window (duration = now - focusStart)
   - Calls `EnumWindows()` — writes a batch of `WindowSnapshot` rows (one per visible window)

2. **ProcessTracker** (every N seconds, default 30s):
   - Calls `Process.GetProcesses()` — filters out processes with visible windows
   - Writes a batch of `ProcessSnapshot` rows for background-only processes

3. **MediaSessionTracker** (every N seconds, default 5s):
   - Calls WinRT `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
   - Deduplicates by title+artist, writes `MediaSessionRecord` on change

4. **IdleDetector** (inline with WindowTracker):
   - Calls `GetLastInputInfo()` — returns tick count of last keyboard/mouse event
   - When idle > threshold (default 2min): pauses WindowTracker until input resumes

5. **SettingsService** (on API call):
   - Mutates in-memory `TrackerSettings` and persists to `settings.json`
   - Trackers read settings each poll cycle — no restart needed

## Database

- **Engine**: SQLite via EF Core
- **Path**: `%LOCALAPPDATA%\WinActivityTracker\activity.db`
- **Tables**: `FocusChanges`, `WindowSnapshots`, `ProcessSnapshots`, `MediaSessionRecords`, `DailySummaries` (reserved)
- **Indexes**: All `Timestamp` columns + `FocusChanges.ProcessName` + unique `(Date, ProcessName)` on DailySummaries
- **Maintenance**: `POST /api/db/cleanup` deletes old records and runs `VACUUM`

## Key Design Decisions

### Why polling instead of hooks?
`SetWinEventHook` requires a Windows message pump (not available in a console/background service). Polling is simpler and works reliably in all contexts. A 3s interval is sufficient — humans don't switch windows faster than that.

### Why EF Core instead of raw SQL?
Migrations, LINQ queries, and type safety with minimal overhead. The data volume is small enough that EF's overhead is negligible.

### Why separate Web project?
The backend can run headless as a Windows Service. The frontend is optional — you can query the REST API with curl, PowerShell, or any HTTP client.
