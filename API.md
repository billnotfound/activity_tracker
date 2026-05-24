# WinActivityTracker — API Reference

Base URL: `http://localhost:5200`

## Endpoints

### Health Check

**`GET /api/status`**
```json
{"status": "running", "trackingEnabled": true, "timestamp": "2026-05-25T00:00:00Z"}
```

---

### Settings

**`GET /api/settings`** — Read current configuration
```json
{
  "trackingEnabled": true,
  "windowPollSeconds": 3,
  "processPollSeconds": 30,
  "mediaPollSeconds": 5,
  "idleThresholdMinutes": 2,
  "excludedProcesses": [],
  "dataRetentionDays": 90,
  "autoCleanup": true
}
```

**`PUT /api/settings`** — Update configuration (runtime, no restart needed)
```bash
# Pause tracking
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"trackingEnabled": false}'

# Adjust intervals + exclude processes
curl -X PUT localhost:5200/api/settings \
  -H "Content-Type: application/json" \
  -d '{"windowPollSeconds": 5, "excludedProcesses": ["explorer"]}'
```
Minimum values enforced:
| Field | Min | Why |
|-------|-----|-----|
| `trackingEnabled` | — | `false` pauses all trackers; set `true` to resume |
| `windowPollSeconds` | 1 | Prevent CPU saturation |
| `processPollSeconds` | 5 | Process enumeration is expensive |
| `mediaPollSeconds` | 1 | — |
| `idleThresholdMinutes` | 1 | 0 would never detect idle |
| `dataRetentionDays` | 1 | 0 would delete everything |

---

### Activity Summary

**`GET /api/summary/today?date=2026-05-25`** — Focus time per process for a day
```json
[
  { "processName": "devenv",    "totalSeconds": 7200.5, "switchCount": 15 },
  { "processName": "firefox",   "totalSeconds": 3400.0, "switchCount": 8 }
]
```
- `date` is optional; defaults to today (server local time)
- Date boundaries are computed in local time → converted to UTC for DB query
- Results sorted by `totalSeconds` descending

**`GET /api/summary/range?from=2026-05-01&to=2026-05-25T23:59:59`** — Focus time over a date range
- Same response format as `/summary/today`
- Both parameters required

---

### Windows

**`GET /api/windows/current`** — Live visible windows (real-time, not from DB)
```json
[
  { "processName": "WindowsTerminal", "title": "~ pwsh",   "isFocused": true },
  { "processName": "firefox",         "title": "GitHub",   "isFocused": false }
]
```

**`GET /api/windows/timeline?from=2026-05-25T08:00:00&to=2026-05-25T12:00:00`** — Focus change history
```json
[
  { "timestamp": "2026-05-25T08:00:03Z", "processName": "WindowsTerminal", "windowTitle": "~ pwsh", "durationSeconds": 45.2 }
]
```
- Default range: last 1 hour

---

### Processes

**`GET /api/processes/snapshot`** — Latest background process batch
```json
[
  { "processName": "svchost", "processId": 1234 },
  { "processName": "sqlservr", "processId": 5678 }
]
```

---

### Media

**`GET /api/media/history?limit=50`** — Recent media playback records
```json
[
  { "timestamp": "...", "appName": "foobar2000.exe", "title": "Track Name", "artist": "Artist Name", "playbackStatus": "Playing" }
]
```
- `limit` defaults to 20

---

### Database Maintenance

**`GET /api/db/stats`** — Row counts, oldest record, growth rate
```json
{
  "focusChanges": 210,
  "windowSnapshots": 5560,
  "processSnapshots": 12363,
  "mediaRecords": 7,
  "oldestRecord": "2026-05-24T16:56:52Z",
  "newRecordsPerDay": 210.0
}
```

**`POST /api/db/cleanup?days=30`** — Delete records older than N days + `VACUUM`
```json
{
  "retentionDays": 30,
  "cutoff": "2026-04-25T...",
  "deleted": {
    "focusChanges": 0,
    "windowSnapshots": 0,
    "processSnapshots": 0,
    "mediaRecords": 0
  }
}
```
- `days` defaults to `settings.dataRetentionDays`
- `VACUUM` runs after deletion to reclaim disk — may be slow on large DBs (>100MB)
- Idempotent: safe to call repeatedly

---

## Data Volume Estimates

With default settings running 12 hours/day:

| Table | Rows per day | ~Size per month |
|-------|-------------|----------------|
| FocusChanges | 500–2000 | <1 MB |
| WindowSnapshots | 100k–300k | 20–60 MB |
| ProcessSnapshots | 30k–50k | 5–10 MB |
| MediaSessionRecords | 50–200 | <1 MB |

WindowSnapshots dominates. Run `POST /api/db/cleanup` periodically, or reduce the snapshot frequency via `PUT /api/settings`.
