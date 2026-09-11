namespace WinActivityTracker.Core.Data;

/// <summary>
/// 应用统一的 SQLite 连接串。Default Timeout 同时设置 busy_timeout 与命令超时:
/// 多写连接并发冲突时等待而非立即 SQLITE_BUSY。不使用 Cache=Shared——共享缓存与
/// WAL 互斥,会静默禁用其一。
/// </summary>
public static class SqliteConnectionStrings
{
    public const int DefaultTimeoutSeconds = 30;

    public static string Build(string dbPath) =>
        $"Data Source={dbPath};Mode=ReadWriteCreate;Default Timeout={DefaultTimeoutSeconds}";
}
