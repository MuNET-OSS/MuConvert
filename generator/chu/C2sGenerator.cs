using System.Globalization;
using System.Text;
using MuConvert.chart;
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
                Version = "1.08.00\t1.08.00",
                Creator = ugc.Designer,
                DefBpm = ugc.BpmEvents.Count > 0 ? ugc.BpmEvents[0].Bpm : 120.0,
            };
            foreach (var b in ugc.BpmEvents)
                result.BpmEvents.Add((b.Measure, ScaleDown(b.Offset, ugc.TicksPerBeat), b.Bpm));
            foreach (var b in ugc.BeatEvents)
                result.MetEvents.Add((b.Measure, 0, b.Den, b.Num));
            foreach (var n in ugc.Notes)
                result.Notes.Add(ScaleNote(n, ugc.TicksPerBeat));
            return result;
        }

        if (chart is SusChart sus)
        {
            var result = new C2sChart { DefBpm = sus.Bpm };
            result.BpmEvents.Add((0, 0, sus.Bpm));
            foreach (var n in sus.Notes)
                result.Notes.Add(ScaleNote(n, sus.TicksPerBeat));
            return result;
        }

        alerts.Add(new Alert(Warning, string.Format(Locale.ChuGeneratorUnsupported, "→ C2S")));
        return new C2sChart();
    }

    private static ChuNote ScaleNote(ChuNote n, int tpb)
    {
        int scaleDown(int v) => (int)((long)v * (C2sResolution / 4) / tpb);
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

    private static int ScaleDown(int ticks, int tpb) => (int)((long)ticks * (C2sResolution / 4) / tpb);

    private static string Serialize(C2sChart chart)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"VERSION\t{chart.Version}");
        sb.AppendLine($"MUSIC\t{chart.MusicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{chart.DifficultId:D2}");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine($"CREATOR\t{chart.Creator}");
        sb.AppendLine($"BPM_DEF\t{Fmt(chart.DefBpm)}\t{Fmt(chart.DefBpm)}\t{Fmt(chart.DefBpm)}\t{Fmt(chart.DefBpm)}");
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

        foreach (var n in chart.Notes.OrderBy(n => n.Measure * C2sResolution + n.Offset))
            sb.AppendLine(FormatNote(n));

        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatNote(ChuNote n) => n.Type switch
    {
        "TAP" => $"TAP\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}",
        "CHR" => $"CHR\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.Extra}",
        "HLD" or "HXD" => $"{n.Type}\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.HoldDuration}",
        "SLD" or "SLC" or "SXD" or "SXC" => $"{n.Type}\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.SlideDuration}\t{n.EndCell}\t{n.EndWidth}",
        "FLK" => $"FLK\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.Extra}",
        "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" => $"{n.Type}\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.TargetNote}",
        "AHD" => $"AHD\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{n.AirHoldDuration}",
        "MNE" => $"MNE\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}",
        _ => $"TAP\t{n.Measure}\t{n.Offset}\t{n.Cell}\t{n.Width}"
    };

    private static string Fmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string Mlt(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
