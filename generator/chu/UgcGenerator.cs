using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

public class UgcGenerator : IGenerator<ChuChart>
{
    private int RSL = 480 * 4;
    private List<Alert> alerts = [];

    public (string, List<Alert>) Generate(ChuChart chart)
    {
        alerts = new List<Alert>();
        var text = Serialize(chart);
        return (text, alerts);
    }

    /**
     * 对 chart.Notes 做一次稳定重排序，使得任何具有 Previous 的音符都会紧紧地出现在它的 Previous 之后，从而满足 UGC 对“Air/Air Slide应该紧跟着其依附的音符”的格式要求。
     */
    private List<ChuNote> SortedNotesForConnectingPrevious(ChuChart chart)
    {
        // 1. 基于Previous，反向构建Next信息
        var nextDict = new Dictionary<ChuNote, List<ChuNote>>();
        foreach (var n in chart.Notes)
        {
            if (n.Previous != null) nextDict.Add(n.Previous, n);
        }

        // 2. 遍历 chart.Notes，对每个 ChuNote 以 DFS 方式把它本身以及它所有 Next 子孙依次加入结果。
        var result = new List<ChuNote>(chart.Notes.Count);
        var visited = new HashSet<ChuNote>();
        foreach (var root in chart.Notes) Dfs(root);
        return result;

        void Dfs(ChuNote n)
        {
            if (!visited.Add(n)) return;
            result.Add(n);
            if (!nextDict.TryGetValue(n, out var nexts)) return;
            foreach (var next in nexts) Dfs(next);
        }
    }

    private string Serialize(ChuChart ugc)
    {
        ugc.Sort();
        
        var sb = new StringBuilder();
        sb.AppendLine($"' Created with MuConvert v{Utils.AppVersion}");
        sb.AppendLine("@VER\t8");
        sb.AppendLine("@EXVER\t1");
        if (!string.IsNullOrEmpty(ugc.Title)) sb.AppendLine($"@TITLE\t{ugc.Title}");
        if (!string.IsNullOrEmpty(ugc.Artist)) sb.AppendLine($"@ARTIST\t{ugc.Artist}");
        if (!string.IsNullOrEmpty(ugc.Designer)) sb.AppendLine($"@DESIGN\t{ugc.Designer}");
        sb.AppendLine($"@DIFF\t{ugc.Difficulty}");
        var displayLevelStr = !string.IsNullOrEmpty(ugc.DisplayLevel) ? ugc.DisplayLevel : "0";
        sb.AppendLine($"@LEVEL\t{displayLevelStr}");
        sb.AppendLine(FormattableString.Invariant($"@CONST\t{ugc.Level:F5}"));
        var songId = !(string.IsNullOrEmpty(ugc.MusicId) || ugc.MusicId == "0") ? ugc.MusicId : $"MuC-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        sb.AppendLine($"@SONGID\t{songId}");
        sb.AppendLine("@FLAG\tHIPRECISION\tTRUE"); // 表明，谱面中的高度使用的是两位高度而不是一位高度
        sb.AppendLine($"@TICKS\t{RSL / 4}");
        foreach (var met in ugc.MetList)
        {
            var (m, _) = Utils.BarAndTick(met.Time, RSL);
            sb.AppendLine($"@BEAT\t{m}\t{met.Numerator}\t{met.Denominator}");
        }
        foreach (var b in ugc.BpmList)
        {
            var (m, o) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine(FormattableString.Invariant($"@BPM\t{m}'{o}\t{b.Bpm:F5}"));
        }
        sb.AppendLine("@TIL\t0\t0'0\t1.00000");

        foreach (var s in ugc.SflList.OrderBy(x => x.Time)) 
        { 
            var (m, o) = Utils.BarAndTick(s.Time, RSL); 
            sb.AppendLine(FormattableString.Invariant($"@SPDMOD\t{m}'{o}\t{s.Multiplier:0.00000}"));
        }

        sb.AppendLine("@MAINTIL\t0");
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        var notes = SortedNotesForConnectingPrevious(ugc);

        // UGC Slide / AIR-SLIDE (v8):
        // - Chains (ChuNote.Previous) serialize as ONE parent line + follower lines (#OffsetTick from parent time).
        // - Ground slide: parent `s`, followers `>s` / `>c` + end cell/width.
        // - Air slide: parent `S` + cell/width + hh (base-36 ×2, C2S/UGC height units) + N/I; followers `>s`/`>c` + xw + hh.
        // - First segment may attach to TAP/HLD via Previous; only skip emit when Previous is another segment of the same chain.
        var slideChains = BuildSlideChains(notes);

        foreach (var n in notes)
        {
            if (IsSlideChainNote(n.Type) && IsSlideContinueSegments(n))
                continue; // 是Slide且不是第一段Slide，则应当已经被处理过了，直接跳过

            var (m, o) = Utils.BarAndTick(n.Time, RSL);
            var ucode = UCode(n);
            if (ucode == "")
            {
                alerts.Add(new Alert(Alert.LEVEL.Warning, $"UGC Generator遇到了不支持的音符类型: {n.Type}", (ugc, n.Time)));
                continue;
            }
            sb.Append($"#{m}'{o}:{ucode}");
            sb.AppendLine();

            if (IsSlideChainNote(n.Type))
            {
                if (slideChains.TryGetValue(n, out var segments))
                {
                    var isAir = IsAirSlide(n.Type);
                    foreach (var seg in segments)
                    {
                        var endTicks = Utils.Tick(seg.EndTime - n.Time, RSL);
                        if (endTicks <= 0) continue;
                        if (isAir)
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}{EncodeAirHeight(seg.EndHeight)}");
                        else
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}");
                    }
                }
                continue;
            }

