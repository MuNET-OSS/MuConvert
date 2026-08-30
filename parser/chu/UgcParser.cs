using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;
using static MuConvert.utils.Utils;
using static MuConvert.utils.ChuUtils;

namespace MuConvert.chu;

/**
 * UMIGURI语法文档： https://gist.github.com/inonote/5c01e73781cab17765a1d93641d52298
 */
public class UgcParser: BaseChuParser
{
    private int RSL = 480 * 4;
    private int Version = 8;
    
    // 保存UGC中，原始的 @FLAG 信息，供一些特性的支持和外部的读取
    private Dictionary<string, bool> _ugcFlags = new();
    public IReadOnlyDictionary<string, bool> UgcFlags => _ugcFlags;

    private int useTil; // 当前的 @USETIL 值
    
    // 从UGC时间轴到C2S时间轴/chart标准时间轴的换算
    // UGC中的小节和tick，和C2S中，并不是一一对应的。因为C2S永远假设一小节由4拍构成，MET的值不会影响整张谱的时间轴；
    // 但UGC中的小节号，每一小节有多长/有几拍，是由 @BEAT 字段直接控制的，并不是保证一小节总是4拍/1920tick的。
    // 因此必须在两者之间进行换算。
    protected Rational T(int ugcBar, int ugcTick)
    {
        Rational result = 0;
        for (int i = 0; i < _ugcBeats.Count; i++)
        {
            var end = i == _ugcBeats.Count - 1 ? 9999999 : _ugcBeats[i + 1].Item1; 
            var barCount = Math.Min(end, ugcBar) - _ugcBeats[i].Item1;
            if (barCount <= 0) break;
            result += barCount * _ugcBeats[i].Item2;
        }
        result += new Rational(ugcTick, RSL);
        return result;
    }
    // 为了实现从上述 T函数 中的换算，所必要的信息。来自于ugc文件中的 @BEAT 字段
    private List<(int, Rational)> _ugcBeats = [];

    public override (ChuChart, List<Alert>) Parse(string text)
    {
        var chart = new ChuChart();
        var alerts = new List<Alert>();
        _ugcFlags = new();
        _ugcBeats = [];
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inHeader = true;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // UGC comment lines (starting with ')
            if (line.StartsWith('\'')) continue;

            if (inHeader)
            {
                if (line.StartsWith("@ENDHEAD"))
                {
                    inHeader = false;
                    continue;
                }
                ParseHeaderLine(line, chart, alerts, i + 1);
            }
            else
            {
                i = ParseNoteLine(lines, i, chart, alerts);
            }
        }

