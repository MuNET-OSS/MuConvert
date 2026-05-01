using System.Reflection;
using MuConvert.chu;

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

        var originalSnapshots = chart.Notes
            .Select(SnapshotNote)
            .OrderBy(s => s)
            .ToArray();

        var reparsedSnapshots = reparsed.Notes
            .Select(SnapshotNote)
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(originalSnapshots, reparsedSnapshots);
    }

    /// <summary>
    /// Builds a stable, comparable string from a note's public instance properties (name-sorted)
    /// so round-trip tests verify no field loss without hard-coding each property in the test.
    /// </summary>
    private static string SnapshotNote(ChuNote note)
    {
        var props = typeof(ChuNote).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var parts = props
            .OrderBy(p => p.Name)
            .Select(p => $"{p.Name}={p.GetValue(note)}");
        return string.Join("|", parts);
    }

    /// <summary>
    /// Same tick scaling as <see cref="C2sGenerator"/> when converting UGC → C2S (384 ticks per measure).
    /// </summary>
    private static ChuNote UgcNoteScaledToC2sTicks(ChuNote n, int ticksPerBeat)
    {
        const int c2sResolution = 384;
        int scaleDown(int v) => (int)((long)v * (c2sResolution / 4) / ticksPerBeat);
        return new ChuNote
        {
            Type = n.Type, Measure = n.Measure, Offset = scaleDown(n.Offset),
            Cell = n.Cell, Width = n.Width,
            HoldDuration = scaleDown(n.HoldDuration), SlideDuration = scaleDown(n.SlideDuration),
            EndCell = n.EndCell, EndWidth = n.EndWidth,
            Extra = n.Extra, TargetNote = n.TargetNote, AirHoldDuration = scaleDown(n.AirHoldDuration),
            StartHeight = n.StartHeight, TargetHeight = n.TargetHeight, NoteColor = n.NoteColor,
        };
    }

    /// <summary>
    /// Compares UGC-side notes to C2S-side notes in C2S tick space (384): snapshots of
    /// <see cref="UgcNoteScaledToC2sTicks"/> for each <paramref name="ugc"/> note vs snapshots of <paramref name="c2s"/> notes.
    /// Use with <c>UgcToC2sViaGenerator</c> (source UGC, C2S from generate+parse) or <c>C2sToUgcViaGenerator</c> (UGC from generate+parse, source C2S).
    /// </summary>
    private static void AssertUgcNotesEquivalentToReparsedC2s(UgcChart ugc, C2sChart c2s, bool isUgcReference)
    {
        var ugcSnaps = ugc.Notes
            .Select(n => SnapshotNote(UgcNoteScaledToC2sTicks(n, ugc.TicksPerBeat)))
            .OrderBy(s => s)
            .ToArray();
        var c2sSnaps = c2s.Notes.Select(SnapshotNote).OrderBy(s => s).ToArray();
        if (isUgcReference) Assert.Equal(ugcSnaps, c2sSnaps);
        else Assert.Equal(c2sSnaps, ugcSnaps);
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
        Assert.NotEmpty(ugc.Notes);

        var (c2sText, _) = new C2sGenerator().Generate(ugc);
        Assert.Contains("VERSION", c2sText);
        Assert.Contains("TAP\t", c2sText);

        // 再把转出来的c2s，parse回去，比较是否和一开始的ugc等价（注意不是文本 round-trip，而是 IR 等价，允许字段重排但不允许信息丢失）
        var (c2sChart, _) = new C2sParser().Parse(c2sText);
        Assert.NotEmpty(c2sChart.Notes);
        AssertUgcNotesEquivalentToReparsedC2s(ugc, c2sChart, true);
    }

    [Fact]
    public void C2sToUgcViaGenerator()
    {
        if (!File.Exists(C2sPath)) throw new SkipException($"Missing: {C2sPath}");
        var (c2s, _) = new C2sParser().Parse(File.ReadAllText(C2sPath));
        Assert.NotEmpty(c2s.Notes);
        
        var (ugcText, _) = new UgcGenerator().Generate(c2s);
        Assert.Contains("@VER", ugcText);
        Assert.Contains("#5'0", ugcText);

        // 再把转出来的ugc，parse回去，比较是否和一开始的c2s等价
        var (ugcReparsed, _) = new UgcParser().Parse(ugcText);
        Assert.NotEmpty(ugcReparsed.Notes);
        AssertUgcNotesEquivalentToReparsedC2s(ugcReparsed, c2s, false);
    }
}
