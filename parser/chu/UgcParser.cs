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

    public override (ChuChart, List<Alert>) Parse(string text)
    {
        var chart = new ChuChart();
        var alerts = new List<Alert>();
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
        return (chart, alerts);
    }

    private static void FinalizeUgcSflDurations(ChuChart chart)
    {
        if (chart.SflList.Count == 0) return;
        chart.SflList = chart.SflList.OrderBy(s => s.Time).ToList();
        var endTime = Utils.Max(chart.SflList[^1].Time, chart.Notes.Max(x=>x.EndTime));
        
        for (var i = 0; i < chart.SflList.Count; i++)
        {
            var t = chart.SflList[i].Time;
            var dur = (i < chart.SflList.Count - 1 ? chart.SflList[i+1].Time : endTime) - t;
            chart.SflList[i] = chart.SflList[i] with { Duration = dur.CanonicalForm };
        }

        chart.SflList = chart.SflList.Where(x => x.Multiplier != 1).ToList(); // 倍率为1的，没必要放进来的
    }

    private void ParseHeaderLine(string line, ChuChart chart, List<Alert> alerts, int lineNum)
    {
        if (!line.StartsWith('@'))
        {
            alerts.Add(new Alert(Warning, $"意外的非头部行: {line}") { Line = lineNum });
            return;
        }

        var spaceIdx = line.IndexOf('\t');
        var tag = spaceIdx > 0 ? line[..spaceIdx] : line;
        var value = spaceIdx > 0 ? line[(spaceIdx + 1)..].Trim() : "";

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
                    chart.MetList.Add(new MET(beatMeasure, beatNum, beatDen));
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
                        chart.BpmList.Add(new BPM(bpmMeasure + new Rational(bpmOffset, RSL), bpmValue));
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
            
            // silently ignored metadata tags
            case "@EXVER": case "@SORT": case "@BGM": case "@BGMOFS": case "@BGMPRV":
            case "@JACKET": case "@BGIMG": case "@BGMODE": case "@FLDCOL": case "@FLDIMG":
            case "@FLAG": case "@ATINFO": case "@DLURL": case "@COPYRIGHT": case "@LICENSE":
            case "@MAINTIL": case "@TIL": case "@USETIL":
            case "@MAINBPM":
            case "@BGSCENE": case "@FLDSCENE": case "@RLDATE": case "@CMT":
                break;

            case "@SPDMOD":
            {
                var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2
                    && TryParseUgcMeasureTick(parts[0], out var meas, out var tick)
                    && decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mult))
                {
                    chart.SflList.Add((meas + new Rational(tick, RSL), Rational.Zero, mult));
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

    private int ParseNoteLine(string[] lines, int idx, ChuChart chart, List<Alert> alerts)
    {
        var line = lines[idx];
        var lineNum = idx + 1;

        // skip comment lines and inline directives
        if (line.StartsWith('\'') || line.StartsWith('@'))
            return idx;

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
            Time = measure + new Rational(tick, RSL),
        };

        var typeChar = code[0];

        switch (typeChar)
        {
            case 't':
                ParseTapNote(code, note, alerts, lineNum, chart, false);
                break;
            case 'x':
                ParseTapNote(code, note, alerts, lineNum, chart, true);
                break;

            case 'h':
                idx = ParseHoldNote(false, lines, idx, code, note, alerts, chart);
                break;
            case 'H': // Air Hold
                idx = ParseHoldNote(true, lines, idx, code, note, alerts, chart);
                break;

            case 's':
                idx = ParseSlideNote(false, lines, idx, code, note, alerts, chart);
                note = null; // ParseSlideNote中，会自己构造note并自己添加进chart。因此这里默认的统一note不应被添加进chart。
                break;
            case 'S': // Air Slide
                idx = ParseSlideNote(true, lines, idx, code, note, alerts, chart);
                note = null;
                break;

            case 'a':
                ParseAirNote(code, note, alerts, lineNum, chart);
                break;
            case 'C': // Air Crush
                idx = ParseAirCrushNote(lines, idx, code, note, alerts, chart);
                note = null;
                break;

            case 'f':
                note.Type = "FLK";
                ParseCellWidth(code, 1, note, alerts, lineNum, chart);
                if (code.Length > 3) note.Tag = code[3..];
                break;

            case 'c': // Umiguri的CLICK音符，疑似在C2s中是没有对应的。这个音符没有Cell和Width，除了Type什么都没有，所以直接存下来就可以了。
                note.Type = "CLICK";
                break;

            case 'd':
                note.Type = "MNE";
                ParseCellWidth(code, 1, note, alerts, lineNum, chart);
                break;

            default:
                alerts.Add(new Alert(Warning, $"未知的音符类型前缀 '{typeChar}': {line}", (chart, note.Time), lineNum, line));
                // 如果后面跟的是跟随行（子ノーツ）而非主行（親ノーツ）的话，把它们全部消耗掉
                while (idx + 1 < lines.Length)
                {
                    var nextLine = lines[idx + 1].Trim();
                    if (!TryParseFollowerLine(nextLine, out _, out _, out _, out _, out _, false))
                    {
                        if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                        break;
                    }
                    idx++;
                }
                return idx;
        }

        if (note != null) chart.Notes.Add(note);
        return idx;
    }

    private void ParseTapNote(string code, ChuNote note, List<Alert> alerts, int lineNum, ChuChart chart, bool isCHR)
    {
        note.Type = "TAP";
        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        if (isCHR)
        {
            note.Type = "CHR";
            var extraRaw = code.Length > 3 ? code[3..] : "";
            note.Tag = U2C_ChrExtras.GetValueOrDefault(extraRaw, extraRaw);
        }
    }

    private void ParseHeightAndColor(ChuNote n, string str, List<Alert> alerts, int lineNum, string noteType="") // 需要传入noteType是因为，不同版本的不同类型note在实现上还略有区别的。
    {
        if (string.IsNullOrEmpty(str)) return;
        if (str.Length == 1 && noteType is "H" or "S" && Version < 6)
        { // 老版本的:H和:S，单独的一位是height而不是颜色，因此不能套用下面的逻辑
            if (TryH36ToI(str, out var height)) n.Height = U2C_Height(height);
            else alerts.Add(new Alert(Warning, "解析Air系列音符的高度属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
            return;
        }
        
        // 先尝试解析interval
        var posOfComma = str.IndexOf(',');
        if (posOfComma >= 0)
        {
            var intervalStr = str[(posOfComma+1)..];
            str = str[..posOfComma];
            if (intervalStr == "$") n.CrushInterval = 100;
            else if (int.TryParse(intervalStr, out var interval)) n.CrushInterval = new Rational(interval, RSL);
            else alerts.Add(new Alert(Warning, "解析Air-Crush的interval属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
        }

        // 剩的部分都满足：最后一位是颜色，前面是高度
        if (str.Length > 0)
        { // 解析颜色
            var rawColorStr = str.Last().ToString();
            n.Tag = U2C_AirColor.GetValueOrDefault(rawColorStr, rawColorStr);
        }
        if (str.Length > 1)
        { // 解析高度
            var heightStr = str[..^1];
            if (TryH36ToI(str[..^1], out var height)) 
                n.Height = U2C_Height(heightStr.Length == 1 ? height : height / 10m); // 一位时不用除以10，两位时需要除以10
            else alerts.Add(new Alert(Warning, "解析Air系列音符的高度属性失败！", n.Time, null, lineNum, FormatNoteRef(n, str)));
        }
    }

    private int ParseHoldNote(bool isAirHold, string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, ChuChart chart)
    {
        note.Type = isAirHold ? "AHD" : "HLD";
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);
        if (isAirHold) ParseHeightAndColor(note, code[3..], alerts, idx+1, "H");

        bool foundFirst = false;
        while (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var marker, out var endTick, out _, out _, out _, false))
            {
                if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                break;
            }

            note.Duration = new Rational(endTick, RSL);
            if (isAirHold && marker == "c") note.Type = "AHX"; // 可能是对应于UMIGURI文档中的 AirHold的 AIR-ACTION 无し终点
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
            alerts.Add(new Alert(Warning, $"HLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = lines[idx] });
        return idx;
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
                note.Previous = filtered[0];
                return true;
            }
        }
        return false;
    }

    private int ParseSlideNote(bool isAirSlide, string[] lines, int idx, string code, ChuNote previousNote, List<Alert> alerts, ChuChart chart)
    {
        // 注：一开始从外面传进来的previousNote，最后并不会被添加进chart里，只是作为第一段的起点参照而已。
        var startTime = previousNote.Time;
        ParseCellWidth(code, 1, previousNote, alerts, idx + 1, chart);
        if (isAirSlide) ParseHeightAndColor(previousNote, code[3..], alerts, idx+1, "S");
        previousNote.EndCell = previousNote.Cell;
        previousNote.EndWidth = previousNote.Width;
        previousNote.EndHeight = previousNote.Height;

        bool foundFirst = false;
        while (idx + 1 < lines.Length)
        { // 循环处理所有的跟随行。idx始终指向上一条已经处理完的行。
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var marker, out var endTick, out var endCell, out var endWidth, out var endHeight, true))
            {
                if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                break;
            }

            var type = isAirSlide ? (marker == "s" ? "ASD" : "ASC") : (marker == "s" ? "SLD" : "SLC");

            var segmentEnd = startTime + new Rational(endTick, RSL);
            var note = new ChuNote
            {
                Type = type, Time = previousNote.EndTime, 
                Cell = previousNote.EndCell, Width = previousNote.EndWidth, Height = previousNote.EndHeight,
                Duration = segmentEnd - previousNote.EndTime, Tag = previousNote.Tag,
                EndCell = endCell!.Value, EndWidth = endWidth!.Value, 
                EndHeight = endHeight != null ? U2C_Height(endHeight.Value) : previousNote.EndHeight,
                Previous = foundFirst ? previousNote : null,
            };

            if (isAirSlide && !foundFirst)
            {
                if (!AddAirPreviousFromLastNote(note, chart)) // 尝试直接从上一个note添加前驱。如果失败了报警告。
                    alerts.Add(new Alert(Warning, $"无法找到 Air Slide 的前驱音符", (chart, note.Time), idx + 1, lines[idx]));
            }
            
            chart.Notes.Add(note);
            previousNote = note;
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
            alerts.Add(new Alert(Warning, $"SLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = lines[idx] });

        return idx;
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

    private void ParseAirNote(string code, ChuNote note, List<Alert> alerts, int lineNum, ChuChart chart)
    {
        note.Type = "AIR"; // 出错情况下的缺省值
        if (code.Length < 5)
        {
            alerts.Add(new Alert(Warning, $"AIR 音符代码过短: {code}") { Line = lineNum });
            return;
        }

        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        var mainPart = code[3..];

        // 解析方向
        var direction = mainPart[..2];
        if (U2C_AirDirections.TryGetValue(direction, out var airType)) note.Type = airType;
        else alerts.Add(new Alert(Warning, $"未知的 AIR 方向: {direction}") { Line = lineNum, RelevantNote = FormatNoteRef(note, code) });
        // 解析颜色
        ParseHeightAndColor(note, mainPart[2..], alerts, lineNum, "a");
        
        if (!AddAirPreviousFromLastNote(note, chart)) // 尝试直接从上一个note添加前驱。如果失败了报警告。
            alerts.Add(new Alert(Warning, $"无法找到 Air 的前驱音符", (chart, note.Time), lineNum + 1, code));
    }

    private int ParseAirCrushNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, ChuChart chart)
    {
        note.Type = "ALD";
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);
        if (code.Length <= 3) alerts.Add(new Alert(Warning, "AirCrush缺少参数！", (chart, note.Time), idx+1, lines[idx]));
        else ParseHeightAndColor(note, code[3..], alerts, idx+1, "C");
        
        bool foundFirst = false;
        bool intervalSet = Version >= 8;
        while (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var marker, out var endTick, out var endCell, out var endWidth, out var endHeight, Version >= 8))
            {
                if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                break;
            }
            
            if (Version >= 8 && marker != "c")
                alerts.Add(new Alert(Warning, $"Air-Crush（v8）子行标记应为 'c'，实际为 '{marker}'", (chart, note.Time), idx + 1, nextLine));

            if (Version <= 6 && !intervalSet && marker == "s")
            {
                note.CrushInterval = new Rational(endTick, RSL);
                intervalSet = true;
            }
            
            note.Duration = new Rational(endTick, RSL);
            if (endCell != null) note.EndCell = endCell.Value;
            if (endWidth != null) note.EndWidth = endWidth.Value;
            if (endHeight != null) note.EndHeight = U2C_Height(endHeight.Value);

            idx++;
            foundFirst = true;
        }
        chart.Notes.Add(note);

        if (!foundFirst)
            alerts.Add(new Alert(Warning, $"air-crush 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = lines[idx] });
        return idx;
    }

    // ReSharper disable once UnusedParameter.Local
    private string FormatNoteRef(ChuNote note, string code)
    {
        var (m, o) = Utils.BarAndTick(note.Time, RSL);
        return $"#{m}'{o}:{code}";
    }
}

