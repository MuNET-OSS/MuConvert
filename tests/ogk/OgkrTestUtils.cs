namespace MuConvert.Tests.ogk;

internal static class OgkrTestUtils
{
    // 查找到测试数据的根目录(tests/ogk/testset)
    public static DirectoryInfo FindTestsetRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "MuConvert.Tests.csproj")))
            dir = Path.GetDirectoryName(dir);
        return new DirectoryInfo(Path.Combine(dir ?? throw new DirectoryNotFoundException("Could not locate repo root."), "ogk", "testset"));
    }

    public static IEnumerable<object[]> GetTestInputs(string dataDir)
    {
        var testsetRoot = Path.Combine(FindTestsetRoot().FullName, dataDir);
        if (!Directory.Exists(testsetRoot))
            throw new DirectoryNotFoundException($"Testset root not found: {testsetRoot}");

        foreach (var ogkrPath in Directory.EnumerateFiles(testsetRoot, "*.ogkr", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return [new OgkrTestInput(ogkrPath)];
        }
    }
}

public record OgkrTestInput(string OgkrPath)
{
    public string Dir => Path.GetDirectoryName(OgkrPath)!;
    public string SongName => Path.GetFileName(Dir);
    public string DifficultyId => Path.GetFileNameWithoutExtension(OgkrPath).Split('_').LastOrDefault() ?? "";

    public override string ToString() => $"{SongName}-{DifficultyId}";
}
