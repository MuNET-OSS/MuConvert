using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;
using SegDictKey = (MuConvert.chu.ChuNoteType Type, bool IsAir, Rationals.Rational Time, int Cell, int Width);

namespace MuConvert.chu;

/**
 * C2S 格式解析器（官方格式，RESOLUTION=384 tick/小节）。
 * Tab 分隔文本，识别 HEADER / TIMING / NOTES 区段。
 */
public class C2sParser: BaseChuParser
{
    private int RSL = 384;
    private static readonly HashSet<string> HeadTags = new(StringComparer.OrdinalIgnoreCase)
        { "VERSION", "MUSIC", "SEQUENCEID", "DIFFICULT", "LEVEL", "CREATOR", "BPM_DEF", "MET_DEF", "RESOLUTION", "CLK_DEF", "PROGJUDGE_BPM", "PROGJUDGE_AER", "TUTORIAL" };
    private static readonly HashSet<string> TimingTags = new(StringComparer.OrdinalIgnoreCase)
        { "BPM", "MET", "SFL", "SLP" };

    private bool _used;
    // C2S 会原始记录 targetNote 字符串；用于在 FillAllPrevious 推断有多个候选时优先匹配。
    private readonly Dictionary<ChuNote, string> _rawTargetNote = new();
    private readonly Dictionary<(Rational Time, int Cell, int Width), List<(Rational, int)>> _slaRecords = new();
    private readonly Dictionary<SegDictKey, List<ChuNote>> segDict = new();

    public override (ChuChart, List<Alert>) Parse(string text)
    {
        if (_used) throw new Exception(Locale.InstanceMultipleUsage);
        _used = true;
        var chart = new ChuChart();
        var alerts = new List<Alert>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool inNotes = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("T_")) continue;

            var parts = line.Split('\t');
            var tag = parts[0].ToUpperInvariant();

