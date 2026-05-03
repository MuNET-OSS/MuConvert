using System.Text;
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
    private static int RSL = 480 * 4;

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
                Designer = c2s.Designer,
                Difficulty = c2s.Difficulty,
                MusicId = c2s.MusicId,
            };
            result.BpmList.AddRange(c2s.BpmList);
            result.MetList.AddRange(c2s.MetList);
            result.SflList.AddRange(c2s.SflList);
            result.Notes = c2s.Notes;
            return result;
        }

        alerts.Add(new Alert(Error, string.Format(Locale.ChuGeneratorUnsupported, "→ UGC")));
        throw new ConversionException(alerts);
    }

    private static string Serialize(UgcChart ugc)
    {
        ugc.Sort();
        
        var sb = new StringBuilder();
        sb.AppendLine("@VER\t6");
        if (!string.IsNullOrEmpty(ugc.Title)) sb.AppendLine($"@TITLE\t{ugc.Title}");
        if (!string.IsNullOrEmpty(ugc.Artist)) sb.AppendLine($"@ARTIST\t{ugc.Artist}");
        if (!string.IsNullOrEmpty(ugc.Designer)) sb.AppendLine($"@DESIGN\t{ugc.Designer}");
        sb.AppendLine($"@DIFF\t{ugc.Difficulty}");
        sb.AppendLine($"@LEVEL\t{ugc.DisplayLevel}");
        sb.AppendLine($"@CONST\t{ugc.Level:F5}");
        sb.AppendLine($"@SONGID\t{ugc.MusicId}");
        sb.AppendLine($"@TICKS\t{RSL / 4}");
        foreach (var met in ugc.MetList)
        {
            var (m, _) = Utils.BarAndTick(met.Time, RSL);
            sb.AppendLine($"@BEAT\t{m}\t{met.Numerator}\t{met.Denominator}");
        }
        foreach (var b in ugc.BpmList)
        {
            var (m, o) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine($"@BPM\t{m}'{o}\t{b.Bpm:F5}");
        }
        sb.AppendLine("@TIL\t0\t0'0\t1.00000");

        foreach (var s in ugc.SflList.OrderBy(x => x.Time)) 
        { 
            var (m, o) = Utils.BarAndTick(s.Time, RSL); 
            sb.AppendLine($"@SPDMOD\t{m}'{o}\t{s.Multiplier:0.00000}");
        }

        sb.AppendLine("@MAINTIL\t0");
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        foreach (var n in ugc.Notes)
        {
            var (m, o) = Utils.BarAndTick(n.Time, RSL);
            sb.Append($"#{m}'{o}:{UCode(n, RSL)}");
            sb.AppendLine();
            var durTicks = Utils.Tick(n.Duration, RSL);
            if (n.Type == "HLD" && durTicks > 0)
                sb.AppendLine($"#{durTicks}>s");
            else if (n.Type == "SLD" && durTicks > 0)
                sb.AppendLine($"#{durTicks}>s{Hx(n.EndCell)}{Hw(n.EndWidth)}");
        }
        return sb.ToString();
    }

    private static string UCode(ChuNote n, int tpm)
    {
        string c = Hx(n.Cell), w = Hw(n.Width);
        var durTicks = Utils.Tick(n.Duration, tpm);
        var targetNote = string.IsNullOrEmpty(n.TargetNote) ? "N" : n.TargetNote;
        return n.Type switch
        {
            "TAP" => $"t{c}{w}",
            "CHR" => $"x{c}{w}{n.Tag}",
            "HLD" or "HXD" => $"h{c}{w}",
            "SLD" or "SXD" => $"s{c}{w}",
            "SLC" or "SXC" => $"s{c}{w}",
            "FLK" => $"f{c}{w}A",
            "MNE" => $"d{c}{w}",
            "AIR" => $"a{c}{w}UC{targetNote}",
            "AUR" => $"a{c}{w}UR{targetNote}",
            "AUL" => $"a{c}{w}UL{targetNote}",
            "AHD" or "AHX" => $"a{c}{w}HD{targetNote}_{durTicks}",
            "ADW" => $"a{c}{w}DC{targetNote}",
            "ADR" => $"a{c}{w}DR{targetNote}",
            "ADL" => $"a{c}{w}DL{targetNote}",
            _ => $"t{c}{w}"
        };
    }

    private static string Hx(int v) => "0123456789ABCDEF"[Math.Clamp(v, 0, 15)].ToString();
    private static string Hw(int v) => "123456789ABCDEFG"[Math.Clamp(v - 1, 0, 15)].ToString();
}