            var durTicks = Utils.Tick(n.Duration, RSL);
            if (n.Type is "HLD" or "HXD" && durTicks > 0)
                sb.AppendLine($"#{durTicks}>s");
            else if (n.Type is "AHD" or "AHX" && durTicks > 0)
            {
                var marker = (n.Type == "AHX") ? 'c' : 's';
                sb.AppendLine($"#{durTicks}>{marker}");
            }
            else if (n.Type is "ALD" && durTicks > 0)
                sb.AppendLine($"#{durTicks}>c{IToH36(n.EndCell)}{IToH36(n.EndWidth)}{EncodeAirHeight(n.EndHeight)}");
        }
        return sb.ToString();
    }

    private static Dictionary<ChuNote, List<ChuNote>> BuildSlideChains(List<ChuNote> notes)
    {
        var chains = new Dictionary<ChuNote, List<ChuNote>>();
        foreach (var n in notes)
        {
            if (!IsSlideChainNote(n.Type)) continue;
            var head = GetSlideHead(n);
            if (!chains.TryGetValue(head, out var list))
                chains[head] = list = [];
            list.Add(n);
        }

        // Order segments by their end time so follower ticks are increasing.
        foreach (var (_, segs) in chains)
        {
            segs.Sort((a, b) =>
            {
                var t = a.EndTime.CompareTo(b.EndTime);
                if (t != 0) return t;
                // stable-ish tie-breakers
                t = a.Time.CompareTo(b.Time);
                if (t != 0) return t;
                t = string.CompareOrdinal(a.Type, b.Type);
                if (t != 0) return t;
                return 0;
            });
        }

        // For a valid chain, follower ticks should be strictly increasing; if the chart has
        // degenerate segments, later code simply skips non-positive offsets.
        return chains;
    }

    private static ChuNote GetSlideHead(ChuNote n)
    {
        var cur = n;
        while (IsSlideContinueSegments(cur)) cur = cur.Previous!;
        return cur;
    }
    
    private static bool IsSlideChainNote(string t) => IsSlide(t) || IsAirSlide(t);
    // 返回true表示，当前ChuNote对应的Slide Segment，是第二段之后（也就是接在别的segment之后）的segment，而不是首段segment，
    private static bool IsSlideContinueSegments(ChuNote n) // Air Slide的前驱只能是Air Slide，反之亦然。
        => (IsSlide(n) && IsSlide(n.Previous)) || (IsAirSlide(n) && IsAirSlide(n.Previous));
    private static char SlideFollowerMarker(string t) => t is "SLC" or "SXC" or "ASC" ? 'c' : 's';

    private static string EncodeAirHeight(decimal value) => IToH36(Math.Clamp((int)Math.Round(C2U_Height(value) * 10), 0, 1295)).PadLeft(2, '0');
    
    private string AirColor(ChuNote n)
    {
        if (Try_C2U_AirColor(n, out var color)) return color;
        else
        {
            if (n.Tag != "") alerts.Add(new Alert(Alert.LEVEL.Warning, string.Format(Locale.C2SUnsupportedAirColor, "UGC Generator", n.Type, n.Tag), n.Time));
            return "N";
        }
    }
    private string CrushColor(ChuNote n)
    {
        if (C2U_AirCrushColor.TryGetValue(n.Tag, out var color)) return color;
        else if (n.Tag.Length == 1) return n.Tag;
        else
        {
            if (n.Tag != "") alerts.Add(new Alert(Alert.LEVEL.Warning, string.Format(Locale.C2SUnsupportedAirColor, "UGC Generator", n.Type, n.Tag), n.Time));
            return "0";
        }
    }

    private string CrushInterval(Rational crushInterval)
    {
        return crushInterval > 25 ? "$" : Utils.Tick(crushInterval, RSL).ToString();
    }

    private string UCode(ChuNote n)
    {
        string c = IToH36(n.Cell), w = IToH36(n.Width);
        return n.Type switch
        {
            "TAP" => $"t{c}{w}",
            "CHR" => $"x{c}{w}{C2U_ChrExtras.GetValueOrDefault(n.Tag, "C")}",
            "HLD" or "HXD" => $"h{c}{w}",
            "SLD" or "SXD" => $"s{c}{w}",
            "SLC" or "SXC" => $"s{c}{w}",
            "FLK" => $"f{c}{w}{n.Tag}",
            "MNE" => $"d{c}{w}",
            // AIR-SLIDE (v8): #BarTick:S x w hh c
            "ASD" or "ASC" => $"S{c}{w}{EncodeAirHeight(n.Height)}{AirColor(n)}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" => $"a{c}{w}{C2U_AirDirections[n.Type]}{AirColor(n)}",
            // AIR-HOLD (v8): #BarTick:H x w c + 子行 #OffsetTick:s / :c（见 Umiguri Chart v8 doc）
            "AHD" or "AHX" => $"H{c}{w}{AirColor(n)}",
            "ALD" => $"C{c}{w}{EncodeAirHeight(n.Height)}{CrushColor(n)},{CrushInterval(n.CrushInterval)}",
            _ => ""
        };
    }
}
