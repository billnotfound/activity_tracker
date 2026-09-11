namespace WinActivityTracker.Core.Services;

/// <summary>
/// 进程名归一化匹配：两侧都补全 .exe 后缀后不区分大小写比较。
/// settings.json 示例排除项是不带扩展名的（"explorer"），而 ProcessNameCache
/// 中的名字几乎都带 .exe——直接 Contains 比较会让文档示例静默失效。
/// </summary>
public static class ProcessNameMatcher
{
    public static bool IsExcluded(IEnumerable<string> excluded, string processName)
    {
        var norm = Normalize(processName);
        foreach (var entry in excluded)
        {
            if (string.Equals(Normalize(entry), norm, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Normalize(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return name;
        return Path.GetExtension(name).Length == 0 ? name + ".exe" : name;
    }
}
