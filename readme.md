# WinActivityTracker

## An activity tracker, simple and work.

Based on .NET and Vue.

Database and settings are stored in `%LOCALAPPDATA%\WinActivityTracker` by default. Paths
can be customized via Web UI, registry (`HKCU\Software\WinActivityTracker`), or the
`WTA_CONFIG_DIR` environment variable.

Storage usage: ~1 MB/Day. Tracks focus changes, window sessions, process sessions, and media playback.

