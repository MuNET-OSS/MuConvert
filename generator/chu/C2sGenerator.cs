using System.Globalization;
using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * C2S 格式生成器。
 * 输入 IChuChart，内部自动转换后输出 C2S 文本。
 */
public class C2sGenerator : IGenerator<IChuChart>
{
    private const int C2sResolution = 384;

    public (string, List<Alert>) Generate(IChuChart chart)
    {
        var alerts = new List<Alert>();
        var c2s = ConvertToC2s(chart, alerts);
        var text = Serialize(c2s);
        return (text, alerts);
    }

    private static C2sChart ConvertToC2s(IChuChart chart, List<Alert> alerts)
    {
        if (chart is C2sChart c2s) return c2s;

        if (chart is UgcChart ugc)
        {
            var result = new C2sChart
            {
                Designer = ugc.Designer,
            };
            foreach (var b in ugc.BpmEvents)
                result.BpmEvents.Add((b.Measure, ScaleDown(b.Offset, ugc.TicksPerBeat), b.Bpm));
            foreach (var b in ugc.BeatEvents)
                result.MetEvents.Add((b.Measure, 0, b.Den, b.Num));
            result.Notes = ugc.Notes;
            return result;
        }

        if (chart is SusChart sus)
        {
            var result = new C2sChart();
            // result.BpmEvents.Add((0, 0, sus.Bpm));
            result.Notes = sus.Notes;
            return result;
        }

        alerts.Add(new Alert(Error, string.Format(Locale.ChuGeneratorUnsupported, "→ C2S")));
        throw new ConversionException(alerts);
    }

    private static int ScaleDown(int ticks, int tpb) => (int)((long)ticks * (C2sResolution / 4) / tpb);

    private static string Serialize(C2sChart chart)
    {
        chart.Sort();
        
        int.TryParse(chart.MusicId, out var musicId);
        var sb = new StringBuilder();
        sb.AppendLine($"VERSION\t1.08.00\t1.08.00");
        sb.AppendLine($"MUSIC\t{musicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{chart.Difficulty:D2}");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine($"CREATOR\t{chart.Designer}");
        var bpm_def = chart.BpmList.BPM_DEF();
        sb.AppendLine($"BPM_DEF\t{bpm_def.Item1}\t{bpm_def.Item2}\t{bpm_def.Item3}\t{bpm_def.Item4}");
        sb.AppendLine("MET_DEF\t4\t4");
        sb.AppendLine($"RESOLUTION\t{chart.Resolution}");
        sb.AppendLine($"CLK_DEF\t{chart.Resolution}");
        sb.AppendLine("PROGJUDGE_BPM\t240.000");
        sb.AppendLine("PROGJUDGE_AER\t0.999");
        sb.AppendLine("TUTORIAL\t0");
        sb.AppendLine();

        foreach (var b in chart.BpmEvents)
            sb.AppendLine($"BPM\t{b.Measure}\t{b.Offset}\t{Fmt(b.Bpm)}");
        foreach (var m in chart.MetEvents)
            sb.AppendLine($"MET\t{m.Measure}\t{m.Offset}\t{m.Denom}\t{m.Num}");
        foreach (var s in chart.SflEvents)
            sb.AppendLine($"SFL\t{s.Measure}\t{s.Offset}\t{s.Duration}\t{Mlt(s.Multiplier)}");
        sb.AppendLine();

        foreach (var n in chart.Notes)
            sb.AppendLine(FormatNote(n, chart.Resolution));

        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatNote(ChuNote n, int tpm)
    {
        var (m, o) = Utils.BarAndTick(n.Time, tpm, 0);
        var durTicks = Utils.Tick(n.Duration, tpm, 0);
        return n.Type switch
        {
            "TAP" => $"TAP\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            "CHR" => $"CHR\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.Tag}",
            "HLD" or "HXD" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}",
            "SLD" or "SLC" or "SXD" or "SXC" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}",
            "FLK" => $"FLK\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.Tag}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" =>
                string.IsNullOrEmpty(n.Tag)
                    ? $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}"
                    : $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{n.Tag}",
            "AHD" or "AHX" =>
                string.IsNullOrEmpty(n.Tag)
                    ? $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{durTicks}"
                    : $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{durTicks}\t{n.Tag}",
            "ASD" or "ASC" => FormatAsdAsc(n, m, o, durTicks),
            "ALD" => FormatAld(n, m, o),
            "MNE" => $"MNE\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            _ => $"TAP\t{m}\t{o}\t{n.Cell}\t{n.Width}"
        };
    }

    private static string FormatAsdAsc(ChuNote n, int m, int o, int durTicks)
    {
        var e0 = n.ExtraData.Count > 0 ? n.ExtraData[0] : 0;
        var e1 = n.ExtraData.Count > 1 ? n.ExtraData[1] : 0;
        return $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{e0}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{e1}\t{n.Tag}";
    }

    private static string FormatAld(ChuNote n, int m, int o)
    {
        var a = n.ExtraData.Count > 0 ? n.ExtraData[0] : 0;
        var b = n.ExtraData.Count > 1 ? n.ExtraData[1] : 0;
        var c = n.ExtraData.Count > 2 ? n.ExtraData[2] : 0;
        var tail = n.ExtraData.Count > 3 ? n.ExtraData[3] : 0;
        return $"ALD\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{a}\t{b}\t{c}\t{n.EndCell}\t{n.EndWidth}\t{tail}";
    }

    private static string Fmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string Mlt(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
