using System.Collections.Concurrent;
using System.Diagnostics;
using WinActivityTracker.Core.Interop;

namespace WinActivityTracker.Core.Services;

/// <summary>
/// PID→Name cache. Uses CreateToolhelp32Snapshot for full enumeration (zero managed
/// Process objects) and Process.GetProcessById for individual lazy lookups.
/// </summary>
public class ProcessNameCache
{
    private readonly ConcurrentDictionary<int, string> _cache = new();

    /// <summary>
    /// Returns the process name for a PID (lazy lookup). Cached on first hit.
    /// </summary>
    public string? GetName(int pid)
    {
        if (_cache.TryGetValue(pid, out var name))
            return name;

        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
            _cache[pid] = name;
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Enumerates all running processes via CreateToolhelp32Snapshot.
    /// Zero managed allocations for process objects — just PID + name strings.
    /// </summary>
    public void RefreshAll()
    {
        _cache.Clear();
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(
            NativeMethods.TH32CS_SNAPPROCESS | NativeMethods.TH32CS_SNAPNOHEAPS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return;

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32();
            entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(entry);

            if (NativeMethods.Process32First(snapshot, ref entry))
            {
                do
                {
                    _cache[(int)entry.th32ProcessID] = entry.szExeFile;
                }
                while (NativeMethods.Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// Returns all cached PID→Name entries. Call RefreshAll() first to populate.
    /// ConcurrentDictionary's enumerator is a thread-safe snapshot — no allocation.
    /// </summary>
    public ConcurrentDictionary<int, string> Snapshot => _cache;
}
