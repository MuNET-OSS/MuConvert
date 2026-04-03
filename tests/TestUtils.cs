namespace MuConvert.Tests;

internal static class TestUtils
{
    /// <summary>
    /// 从测试运行目录向上查找包含 MuConvert.csproj 的仓库根目录。
    /// </summary>
    public static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MuConvert.csproj")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root (MuConvert.csproj).");
    }
}
