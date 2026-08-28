using System.Reflection;
using MuConvert.chart;
using MuConvert.chu;
using Rationals;

namespace MuConvert.Tests.chu;

public class UgcTimeTests
{
    private const int C2sRsl = 384;

    private static string TerminalDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "chu", "testset", "自制谱", "Terminal");

    /// <summary>从 terminal_03.c2s 读取 MET 行，构建 MetList（与 C2sParser 一致）。</summary>
    private static List<MET> LoadTerminalMetList()
    {
        var path = Path.Combine(TerminalDir, "terminal_03.c2s");
        var list = new List<MET>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("MET\t", StringComparison.Ordinal)) continue;
            var p = line.Split('\t');
            var time = int.Parse(p[1]) + new Rational(int.Parse(p[2]), C2sRsl);
            // C2S MET 行里写的小节号，比 UGC @BEAT 大 1（PenguinTools / Terminal 惯例）。
            // FillUgcBeats 期望的是 chart 内部时间（与 UgcParser 写入 MetList 的方式一致）。
            if (time > 0) time -= 1;
            list.Add(new MET(time, int.Parse(p[4]), int.Parse(p[3])));
        }
        return list;
    }

    /// <summary>从 terminal.ugc 读取 @BEAT 行，构建 UgcParser 所用的 _ugcBeats。</summary>
    private static List<(int Bar, Rational Beat)> LoadTerminalUgcBeats()
    {
        var path = Path.Combine(TerminalDir, "terminal.ugc");
        var list = new List<(int, Rational)>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("@BEAT\t", StringComparison.Ordinal)) continue;
            var p = line.Split('\t');
            list.Add((int.Parse(p[1]), new Rational(int.Parse(p[2]), int.Parse(p[3]))));
        }
        return list;
    }

    private static T InvokeInstance<T>(object target, string name, params object?[] args)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var method = target.GetType().GetMethods(flags)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length);
        return (T)method.Invoke(target, args)!;
    }

    private static void SetInstanceField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private static T GetInstanceField<T>(object target, string name)
        => (T)GetInstanceField(target, name)!;

    private static object? GetInstanceField(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    /// <summary>从 terminal.ugc 的 @BEAT 构建 UgcGenerator 所用的 _ugcBeats。</summary>
    private static List<(int Bar, int Num, int Den)> LoadTerminalGeneratorUgcBeats()
    {
        return LoadTerminalUgcBeats()
            .Select(b => (b.Bar, (int)b.Beat.Numerator, (int)b.Beat.Denominator))
            .ToList();
    }

    private static void AssertBeatEntriesEqual(
        IReadOnlyList<(int Bar, Rational Beat)> expected,
        IReadOnlyList<(int Bar, int Num, int Den)> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Bar, actual[i].Bar);
            Assert.Equal(expected[i].Beat, new Rational(actual[i].Num, actual[i].Den));
        }
    }

    private static void FillUgcBeats(UgcGenerator gen, List<MET> metList)
        => InvokeInstance<object>(gen, "FillUgcBeats", metList);

    private static List<(int Bar, int Num, int Den)> GetGeneratorUgcBeats(UgcGenerator gen)
        => GetInstanceField<List<(int, int, int)>>(gen, "_ugcBeats");

    private static Rational ParserT(UgcParser parser, int ugcBar, int ugcTick)
        => InvokeInstance<Rational>(parser, "T", ugcBar, ugcTick);

    private static (int Bar, int Tick) GeneratorT(UgcGenerator gen, Rational time)
        => InvokeInstance<(int, int)>(gen, "T", time);

    [Fact]
    public void FillUgcBeats_MatchesTerminalUgcBeats()
    {
        var gen = new UgcGenerator();
        FillUgcBeats(gen, LoadTerminalMetList());
        AssertBeatEntriesEqual(LoadTerminalUgcBeats(), GetGeneratorUgcBeats(gen));
    }

    public static IEnumerable<object[]> ParserTCases =>
    [
        [0, 0, Rational.Zero],
        [8, 960, new Rational(17, 2)],          // 8'960
        [64, 0, new Rational(64)],              // 64'0，@BEAT 64 5/4 段起点
        [64, 1920, new Rational(65)],           // 64'1920
        [65, 0, new Rational(261, 4)],          // 65'0 = 64 + 5/4
        [72, 360, new Rational(1103, 16)],      // 72'360（Terminal 末段 2/4 小节）
    ];

    [Theory]
    [MemberData(nameof(ParserTCases))]
    public void UgcParser_T_ConvertsUgcPositionToChartTime(int ugcBar, int ugcTick, Rational expected)
    {
        var parser = new UgcParser();
        SetInstanceField(parser, "_ugcBeats", LoadTerminalUgcBeats());
        Assert.Equal(expected, ParserT(parser, ugcBar, ugcTick));
    }

    [Theory]
    [MemberData(nameof(ParserTCases))]
    public void UgcGenerator_T_ConvertsChartTimeToUgcPosition(int expectedBar, int expectedTick, Rational chartTime)
    {
        var gen = new UgcGenerator();
        SetInstanceField(gen, "_ugcBeats", LoadTerminalGeneratorUgcBeats());

        var (bar, tick) = GeneratorT(gen, chartTime);
        Assert.Equal(expectedBar, bar);
        Assert.Equal(expectedTick, tick);
    }
}
