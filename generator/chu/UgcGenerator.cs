using System.Text;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * UGC 格式生成器。
 * 输入 IChuChart，内部自动转换后输出 UGC 文本。
 */
public class UgcGenerator : IGenerator<IChuChart>
{
    private const int UgcTicksPerBeat = 480;
    private const int C2sResolution = 384;

    public (string, List<Alert>) Generate(IChuChart chart)
    {
        var alerts = new List<Alert>();
        var ugc = ConvertToUgc(chart, alerts);
        var text = Serialize(ugc);
        return (text, alerts);
    }

    private static UgcChart ConvertToUgc(IChuChart chart, List<Alert> alerts)
    {
        if (chart is UgcChart ugc) return ugc;

        if (chart is C2sChart c2s)
        {
            var result = new UgcChart
            {
                TicksPerBeat = UgcTicksPerBeat,
                Designer = c2s.Creator,
                Difficulty = MapDiffId(c2s.DifficultId),
                SongId = c2s.MusicId.ToString(),
            };
            foreach (var b in c2s.BpmEvents)
                result.BpmEvents.Add((b.Measure, ScaleUp(b.Offset), b.Bpm));
            foreach (var m in c2s.MetEvents)
                result.BeatEvents.Add((m.Measure, m.Num, m.Denom));
            foreach (var n in c2s.Notes)
                result.Notes.Add(ScaleUpNote(n));
            return result;
        }

        alerts.Add(new Alert(Warning, string.Format(Locale.ChuGeneratorUnsupported, "→ UGC")));
        return new UgcChart();
    }

    private static ChuNote ScaleUpNote(ChuNote n)
    {
        int s(int v) => (int)((long)v * UgcTicksPerBeat / (C2sResolution / 4));
        return new ChuNote
        {
            Type = n.Type, Measure = n.Measure, Offset = s(n.Offset),
            Cell = n.Cell, Width = n.Width,
            HoldDuration = s(n.HoldDuration), SlideDuration = s(n.SlideDuration),
            EndCell = n.EndCell, EndWidth = n.EndWidth,
            Extra = n.Extra, TargetNote = IsAir(n.Type) && string.IsNullOrEmpty(n.TargetNote) ? "N" : n.TargetNote,
            AirHoldDuration = s(n.AirHoldDuration),
            StartHeight = n.StartHeight, TargetHeight = n.TargetHeight, NoteColor = n.NoteColor,
        };
    }

    private static int ScaleUp(int v) => (int)((long)v * UgcTicksPerBeat / (C2sResolution / 4));

    private static bool IsAir(string t) => t is "AIR" or "AUR" or "AUL" or "AHD" or "ADW" or "ADR" or "ADL";

    private static string MapDiffId(int id) => id switch
    {
        0 => "BASIC", 1 => "ADVANCED", 2 => "EXPERT", 3 => "MASTER", 4 => "ULTIMA", _ => "0"
    };

    private static string Serialize(UgcChart ugc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@VER\t6");
        if (!string.IsNullOrEmpty(ugc.Title)) sb.AppendLine($"@TITLE\t{ugc.Title}");
        if (!string.IsNullOrEmpty(ugc.Artist)) sb.AppendLine($"@ARTIST\t{ugc.Artist}");
        if (!string.IsNullOrEmpty(ugc.Designer)) sb.AppendLine($"@DESIGN\t{ugc.Designer}");
        sb.AppendLine($"@DIFF\t{DiffId(ugc.Difficulty)}");
        sb.AppendLine($"@LEVEL\t{ugc.Level}");
        sb.AppendLine($"@CONST\t{ugc.Constant:F5}");
        sb.AppendLine($"@SONGID\t{ugc.SongId}");
        sb.AppendLine($"@TICKS\t{ugc.TicksPerBeat}");
        foreach (var b in ugc.BeatEvents) sb.AppendLine($"@BEAT\t{b.Measure}\t{b.Num}\t{b.Den}");
        foreach (var b in ugc.BpmEvents) sb.AppendLine($"@BPM\t{b.Measure}'{b.Offset}\t{b.Bpm:F5}");
        sb.AppendLine("@TIL\t0\t0'0\t1.00000");
        sb.AppendLine("@MAINTIL\t0");
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        var notes = ugc.Notes.OrderBy(n => n.Measure).ThenBy(n => n.Offset).ToList();
        foreach (var n in notes)
        {
            sb.Append($"#{n.Measure}'{n.Offset}:{UCode(n)}");
            sb.AppendLine();
            if (n.Type == "HLD" && n.HoldDuration > 0)
                sb.AppendLine($"#{n.HoldDuration}>s");
            else if (n.Type == "SLD" && n.SlideDuration > 0)
                sb.AppendLine($"#{n.SlideDuration}>s{Hx(n.EndCell)}{Hw(n.EndWidth)}");
        }
        return sb.ToString();
    }

    private static string UCode(ChuNote n)
    {
        string c = Hx(n.Cell), w = Hw(n.Width);
        return n.Type switch
        {
            "TAP" => $"t{c}{w}",
            "CHR" => $"x{c}{w}{n.Extra}",
            "HLD" => $"h{c}{w}",
            "SLD" => $"s{c}{w}",
            "FLK" => $"f{c}{w}A",
            "MNE" => $"d{c}{w}",
            "AIR" => $"a{c}{w}UC{n.TargetNote}",
            "AUR" => $"a{c}{w}UR{n.TargetNote}",
            "AUL" => $"a{c}{w}UL{n.TargetNote}",
            "AHD" => $"a{c}{w}HD{n.TargetNote}_{n.AirHoldDuration}",
            "ADW" => $"a{c}{w}DC{n.TargetNote}",
            "ADR" => $"a{c}{w}DR{n.TargetNote}",
            "ADL" => $"a{c}{w}DL{n.TargetNote}",
            _ => $"t{c}{w}"
        };
    }

    private static string Hx(int v) => "0123456789ABCDEF"[Math.Clamp(v, 0, 15)].ToString();
    private static string Hw(int v) => "123456789ABCDEFG"[Math.Clamp(v - 1, 0, 15)].ToString();
    private static int DiffId(string d) => d switch { "BASIC" => 0, "ADVANCED" => 1, "EXPERT" => 2, "MASTER" => 3, "ULTIMA" => 4, _ => 0 };
}
