using MuConvert.chu;
using MuConvert.parser;
using MuConvert.utils;

namespace MuConvert.Tests.chu;

public class ChuTests
{
    private static string TestsetDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "chu", "testset");
    private static string OfficialDir => Path.Combine(TestsetDir, "官谱", "B.B.K.K.B.K.K");
    private static string CustomDir => Path.Combine(TestsetDir, "自制谱", "Example");
    private static string C2sPath => Path.Combine(OfficialDir, "0003_00.c2s");
    private static string UgcPath => Path.Combine(CustomDir, "basic.ugc");

    [Fact]
    public void CanParseOfficialC2S()
    {
        if (!File.Exists(C2sPath)) throw new SkipException($"Missing: {C2sPath}");
        var (chart, _) = new C2sParser().Parse(File.ReadAllText(C2sPath));
        Assert.NotEmpty(chart.Notes);
        Assert.Equal(384, chart.Resolution);
    }

    [Fact]
    public void C2sRoundTrip()
    {
        if (!File.Exists(C2sPath)) throw new SkipException($"Missing: {C2sPath}");
        var (chart, _) = new C2sParser().Parse(File.ReadAllText(C2sPath));
        var (rt, _) = new C2sGenerator().Generate(chart);
        var (reparsed, _) = new C2sParser().Parse(rt);
        Assert.Equal(chart.Notes.Count, reparsed.Notes.Count);
    }

    [Fact]
    public void CanParseUgc()
    {
        if (!File.Exists(UgcPath)) throw new SkipException($"Missing: {UgcPath}");
        var (chart, _) = new UgcParser().Parse(File.ReadAllText(UgcPath));
        Assert.NotEmpty(chart.Notes);
        Assert.Equal("MASTER", chart.Difficulty);
    }

    [Fact]
    public void UgcToC2sViaGenerator()
    {
        if (!File.Exists(UgcPath)) throw new SkipException($"Missing: {UgcPath}");
        var (ugc, _) = new UgcParser().Parse(File.ReadAllText(UgcPath));
        var (c2sText, _) = new C2sGenerator().Generate(ugc);
        Assert.Contains("VERSION", c2sText);
        Assert.Contains("TAP\t", c2sText);
    }

    [Fact]
    public void C2sToUgcViaGenerator()
    {
        if (!File.Exists(C2sPath)) throw new SkipException($"Missing: {C2sPath}");
        var (c2s, _) = new C2sParser().Parse(File.ReadAllText(C2sPath));
        var (ugcText, _) = new UgcGenerator().Generate(c2s);
        Assert.Contains("@VER", ugcText);
        Assert.Contains("#5'0", ugcText);
    }
}