        FinalizeUgcSflDurations(chart);
        FillAllPrevious(chart, alerts);
        chart.Sort();
        if (UgcFlags.GetValueOrDefault("SOFFSET"))
        { // 根据UGC文档，@FLAG SOFFSET 表示应该给谱面开头添加一个小节的空白（“頭に 1 小節分の空白を挿入するかどうか”）
            chart.Shift(1);
        }
        return (chart, alerts);
    }

    private static void FinalizeUgcSflDurations(ChuChart chart)
    {
        var endTime = chart.Notes.Max(x => x.EndTime);
        foreach (var k in chart.SpeedGroups.Keys)
        {
            chart.SpeedGroups[k] = FinalizeUgcSflDurations(chart.SpeedGroups[k], endTime);
        }
    }
    private static List<SFL> FinalizeUgcSflDurations(List<SFL> sflList, Rational endTime)
    {
        if (sflList.Count == 0) return sflList;
        sflList = sflList.OrderBy(s => s.Time).ToList();
        endTime = Max(sflList[^1].Time, endTime);
        
        for (var i = 0; i < sflList.Count; i++)
        {
            var t = sflList[i].Time;
            var dur = (i < sflList.Count - 1 ? sflList[i+1].Time : endTime) - t;
            sflList[i] = sflList[i] with { Duration = dur.CanonicalForm };
        }

        sflList = sflList.Where(x => x.Multiplier != 1).ToList(); // 倍率为1的，没必要放进来的
        return sflList;
    }

    private static (string, string) SplitDirective(string line)
    {
        var spaceIdx = line.IndexOf('\t');
        var tag = spaceIdx > 0 ? line[..spaceIdx] : line;
        var value = spaceIdx > 0 ? line[(spaceIdx + 1)..].Trim() : "";
        return (tag, value);
    }
    
    private void ParseHeaderLine(string line, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        if (!line.StartsWith('@'))
        {
            alerts.Add(new Alert(Warning, $"意外的非头部行: {line}") { Line = lineNum });
            return;
        }

        var (tag, value) = SplitDirective(line);
        switch (tag)
        {
            case "@TITLE":
                chart.Title = value;
                break;

            case "@ARTIST":
                chart.Artist = value;
                break;

            case "@DESIGN":
                chart.Designer = value;
                break;

            case "@DIFF":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var diff))
                {
                    chart.Difficulty = diff;
                }
                else
                {
                    chart.Difficulty = new string(value.Where(char.IsLetter).ToArray()).ToUpperInvariant() switch
                    {
                        "BASIC" => 0,
                        "ADVANCED" => 1,
                        "EXPERT" => 2,
                        "MASTER" => 3,
                        "WORLDSEND" => 4,
                        "ULTIMA" => 5,
                        _ => 3,
                    };
                }
                break;

            case "@LEVEL":
                chart.DisplayLevel = value;
                break;

            case "@CONST":
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
                    chart.Level = constant;
                else
                    alerts.Add(new Alert(Warning, $"@CONST 格式错误: {line}") { Line = lineNum });
                break;

            case "@SONGID":
                chart.MusicId = value;
                break;

            case "@TICKS":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                    RSL = ticks * 4;
                else
                    alerts.Add(new Alert(Warning, $"@TICKS 格式错误: {line}") { Line = lineNum });
                break;

            case "@BEAT":
                var beatParts = value.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (beatParts.Length >= 3
                    && int.TryParse(beatParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatMeasure)
                    && int.TryParse(beatParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatNum)
                    && int.TryParse(beatParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var beatDen))
                {
                    _ugcBeats.Add((beatMeasure, new Rational(beatNum, beatDen)));
                    chart.MetList.Add(new MET(T(beatMeasure, 0), beatNum, beatDen));
                }
                else
                {
                    alerts.Add(new Alert(Warning, $"@BEAT 格式错误: {line}") { Line = lineNum });
                }
                break;

            case "@BPM":
                var bpmPart = value;
                var bpmSpaceIdx = bpmPart.IndexOfAny(['\t', ' ']);
                if (bpmSpaceIdx > 0)
                {
                    var measureOffset = bpmPart[..bpmSpaceIdx];
                    var bpmValueStr = bpmPart[(bpmSpaceIdx + 1)..];
                    if (TryParseUgcMeasureTick(measureOffset, out var bpmMeasure, out var bpmOffset)
                        && decimal.TryParse(bpmValueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpmValue))
                    {
                        chart.BpmList.Add(new BPM(T(bpmMeasure, bpmOffset), bpmValue));
                    }
                    else
                    {
                        alerts.Add(new Alert(Warning, $"@BPM 格式错误: {line}") { Line = lineNum });
                    }
                }
                else
                {
                    alerts.Add(new Alert(Warning, $"@BPM 格式错误: {line}") { Line = lineNum });
                }
                break;

            case "@VER":
                Version = int.Parse(value);
                break;

            case "@FLAG":
            {
                var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && bool.TryParse(parts[1], out var flagValue))
                    _ugcFlags[parts[0]] = flagValue;
                else
                    alerts.Add(new Alert(Warning, $"@FLAG 格式错误: {line}") { Line = lineNum });
                break;
            }
            
            // silently ignored metadata tags
            case "@EXVER": case "@SORT": case "@BGM": case "@BGMOFS": case "@BGMPRV":
            case "@JACKET": case "@BGIMG": case "@BGMODE": case "@FLDCOL": case "@FLDIMG":
            case "@ATINFO": case "@DLURL": case "@COPYRIGHT": case "@LICENSE":
            case "@MAINBPM":
            case "@BGSCENE": case "@FLDSCENE": case "@RLDATE": case "@CMT":
                break;
            
            case "@MAINTIL":
                if (int.Parse(value) != 0)
                {
                    alerts.Add(new Alert(Error, "暂不支持 @MAINTIL 不为0的谱面！"));
                    throw new ConversionException(alerts);
                }
                break;
            
            case "@USETIL":
                useTil = int.Parse(value);
                break;

            case "@TIL":
            {
                var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3
                    && int.TryParse(parts[0], out var groupId)
                    && TryParseUgcMeasureTick(parts[1], out var meas, out var tick)
                    && decimal.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mult))
                {
                    chart.SpeedGroups.Add(groupId, (T(meas, tick), Rational.Zero, mult));
                }
                else
                    alerts.Add(new Alert(Warning, $"@TIL 格式错误: {line}") { Line = lineNum });
                break;
            }

            case "@SPDMOD":
            {
                var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2
                    && TryParseUgcMeasureTick(parts[0], out var meas, out var tick)
                    && decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mult))
                {
                    chart.SflList.Add((T(meas, tick), Rational.Zero, mult));
                }
                else
                    alerts.Add(new Alert(Warning, $"@SPDMOD 格式错误: {line}") { Line = lineNum });
                break;
            }

            default:
                alerts.Add(new Alert(Info, $"未知头部标签: {tag}") { Line = lineNum });
                break;
        }
    }

    /** UGC 时刻字符串 measure'tick（@BPM、@SPDMOD、音符行 #m't 共用）。 */
    private static bool TryParseUgcMeasureTick(string measureTick, out int measure, out int tick)
    {
        measure = 0;
        tick = 0;
        measureTick = measureTick.Trim();
        var ap = measureTick.IndexOf('\'');
        if (ap <= 0)
            return false;

        return int.TryParse(measureTick[..ap], NumberStyles.Integer, CultureInfo.InvariantCulture, out measure)
            && int.TryParse(measureTick[(ap + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out tick);
    }

    private bool ProcessDirective(string line)
    {
        if (line.StartsWith('\'')) return true; // 注释行
        else if (line.StartsWith('@'))
        {
            var (tag, value) = SplitDirective(line);
            if (tag == "@USETIL") useTil = int.Parse(value);
            return true;
        }
        else return false;
    } 
    
    private int ParseNoteLine(string[] lines, int idx, ChuChart chart, List<Alert> alerts)
    {
        var line = lines[idx];
        var lineNum = idx + 1;

        if (ProcessDirective(line)) return idx;

        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0)
        {
            alerts.Add(new Alert(Warning, $"无法解析的音符行: {line}") { Line = lineNum });
            return idx;
        }

        var prefix = line[..colonIdx];
        var code = line[(colonIdx + 1)..];
        var hashIdx = prefix.IndexOf('#');
        if (hashIdx < 0)
        {
            alerts.Add(new Alert(Warning, $"音符行前缀格式错误: {line}") { Line = lineNum });
            return idx;
        }

        if (!TryParseUgcMeasureTick(prefix[(hashIdx + 1)..], out var measure, out var tick))
        {
            alerts.Add(new Alert(Warning, $"无法解析 measure'tick: {line}") { Line = lineNum });
            return idx;
        }

        if (string.IsNullOrEmpty(code))
        {
            alerts.Add(new Alert(Warning, $"音符行为空: {line}") { Line = lineNum });
            return idx;
        }

        ChuNote? note = new ChuNote
        {
            Time = T(measure, tick),
            SpeedGroup = useTil,
        };

        var typeChar = code[0];

        switch (typeChar)
        {
            case 't':
            case 'x':
            case 'f':
            case 'd':
                note = ParseTapNote(code, note, alerts, lineNum, chart, typeChar, line);
                break;

            case 'h':
            case 'H': // Air Hold
            case 's':
            case 'S': // Air Slide
                (idx, note) = ParseHoldOrSlideNote(typeChar, lines, idx, code, note, alerts, chart);
                break;

            case 'a':
                note = ParseAirNote(code, note, alerts, lineNum, chart);
                break;
            case 'C': // Air Crush
                (idx, note) = ParseAirCrushNote(lines, idx, code, note, alerts, chart);
                break;

            case 'c': // Umiguri的CLICK音符，疑似在C2s中是没有对应的。这个音符没有Cell和Width，除了Type什么都没有，所以直接忽略就可以了。
                break;

            default:
                alerts.Add(new Alert(Warning, $"未知的音符类型前缀 '{typeChar}': {line}", (chart, note.Time), lineNum, line));
                // 如果后面跟的是跟随行（子ノーツ）而非主行（親ノーツ）的话，把它们全部消耗掉
                while (idx + 1 < lines.Length)
                {
                    var nextLine = lines[idx + 1].Trim();
                    if (!TryParseFollowerLine(nextLine, out _, out _, out _, out _, out _, false))
                    {
                        if (ProcessDirective(nextLine)) { idx++; continue; }
                        break;
                    }
                    idx++;
                }
                return idx;
        }

        if (note != null) chart.Notes.Add(note);
        return idx;
    }

    private ChuNote ParseTapNote(string code, ChuNote note, List<Alert> alerts, int lineNum, ChuChart chart, char noteType, string line)
    {
        note.Type = ChuNoteType.Tap;
        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        var directionStr = code.Length > 3 ? code[3..] : "";
        if (noteType == 'x')
        {
            if (ExDirections_FromUgc.TryGetValue(directionStr, out var r)) note.Ex = r;
            else alerts.Add(new Alert(Warning, "ExTap/CHR音符的方向无效", (chart, note.Time), lineNum, line));
        }
        else if (noteType == 'f')
        {
            note.Type = ChuNoteType.Flick;
            if (directionStr is "L" or "R") note.Ex = ExDirections_FromUgc[directionStr];
            else alerts.Add(new Alert(Warning, "Flick音符的方向无效", (chart, note.Time), lineNum, line));
        } 
        else if (noteType == 'd') note.Type = ChuNoteType.Mine;
        return note;
    }
    
    private void ParseHeightAndColor(ChuNote n, string str, List<Alert> alerts, int lineNum, string noteType="") // 需要传入noteType是因为，不同版本的不同类型note在实现上还略有区别的。
    {
        if (string.IsNullOrEmpty(str)) return;
        if (str.Length == 1 && noteType is "H" or "S" && Version < 6)
        { // 老版本的:H和:S，单独的一位是height而不是颜色，因此不能套用下面的逻辑
            if (TryH36ToI(str, out var height)) n.Height = Height_FromUgc(height);
            else alerts.Add(new Alert(Warning, "解析Air系列音符的高度属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
            return;
        }

        if (noteType == "C")
        {
            // 先尝试解析interval
            var posOfComma = str.IndexOf(',');
            if (posOfComma >= 0)
            {
                var intervalStr = str[(posOfComma + 1)..];
                str = str[..posOfComma];
                if (intervalStr == "$") n.CrushInterval = null;
                else if (int.TryParse(intervalStr, out var interval)) n.CrushInterval = new Rational(interval, RSL);
                else alerts.Add(new Alert(Warning, "解析Air-Crush的interval属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
            }
            else if (Version >= 8) alerts.Add(new Alert(Warning, $"Air-Crush（v8）缺少crushInterval", n.Time, null, lineNum, FormatNoteRef(n, str))); // v8以上，按说是不应该这样的，所以给个警告
        }

        // 剩的部分都满足：最后一位是颜色，前面是高度
        if (str.Length > 0)
        { // 解析颜色
            var rawColorStr = str.Last().ToString();
            if (AirColor_FromUgc(n, rawColorStr, out var r)) n.Color = r;
        }
        if (str.Length > 1)
        { // 解析高度
            var heightStr = str[..^1];
            if (TryH36ToI(str[..^1], out var height)) 
                n.Height = Height_FromUgc(heightStr.Length == 1 ? height : height / 10m); // 一位时不用除以10，两位时需要除以10
            else alerts.Add(new Alert(Warning, "解析Air系列音符的高度属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
        }
    }
    
    // UGC中约定Air系列音符都一定紧跟在其Previous的后面。
    // 所以我们直接用上一个解析出的note就可以立即确定前驱了，无需再等到最后集中FillPrevious，而且等到最后集中FillPrevious时的结果也可能是错的。
    // 本函数作为一个工具函数干的就是这个事情。
    private bool AddAirPreviousFromLastNote(ChuNote note, ChuChart chart)
    {
        if (chart.Notes.Count > 0)
        {
            var filtered = FilterPreviousCandidates(note, [chart.Notes.Last()]); // 仅传入一个元素到FilterPreviousCandidates，因此返回结果最多一个元素
            if (filtered.Count > 0)
            {
                note.TargetNote = filtered[0];
                return true;
            }
        }
        return false;
    }

    private (int, ChuNote?) ParseHoldOrSlideNote(char noteType, string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, ChuChart chart)
    { 
        note.IsAir = noteType is 'S' or 'H';
        note.Type = noteType is 'S' or 's' ? ChuNoteType.Slide : ChuNoteType.Hold;
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);
        if (note.IsAir) ParseHeightAndColor(note, code[3..], alerts, idx+1, noteType.ToString());

        bool foundFirst = false;
        int lastSegTick = 0;
        while (idx + 1 < lines.Length)
        { // 循环处理所有的跟随行。idx始终指向上一条已经处理完的行。
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var marker, out var endTick, out var endCell, out var endWidth, 
                    out var endHeight, note.Type == ChuNoteType.Slide))
            {
                if (ProcessDirective(nextLine)) { idx++; continue; }
                break;
            }

            var segment = new ChuSegment(note) { C = marker == "c" };
            segment.Length = new Rational(endTick - lastSegTick, RSL);
            lastSegTick = endTick;
            if (endCell != null) segment.EndCell = endCell.Value;
            if (endWidth != null) segment.EndWidth = endWidth.Value;
            if (endHeight != null) segment.EndHeight = endHeight.Value;
            note.Segments.Add(segment);
            
            if (noteType == 'h' && segment.C) alerts.Add(new Alert(Warning, $"Hold不应有c类型的跟随行", (chart, note.EndTime), idx + 1, lines[idx]));
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
        {
            alerts.Add(new Alert(Warning, $"SLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = lines[idx] });
            return (idx, null);
        }
        if (note.IsAir)
        {
            if (!AddAirPreviousFromLastNote(note, chart)) // 尝试直接从上一个note添加前驱。如果失败了报警告。
                alerts.Add(new Alert(Warning, $"无法找到 Air Slide 的前驱音符", (chart, note.Time), idx + 1, lines[idx]));
        }
        return (idx, note);
    }
    
    private static bool TryParseFollowerLine(string line, out string marker, out int endTick, out int? endCell, out int? endWidth, out decimal? height, bool requireEndCellWidth)
    {
        endTick = 0;
        endCell = null;
        endWidth = null;
        marker = "";
        height = null;

        if (!line.StartsWith('#')) return false;

        // support both >s (SLD) and >c (SLC) follower lines
        int sepIdx = line.IndexOfAny(['>', ':']);
        if (sepIdx < 1) return false;
        marker = line[sepIdx+1].ToString();
        int markerLen = 2;

        var endTickStr = line[1..sepIdx];
        if (!int.TryParse(endTickStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out endTick)) return false;

        var afterMarker = line[(sepIdx + markerLen)..];
        if (afterMarker.Length >= 2)
        {
            endCell = HToI(afterMarker[0]);
            endWidth = HToI(afterMarker[1]);
        }
        else if (requireEndCellWidth) return false;

        if (afterMarker.Length > 2)
        {
            var heightStr = afterMarker[2..];
            if (TryH36ToI(heightStr, out var r)) height = heightStr.Length == 1 ? r : r / 10m;
        }

        return true;
    }

    private void ParseCellWidth(string code, int startIdx, ChuNote note, List<Alert> alerts, int lineNum, ChuChart chart)
    {
        if (code.Length > startIdx)
        {
            note.Cell = HToI(code[startIdx]);
            if (code.Length > startIdx + 1)
                note.Width = HToI(code[startIdx + 1]);
            else
                alerts.Add(new Alert(Warning, $"音符缺少 width: {code}", (chart, note.Time), lineNum, FormatNoteRef(note, code)));
        }
        else
        {
            alerts.Add(new Alert(Warning, $"音符缺少 cell 和 width: {code}", (chart, note.Time), lineNum, FormatNoteRef(note, code)));
        }
    }

    private ChuNote? ParseAirNote(string code, ChuNote note, List<Alert> alerts, int lineNum, ChuChart chart)
    {
        note.Type = ChuNoteType.Tap; // 出错情况下的缺省值
        note.IsAir = true;
        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        var mainPart = code[3..];
        if (mainPart.Length < 2)
        {
            alerts.Add(new Alert(Warning, $"AIR 音符代码过短: {code}") { Line = lineNum });
            return null;
        }

        // 解析方向
        var direction = mainPart[..2];
        if (AirDirections_FromUgc.TryGetValue(direction, out var airType)) note.AirDirection = airType;
        else alerts.Add(new Alert(Warning, $"未知的 AIR 方向: {direction}") { Line = lineNum, RelevantNote = FormatNoteRef(note, code) });
        // 解析颜色
        ParseHeightAndColor(note, mainPart[2..], alerts, lineNum, "a");
        
        if (!AddAirPreviousFromLastNote(note, chart)) // 尝试直接从上一个note添加前驱。如果失败了报警告。
            alerts.Add(new Alert(Warning, $"无法找到 Air 的前驱音符", (chart, note.Time), lineNum + 1, code));
        return note;
    }

    private (int, ChuNote?) ParseAirCrushNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, ChuChart chart)
    {
        note.Type = ChuNoteType.Crush;
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);
        if (code.Length <= 3) alerts.Add(new Alert(Warning, "AirCrush缺少参数！", (chart, note.Time), idx+1, lines[idx]));
        else ParseHeightAndColor(note, code[3..], alerts, idx+1, "C");
        
        bool foundFirst = false;
        int lastSegTick = 0;
        while (idx + 1 < lines.Length)
        { // 循环处理所有的跟随行。idx始终指向上一条已经处理完的行。
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var marker, out var endTick, out var endCell, out var endWidth, out var endHeight, Version >= 8))
            {
                if (ProcessDirective(nextLine)) { idx++; continue; }
                break;
            }
            
            if (Version >= 8 && marker != "c")
                alerts.Add(new Alert(Warning, $"Air-Crush（v8）子行标记应为 'c'，实际为 '{marker}'", (chart, note.EndTime), idx + 1, nextLine));
            if (Version <= 6 && marker == "s") note.CrushInterval ??= new Rational(endTick, RSL);
            if (endCell == null) { idx++; continue; } // 老版本当中，>s跟随行是用来记载interval的，此时没有endWidth是正常的，说明这个只是interval标记，解析interval后忽略即可，不用再管
            
            var segment = new ChuSegment(note) { C = marker == "c", EndCell = endCell.Value, EndWidth = endWidth!.Value };
            segment.Length = new Rational(endTick - lastSegTick, RSL);
            lastSegTick = endTick;
            if (endHeight != null) segment.EndHeight = endHeight.Value;
            note.Segments.Add(segment);
            
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
        {
            alerts.Add(new Alert(Warning, $"air-crush 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = lines[idx] });
            return (idx, null);
        }
        return (idx, note);
    }

    // ReSharper disable once UnusedParameter.Local
    private string FormatNoteRef(ChuNote note, string code)
    {
        var (m, o) = Utils.BarAndTick(note.Time, RSL);
        return $"#{m}'{o}:{code}";
    }
}