            if (inNotes || !HeadTags.Contains(tag) && !TimingTags.Contains(tag))
            {
                inNotes = true;
                ParseNote(parts, chart, alerts, i + 1);
            }
            else if (HeadTags.Contains(tag))
            {
                ParseHeader(parts, chart);
            }
            else if (TimingTags.Contains(tag))
            {
                ParseTiming(parts, chart);
                inNotes = false;
            }
        }

        FillAllPrevious(chart, alerts, _rawTargetNote);
        ProcessSLA(chart);
        chart.Sort();
        return (chart, alerts);
    }

    private void ProcessSLA(ChuChart chart)
    {
        foreach (var note in chart.Notes)
        {
            var t = (note.Time, note.Cell, note.Width);
            if (_slaRecords.TryGetValue(t, out var items))
            {
                var item = items.FirstOrDefault(x => x.Item1 >= note.Duration);
                if (item != default) note.SpeedGroup = item.Item2;
            }
        }
    }

    private void ParseHeader(string[] p, ChuChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "MUSIC": chart.MusicId = Int(p, 1).ToString(); break;
            case "DIFFICULT": chart.Difficulty = Int(p, 1); break;
            case "LEVEL": chart.Level = Decimal(p, 1); break;
            case "CREATOR": chart.Designer = Str(p, 1); break;
            case "RESOLUTION": RSL = Math.Max(1, Int(p, 1, 384)); break;
        }
    }

    private void ParseTiming(string[] p, ChuChart chart)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "BPM":
                chart.BpmList.Add(new BPM(Int(p, 1) + new Rational(Int(p, 2), RSL), 
                    decimal.Parse(p[3], CultureInfo.InvariantCulture)));
                break;
            case "MET":
                chart.MetList.Add(new MET(Int(p, 1) + new Rational(Int(p, 2), RSL), Int(p, 4, 4), Int(p, 3, 4)));
                break;
            case "SFL":
                chart.SflList.Add((
                    Int(p, 1) + new Rational(Int(p, 2), RSL),
                    new Rational(Int(p, 3), RSL),
                    decimal.Parse(p[4], CultureInfo.InvariantCulture)));
                break;
            case "SLP":
                chart.SpeedGroups.Add(Int(p, 5), (
                    Int(p, 1) + new Rational(Int(p, 2), RSL),
                    new Rational(Int(p, 3), RSL),
                    decimal.Parse(p[4], CultureInfo.InvariantCulture)));
                break;
        }
    }

    private ChuSegment ParseSegment(ChuNote note, string[] p, string type)
    {
        var seg = new ChuSegment(note)
        {
            C = note.Type == ChuNoteType.Crush || (note.Type == ChuNoteType.Slide && type[2] == 'C'),
        };

        var durationIdx = note.IsAir ? (note.Type == ChuNoteType.Hold ? 6 : 7) : 5;
        seg.Length = new Rational(Int(p, durationIdx), RSL);
        if (note.Type is ChuNoteType.Slide or ChuNoteType.Crush)
        {
            seg.EndCell = Int(p, durationIdx + 1); 
            seg.EndWidth = Math.Max(1, Int(p, durationIdx + 2, 1));
            if (note.IsAir) seg.EndHeight = Decimal(p, durationIdx + 3, 5);
        }
        
        return seg;
    }

    private void ParseNote(string[] p, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        var type = p[0].ToUpperInvariant();
        ChuNote? note = new ChuNote
        {
            Time = Int(p, 1) + new Rational(Int(p, 2), RSL),
            Cell = Int(p, 3), Width = Math.Max(1, Int(p, 4, 1)),
        };

        if (type == "SLA")
        { // SLA要单独处理
            var length = new Rational(Int(p, 5), RSL);
            var groupId = Int(p, 6);
            _slaRecords.Add((note.Time, note.Cell, note.Width), (length, groupId));
            return;
        }
        
        var t = type switch
        {
            "TAP" or "CHR" => (ChuNoteType.Tap, false),
            "MNE" => (ChuNoteType.Mine, false),
            "FLK" => (ChuNoteType.Flick, false),
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" => (ChuNoteType.Tap, true),
            "HLD" or "HXD" => (ChuNoteType.Hold, false),
            "SLD" or "SLC" or "SXD" or "SXC" => (ChuNoteType.Slide, false),
            "AHD" or "AHX" => (ChuNoteType.Hold, true),
            "ASD" or "ASC" => (ChuNoteType.Slide, true),
            "ALD" => (ChuNoteType.Crush, true),
            _ => AlertUnknownType(type),
        };
        if (t == null) return;
        (note.Type, note.IsAir) = t.Value;
        
        string? targetNote = null;
        if (note.Type is ChuNoteType.Tap or ChuNoteType.Mine or ChuNoteType.Flick)
        {
            if (type == "CHR") ParseEnum<ExDirection>(Str(p, 5), x=>note.Ex = x);
            else if (note is { Type: ChuNoteType.Tap, IsAir: true })
            {
                ParseEnum<AirDirection>(type, x=>note.AirDirection = x);
                targetNote = Str(p, 5);
                if (p.Length >= 7) ParseEnum<NoteColor>(Str(p, 6), x=>note.Color = x);
            }
        }
        else
        {
            // ReSharper disable AccessToModifiedClosure
            // 先解析数据字段
            if (ChuUtils.ShouldHaveHeight(note)) note.Height = Decimal(p, 6, 5);
            if (note.Type == ChuNoteType.Crush) note.CrushInterval = CrushInterval(p, 5);
            if (note.IsAir) // 解析颜色
                ParseEnum<NoteColor>(Str(p, note.Type == ChuNoteType.Hold ? 7 : 11), x=>note.Color = x);
            if (type is "HXD" or "SXD" or "SXC") // 解析Ex
                ParseEnum<ExDirection>(Str(p, type == "HXD" ? 6 : 9), x=>note.Ex = x);
            
            // 首先，对Air Hold/Air Slide，需要读取TargetNote，确定它是否是接续段；其他类型的音符，则默认允许是接续段
            bool canConnect = true, isConnect = false;
            if (note is { IsAir: true, Type: ChuNoteType.Hold or ChuNoteType.Slide })
            {
                targetNote = Str(p, 5);
                canConnect = (note.Type == ChuNoteType.Hold && targetNote == "AHD") ||
                             (note.Type == ChuNoteType.Slide && targetNote is "ASD" or "ASC");
            }
            SegDictKey segKey = (note.Type, note.IsAir, note.Time, note.Cell, note.Width);
            if (canConnect && segDict.TryGetValue(segKey, out var list))
            {
                IEnumerable<ChuNote> f = list;
                if (note.IsAir) f = f.Where(x => x.Color == note.Color);
                if (ChuUtils.ShouldHaveHeight(note)) f = f.Where(x => x.Segments.Last().EndHeight == note.Height);
                var filtered = f.ToList();
                if (filtered.Count > 0)
                { // 说明找到了前驱。则note应该改为前驱，并阻止刚才创建的伪note加入谱面（通过把isConnect设为true实现）。
                    note = filtered[0];
                    Utils.Assert(list.Remove(filtered[0]));
                    isConnect = true;
                }
            }
            // ReSharper restore AccessToModifiedClosure

            note.Segments.Add(ParseSegment(note, p, type));
            segDict.Add((note.Type, note.IsAir, note.EndTime, note.EndCell, note.EndWidth), note);
            if (isConnect) note = null; // 阻止note加入谱面，因为现在的note是之前已经被加入过一次的那个了
        }

        if (note == null) return;
        if (targetNote != null) _rawTargetNote[note] = targetNote;
        chart.Notes.Add(note);

        void ParseEnum<T>(string str, Action<T> assign) where T : struct, Enum
        {
            if (Enum.TryParse(str, out T a)) assign(a);
            else AlertTag(str);
        }
        void AlertTag(string str) => alerts.Add(new Alert(Warning, $"无法识别的方向/颜色标签：{str}", (chart, note.Time), lineNum, string.Join("\t", p)));
        (ChuNoteType, bool)? AlertUnknownType(string str)
        {
            alerts.Add(new Alert(Warning, $"无法识别的C2S指令：{str}", (chart, note.Time), lineNum, string.Join("\t", p)));
            return null;
        }
    }

    private static int Int(string[] p, int i, int def = 0) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static decimal Decimal(string[] p, int i, decimal def = 0) => i < p.Length && decimal.TryParse(p[i], CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string Str(string[] p, int i) => i < p.Length ? p[i] : "";

    private Rational? CrushInterval(string[] p, int i)
    {
        var v = Int(p, i);
        if (v >= 9600) return null;
        else return new Rational(v, RSL);
    }
}
