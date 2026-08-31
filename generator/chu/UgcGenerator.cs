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
    
    private int useTil = 0; // 当前的 @USETIL 值

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
            if (n.TargetNote != null) nextDict.Add(n.TargetNote, n);
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

        #region 生成TIL
        Dictionary<(Rational time, int groupId), decimal> tilList = new()
        {
            [(0, 0)] = 1,
        };
        foreach (var (groupId, list) in ugc.SpeedGroups)
        {
            foreach (var t in list)
            {
                tilList[(t.Time.CanonicalForm, groupId)] = t.Multiplier;
                tilList[((t.Time + t.Duration).CanonicalForm, groupId)] = 1;
            }
        }
        
        foreach (var s in tilList.ToList()
                     .OrderBy(x=>(x.Key.time, x.Key.groupId)))
        { 
            var (m, o) = T(s.Key.time); 
            sb.AppendLine(FormattableString.Invariant($"@TIL\t{s.Key.groupId}\t{m}'{o}\t{s.Value:0.00000}"));
        }
        #endregion
        
        sb.AppendLine("@MAINTIL\t0"); // 用户没有通过ExtraHeaders指定，则提供一个默认值
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        var notes = SortedNotesForConnectingPrevious(ugc);
        foreach (var n in notes)
        {
            if (n.SpeedGroup != useTil)
            {
                useTil = n.SpeedGroup;
                sb.AppendLine($"@USETIL\t{useTil}");
            }

            var (m, o) = T(n.Time);
            var ucode = UCode(n);
            if (ucode == "")
            {
                alerts.Add(new Alert(Alert.LEVEL.Warning, $"UGC Generator遇到了不支持的音符类型: {n.Type}", (ugc, n.Time)));
                continue;
            }
            sb.Append($"#{m}'{o}:{ucode}");
            sb.AppendLine();

            AppendFollowerLines(sb, n);
        }
        return sb.ToString();
    }

    private void AppendFollowerLines(StringBuilder sb, ChuNote n)
    {
        if (n.Segments.Count == 0) return;

        Rational time = 0;
        foreach (var seg in n.Segments)
        {
            time += seg.Length;
            var endTick = Utils.Tick(time, RSL);

            var marker = seg.C ? 'c' : 's';
            if (IsAirSlide(n) || n.Type == ChuNoteType.Crush)
                sb.AppendLine($"#{endTick}>{marker}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}{EncodeAirHeight(seg.EndHeight)}");
            else if (n.Type == ChuNoteType.Slide)
                sb.AppendLine($"#{endTick}>{marker}{IToH36(seg.EndCell)}{IToH36(seg.EndWidth)}");
            else
                sb.AppendLine($"#{endTick}>{marker}");
        }
    }

    private static string EncodeAirHeight(decimal value) => IToH36(Math.Clamp((int)Math.Round(Height_ToUgc(value) * 10), 0, 1295)).PadLeft(2, '0');
    
    private string AirColor(ChuNote n)
    {
        var color = AirColor_ToUgc(n);
        if (color == "N" && n.Color is not (NoteColor.DEF or NoteColor.GRN or NoteColor.PPL))
            alerts.Add(new Alert(Alert.LEVEL.Warning, string.Format(Locale.C2SUnsupportedAirColor, "UGC Generator", n.Type, n.Color), n.Time));
        return color;
    }
    private string CrushColor(ChuNote n) => AirCrush_Color_ToUgc[n.Color];
    private string CrushInterval(Rational? crushInterval) => 
        crushInterval != null ? Utils.Tick(crushInterval.Value, RSL).ToString() : "$";

    private string UCode(ChuNote n)
    {
        string c = IToH36(n.Cell), w = IToH36(n.Width);
        return (n.Type, n.IsAir) switch
        {
            (ChuNoteType.Tap, false) when !n.IsEx => $"t{c}{w}",
            (ChuNoteType.Tap, false) => $"x{c}{w}{ExDirections_ToUgc[n.Ex!.Value]}",
            (ChuNoteType.Tap, true) => $"a{c}{w}{AirDirections_ToUgc[n.AirDirection]}{AirColor(n)}",
            (ChuNoteType.Flick, _) => $"f{c}{w}{n.Ex switch { ExDirection.RS => "R", _ => "L" }}",
            (ChuNoteType.Hold, false) => $"h{c}{w}",
            (ChuNoteType.Hold, true) => $"H{c}{w}{AirColor(n)}",
            (ChuNoteType.Slide, false) => $"s{c}{w}",
            (ChuNoteType.Slide, true) => $"S{c}{w}{EncodeAirHeight(n.Height)}{AirColor(n)}",
            (ChuNoteType.Crush, _) => $"C{c}{w}{EncodeAirHeight(n.Height)}{CrushColor(n)},{CrushInterval(n.CrushInterval)}",
            (ChuNoteType.Mine, _) => $"d{c}{w}",
            _ => ""
        };
    }
}
