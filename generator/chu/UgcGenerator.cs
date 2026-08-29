using System.Text;
using MuConvert.chart;
using MuConvert.generator;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

public class UgcGenerator : IGenerator<ChuChart>
{
    private int RSL = 480 * 4;
    private List<Alert> alerts = [];
    public List<(string, string)> ExtraHeaders = [];

    /**
     * <param name="extraHeaders">在生成的UGC的HEAD区域，添加上额外的字段。</param>
     */
    public UgcGenerator(List<(string, string)>? extraHeaders = null)
    {
        if (extraHeaders != null) ExtraHeaders = extraHeaders;
    }

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

    // 从C2S时间轴/chart标准时间轴，到UGC时间轴的换算
    // UGC中的小节和tick，和C2S中，并不是一一对应的。因为C2S永远假设一小节由4拍构成，MET的值不会影响整张谱的时间轴；
    // 但UGC中的小节号，每一小节有多长/有几拍，是由 @BEAT 字段直接控制的，并不是保证一小节总是4拍/1920tick的。
    // 因此必须在两者之间进行换算。
    protected (int, int) T(Rational time)
    {
        int resBar = 0;
        for (int i = 0; i < _ugcBeats.Count; i++)
        {
            var b = _ugcBeats[i];
            var ratio = new Rational(b.Item2, b.Item3);
            var rangeLen = i == _ugcBeats.Count - 1 ? 9999999 : (_ugcBeats[i + 1].Item1 - b.Item1) * ratio;
            if (rangeLen < time)
            { // 整个区间花完了
                resBar = _ugcBeats[i + 1].Item1;
                time -= rangeLen;
            }
            else
            {
                var extra = time / ratio;
                resBar += (int)extra.WholePart;
                int resTick = (int)(extra.FractionPart * ratio * RSL).Round();
                return (resBar, resTick);
            }
        }
        throw Utils.Fail();
    }
    // 为了实现从上述 T函数 中的换算，所必要的信息。可通过CalcUgcBeats函数算出。
    private List<(int, int, int)> _ugcBeats = [];
    
    private void FillUgcBeats(List<MET> metList)
    {
        _ugcBeats = [];
        foreach (var met in metList)
        {
            if (_ugcBeats.Count == 0)
            {
                if (met.Time > 0) _ugcBeats.Add((0, 4, 4)); // 鲁棒性，补 @BEAT 0 4 4。不能continue，因为马上还要添加显式的那一条。
                else
                { // met.Time == 0，把这条添加进去后直接continue
                    _ugcBeats.Add((0, met.Numerator, met.Denominator));
                    continue;
                }
            }
            // 到此处，_ugcBeats.Count一定>0了，所以可以调用T函数了
            var (ugcBar, ugcTick) = T(met.Time);
            if (ugcTick > 0)
            { // 如有残余部分，直接组织成一个新的@BEAT
                var extra = new Rational(ugcTick, RSL).CanonicalForm;
                _ugcBeats.Add((ugcBar, (int)extra.Numerator, (int)extra.Denominator));
                (ugcBar, ugcTick) = T(met.Time);
                Utils.Assert(ugcTick == 0);
            }
            _ugcBeats.Add((ugcBar, met.Numerator, met.Denominator));
        }
    }

    private string Serialize(ChuChart ugc)
    {
        ugc.Sort();
        FillUgcBeats(ugc.MetList);
        var extraHeaderKeys = ExtraHeaders.Select(x => x.Item1).ToHashSet();
        
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
        foreach (var (key, value) in ExtraHeaders)
        { // 写入用户传入的ExtraHeaders中要求的字段
            sb.AppendLine($"@{key}\t{value}");
        }
        sb.AppendLine("@FLAG\tHIPRECISION\tTRUE"); // 表明，谱面中的高度使用的是两位高度而不是一位高度
        sb.AppendLine($"@TICKS\t{RSL / 4}");
        foreach (var (bar, num, deno) in _ugcBeats)
        {
            sb.AppendLine($"@BEAT\t{bar}\t{num}\t{deno}");
        }
        foreach (var b in ugc.BpmList)
        {
            var (m, o) = T(b.Time);
            sb.AppendLine(FormattableString.Invariant($"@BPM\t{m}'{o}\t{b.Bpm:F5}"));
        }
        if (!extraHeaderKeys.Contains("TIL")) sb.AppendLine("@TIL\t0\t0'0\t1.00000"); // 用户没有通过ExtraHeaders指定，则提供一个默认值

        foreach (var s in ugc.SflList.OrderBy(x => x.Time)) 
        { 
            var (m, o) = T(s.Time); 
            sb.AppendLine(FormattableString.Invariant($"@SPDMOD\t{m}'{o}\t{s.Multiplier:0.00000}"));
        }

        if (!extraHeaderKeys.Contains("MAINTIL")) sb.AppendLine("@MAINTIL\t0"); // 用户没有通过ExtraHeaders指定，则提供一个默认值
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        var notes = SortedNotesForConnectingPrevious(ugc);

        // UGC Slide / AIR-SLIDE / AIR-HOLD / AIR-CRUSH (v8):
        // - Chains (ChuNote.Previous) serialize as ONE parent line + follower lines (#OffsetTick from parent time).
        // - Ground slide: parent `s`, followers `>s` / `>c` + end cell/width.
        // - Air slide: parent `S` + cell/width + hh + N/I; followers `>s`/`>c` + xw + hh.
        // - Air hold: parent `H` + cell/width + color; followers `>s` / `>c` only.
        // - Air crush: parent `C` + cell/width + hh + color,interval; followers `>c` + xw + hh.
        // - First segment may attach to TAP/HLD via Previous; only skip emit when Previous is another segment of the same chain.
        var slideChains = BuildSlideChains(notes);

        foreach (var n in notes)
        {
            if (IsSlideChainNote(n.Type) && IsChainContinueSegments(n))
                continue; // 是链式音符且不是第一段，则应当已经被处理过了，直接跳过

            var (m, o) = T(n.Time);
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
                    foreach (var seg in segments)
                    {
                        var endTicks = Utils.Tick(seg.EndTime - n.Time, RSL);
                        if (endTicks <= 0) continue;
                        if (IsAirSlide(n.Type) || IsAirCrush(n.Type))
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}{EncodeAirHeight(seg.EndHeight)}");
                        else if (IsSlide(n.Type))
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}");
                        else
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}");
                    }
                }
            }
            else
            {
                var durTicks = Utils.Tick(n.Duration, RSL);
                if (n.Type is "HLD" or "HXD" && durTicks > 0)
                    sb.AppendLine($"#{durTicks}>s");
            }
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
        while (IsChainContinueSegments(cur)) cur = cur.Previous!;
        return cur;
    }
    
    private static char SlideFollowerMarker(string t) => t is "SLC" or "SXC" or "ASC" or "ALD" ? 'c' : 's';

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
