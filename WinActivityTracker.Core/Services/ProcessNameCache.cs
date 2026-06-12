using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using WinActivityTracker.Core.Interop;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// PID→Info cache. Startup: RefreshAll() snapshots all processes via
/// CreateToolhelp32Snapshot. Runtime: WMI Win32_ProcessStartTrace / StopTrace
/// events keep the cache current. Falls back to polling if WMI is unavailable.
/// </summary>
public class ProcessNameCache : IDisposable
{
    public record Entry(string Name, int ParentPid, string? ParentName);

    private readonly ILogger<ProcessNameCache> _logger;
    private volatile ConcurrentDictionary<int, Entry> _cache = new();
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;

    public ProcessNameCache(ILogger<ProcessNameCache> logger)
    {
        _logger = logger;
    }

    /// <summary>True when WMI events are keeping the cache current.</summary>
    public bool IsWmiActive { get; private set; }

    public string? GetName(int pid) => GetEntry(pid)?.Name;

    public Entry? GetEntry(int pid)
    {
        if (_cache.TryGetValue(pid, out var entry))
            return entry;

        try
        {
            using var proc = Process.GetProcessById(pid);
            var name = NormalizeProcessName(proc.ProcessName);
            var e = new Entry(name, ParentPid: 0, ParentName: null);
            _cache[pid] = e;
            return e;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Full enumeration via CreateToolhelp32Snapshot. Called once on startup,
    /// then periodically by ProcessTracker if WMI is unavailable.
    /// </summary>
    public void RefreshAll()
    {
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(
            NativeMethods.TH32CS_SNAPPROCESS | NativeMethods.TH32CS_SNAPNOHEAPS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return;

        var raw = new Dictionary<int, (string Name, int ParentPid)>();
        try
        {
            var entry = new NativeMethods.PROCESSENTRY32();
            entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(entry);

            if (NativeMethods.Process32First(snapshot, ref entry))
            {
                do
                {
                    raw[(int)entry.th32ProcessID] =
                        (NormalizeProcessName(entry.szExeFile), (int)entry.th32ParentProcessID);
                }
                while (NativeMethods.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        var newCache = new ConcurrentDictionary<int, Entry>();
        foreach (var (pid, (name, ppid)) in raw)
        {
            string? parentName = null;
            if (ppid != 0 && raw.TryGetValue(ppid, out var parent))
                parentName = parent.Name;

            newCache[pid] = new Entry(name, ppid, parentName);
        }

        _cache = newCache;
    }

    // ===== WMI event subscription =====

    /// <summary>
    /// Subscribes to Win32_ProcessStartTrace / StopTrace. Returns true on success.
    /// On failure (e.g. test environments, restricted accounts), silently returns
    /// false — ProcessTracker will fall back to periodic RefreshAll().
    /// </summary>
    public bool StartWmiWatch()
    {
        if (IsWmiActive) return true;

        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += OnProcessStarted;
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += OnProcessStopped;
            _stopWatcher.Start();

            IsWmiActive = true;
            return true;
        }
        catch (Exception ex)
        {
            IsWmiActive = false;
            _startWatcher?.Dispose();
            _startWatcher = null;
            _stopWatcher?.Dispose();
            _stopWatcher = null;
            _logger.LogWarning(ex, "WMI process watch unavailable, using polling fallback");
            return false;
        }
    }

    public void StopWmiWatch()
    {
        IsWmiActive = false;
        if (_startWatcher != null)
        {
            try { _startWatcher.Stop(); } catch { }
            _startWatcher.EventArrived -= OnProcessStarted;
            _startWatcher.Dispose();
            _startWatcher = null;
        }
        if (_stopWatcher != null)
        {
            try { _stopWatcher.Stop(); } catch { }
            _stopWatcher.EventArrived -= OnProcessStopped;
            _stopWatcher.Dispose();
            _stopWatcher = null;
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var name = NormalizeProcessName((string)e.NewEvent.Properties["ProcessName"].Value);
            var ppid = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"].Value);

            string? parentName = null;
            if (ppid != 0 && _cache.TryGetValue(ppid, out var parent))
                parentName = parent.Name;

            _cache[pid] = new Entry(name, ppid, parentName);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "WMI ProcessStartTrace event parse failed"); }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            _cache.TryRemove(pid, out _);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "WMI ProcessStopTrace event parse failed"); }
    }

    public ConcurrentDictionary<int, Entry> Snapshot => _cache;

    public void Dispose()
    {
        StopWmiWatch();
    }

    private static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return name;
        return Path.GetExtension(name).Length == 0 ? name + ".exe" : name;
    }
}
