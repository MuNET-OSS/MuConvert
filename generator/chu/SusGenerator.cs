using System.Text;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * SUS 格式生成器。
 * 输入 IChuChart，内部自动转换后输出 SUS 文本。
 */
public class SusGenerator : IGenerator<IChuChart>
{
    private const int SusTpb = 480;
    private const int C2sRsl = 384;

    public (string, List<Alert>) Generate(IChuChart chart)
    {
        var alerts = new List<Alert>();
        var sus = ConvertToSus(chart, alerts);
        var text = Serialize(sus);
        return (text, alerts);
    }

    private static SusChart ConvertToSus(IChuChart chart, List<Alert> alerts)
    {
        if (chart is SusChart sus) return sus;

        double bpm = 120.0;
        string title = "", artist = "";

        if (chart is C2sChart c2s)
        {
            bpm = c2s.BpmEvents.Count > 0 ? c2s.BpmEvents[0].Bpm : c2s.DefBpm;
            var result = new SusChart { Bpm = bpm, TicksPerBeat = SusTpb, Title = title, Artist = artist };
            foreach (var n in c2s.Notes) result.Notes.Add(ScaleUp(n));
            return result;
        }

        if (chart is UgcChart ugc)
        {
            bpm = ugc.BpmEvents.Count > 0 ? ugc.BpmEvents[0].Bpm : 120.0;
            var result = new SusChart { Bpm = bpm, TicksPerBeat = SusTpb, Title = ugc.Title, Artist = ugc.Artist };
            foreach (var n in ugc.Notes) result.Notes.Add(MapLaneOnly(n));
            return result;
        }

        alerts.Add(new Alert(Warning, string.Format(Locale.ChuGeneratorUnsupported, "→ SUS")));
        return new SusChart();
    }

    private static ChuNote ScaleUp(ChuNote n)
    {
        int s(int v) => (int)((long)v * SusTpb / (C2sRsl / 4));
        return new ChuNote
        {
            Type = n.Type, Measure = n.Measure, Offset = s(n.Offset),
            Cell = n.Cell * 2, Width = n.Width * 2,
            HoldDuration = s(n.HoldDuration), SlideDuration = s(n.SlideDuration),
            EndCell = n.EndCell * 2, EndWidth = n.EndWidth * 2,
            Extra = n.Extra, TargetNote = n.TargetNote, AirHoldDuration = s(n.AirHoldDuration),
        };
    }

    private static ChuNote MapLaneOnly(ChuNote n) => new()
    {
        Type = n.Type, Measure = n.Measure, Offset = n.Offset,
        Cell = n.Cell * 2, Width = n.Width * 2,
        HoldDuration = n.HoldDuration, SlideDuration = n.SlideDuration,
        EndCell = n.EndCell * 2, EndWidth = n.EndWidth * 2,
        Extra = n.Extra, TargetNote = n.TargetNote, AirHoldDuration = n.AirHoldDuration,
    };

    private static string Serialize(SusChart sus)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(sus.Title)) sb.AppendLine($"#TITLE \"{sus.Title}\"");
        if (!string.IsNullOrEmpty(sus.Artist)) sb.AppendLine($"#ARTIST \"{sus.Artist}\"");
        if (!string.IsNullOrEmpty(sus.Designer)) sb.AppendLine($"#DESIGNER \"{sus.Designer}\"");
        sb.AppendLine($"#BPM_DEF {sus.Bpm:F2}");
        sb.AppendLine($"#REQUEST \"{sus.TicksPerBeat}\"");
        sb.AppendLine();

        foreach (var n in sus.Notes.OrderBy(n => n.Measure).ThenBy(n => n.Offset))
            sb.AppendLine($"#{n.Measure:X2}{n.Offset:X3}:{FormatData(n)}");

        return sb.ToString();
    }

    private static string FormatData(ChuNote n)
    {
        string lw = $"{n.Cell:X2}{n.Width:X2}";
        string tc = TypeCode(n.Type);
        string dur = $"{(n.HoldDuration > 0 ? n.HoldDuration : n.SlideDuration > 0 ? n.SlideDuration : n.AirHoldDuration):X4}";
        return tc switch
        {
            "01" or "02" or "03" or "10" => $"{tc}{lw}",
            "05" or "08" => $"{tc}{lw}{dur}",
            "06" => $"{tc}{lw}{dur}{n.EndCell:X2}{n.EndWidth:X2}",
            "07" or "09" => $"{tc}{lw}{n.TargetNote}",
            _ => $"01{lw}"
        };
    }

    private static string TypeCode(string t) => t switch
    {
        "TAP" => "01", "CHR" => "02", "FLK" => "03",
        "HLD" => "05", "SLD" => "06", "SLC" => "06",
        "AIR" => "07", "AUR" => "07", "AUL" => "07",
        "AHD" => "08", "ADW" => "09", "ADR" => "09", "ADL" => "09",
        "MNE" => "10", _ => "01"
    };
}
