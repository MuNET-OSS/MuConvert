using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.chu;

/**
 * UGC 格式解析器（UMIGURI 格式，@TICKS=480 tick/拍）。
 * @HEADER 标签 + #measure'tick:code 音符格式。
 */
public class UgcParser : IParser<UgcChart>
{
    private static readonly Dictionary<string, string> AirDirections = new()
    {
        ["UC"] = "AIR",
        ["UR"] = "AUR",
        ["UL"] = "AUL",
        ["DC"] = "ADW",
        ["DR"] = "ADR",
        ["DL"] = "ADL",
        ["HD"] = "AHD",
    };

    private static readonly Dictionary<string, string> ChrExtras = new()
    {
        ["U"] = "UP",
        ["D"] = "DW",
        ["C"] = "CE",
    };

    public (UgcChart, List<Alert>) Parse(string text)
    {
        var chart = new UgcChart();
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
                if (line == "@ENDHEAD")
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
        return (chart, alerts);
    }

    private static void FinalizeUgcSflDurations(UgcChart chart)
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

    private static void ParseHeaderLine(string line, UgcChart chart, List<Alert> alerts, int lineNum)
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
                    chart.TicksPerBeat = ticks;
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
                        var tpm = chart.TicksPerBeat * 4;
                        chart.BpmList.Add(new BPM(bpmMeasure + new Rational(bpmOffset, tpm), bpmValue));
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

            // silently ignored metadata tags
            case "@VER": case "@EXVER": case "@SORT": case "@BGM": case "@BGMOFS": case "@BGMPRV":
            case "@JACKET": case "@BGIMG": case "@BGMODE": case "@FLDCOL": case "@FLDIMG":
            case "@FLAG": case "@ATINFO": case "@DLURL": case "@COPYRIGHT": case "@LICENSE":
            case "@MAINTIL": case "@TIL":
                break;

            case "@SPDMOD":
            {
                var parts = value.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2
                    && TryParseUgcMeasureTick(parts[0], out var meas, out var tick)
                    && decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mult))
                {
                    var tpm = chart.TicksPerBeat * 4;
                    chart.SflList.Add((meas + new Rational(tick, tpm), Rational.Zero, mult));
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

    private static int ParseNoteLine(string[] lines, int idx, UgcChart chart, List<Alert> alerts)
    {
        var line = lines[idx];
        var lineNum = idx + 1;

        // skip comment lines and inline directives
        if (line.StartsWith('\'') || line.StartsWith('@'))
            return idx;

        // standalone follower line: silently skip (will be attached by parent or ignored)
        if (line.StartsWith('#') && !line.Contains(':') && (line.Contains(">s") || line.Contains(">c")))
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

        var tpm = chart.TicksPerBeat * 4;
        var note = new ChuNote
        {
            Time = measure + new Rational(tick, tpm),
        };

        var typeChar = char.ToLowerInvariant(code[0]);

        switch (typeChar)
        {
            case 't':
                ParseTapNote(code, note, alerts, lineNum, chart);
                break;

            case 'h':
                idx = ParseHoldNote(lines, idx, code, note, alerts, chart);
                break;

            case 's':
                idx = ParseSlideNote(lines, idx, code, note, alerts, chart);
                break;

            case 'a':
                ParseAirNote(code, note, alerts, lineNum, chart);
                break;

            case 'x':
                ParseChrNote(code, note, alerts, lineNum, chart);
                break;

            case 'f':
                note.Type = "FLK";
                ParseCellWidth(code, 1, note, alerts, lineNum, chart);
                if (code.Length > 3)
                    note.Tag = code[3..];
                break;

            case 'c':
                return idx; // Margrete Air Crush, silently skip

            case 'd':
                note.Type = "MNE";
                ParseCellWidth(code, 1, note, alerts, lineNum, chart);
                break;

            default:
                alerts.Add(new Alert(Warning, $"未知的音符类型前缀 '{typeChar}': {line}") { Line = lineNum });
                return idx;
        }

        chart.Notes.Add(note);
        return idx;
    }

    private static void ParseTapNote(string code, ChuNote note, List<Alert> alerts, int lineNum, UgcChart chart)
    {
        note.Type = "TAP";
        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
    }

    private static int ParseHoldNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, UgcChart chart)
    {
        note.Type = "HLD";
        var tpm = chart.TicksPerBeat * 4;
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);

        bool foundFirst = false;
        while (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var duration, out _, out _))
            {
                if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                break;
            }

            note.Duration += new Rational(duration, tpm);
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
            alerts.Add(new Alert(Warning, $"HLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = FormatNoteRef(note, chart) });
        return idx;
    }

    private static int ParseSlideNote(string[] lines, int idx, string code, ChuNote note, List<Alert> alerts, UgcChart chart)
    {
        note.Type = "SLD";
        var tpm = chart.TicksPerBeat * 4;
        ParseCellWidth(code, 1, note, alerts, idx + 1, chart);

        bool foundFirst = false;
        while (idx + 1 < lines.Length)
        {
            var nextLine = lines[idx + 1].Trim();
            if (!TryParseFollowerLine(nextLine, out var duration, out var endCell, out var endWidth))
            {
                if (nextLine.StartsWith('\'') || nextLine.StartsWith('@')) { idx++; continue; }
                break;
            }

            note.Duration += new Rational(duration, tpm);
            note.EndCell = endCell;
            note.EndWidth = endWidth;
            idx++;
            foundFirst = true;
        }

        if (!foundFirst)
            alerts.Add(new Alert(Warning, $"SLD 音符缺少时长跟随行") { Line = idx + 1, RelevantNote = FormatNoteRef(note, chart) });

        return idx;
    }

    private static bool TryParseStandaloneFollower(string[] lines, int idx, UgcChart chart, List<Alert> alerts)
    {
        var line = lines[idx];
        if (!line.StartsWith('#') || !line.Contains(">s") && !line.Contains(">c")) return false;

        if (!TryParseFollowerLine(line, out var duration, out var endCell, out var endWidth)) return false;

        // find the last SLD or HLD note and attach duration
        var tpm = chart.TicksPerBeat * 4;
        for (int i = chart.Notes.Count - 1; i >= 0; i--)
        {
            var n = chart.Notes[i];
            if (n.Type is "SLD" or "HLD")
            {
                if (n.Type == "SLD")
                {
                    n.Duration = new Rational(duration, tpm);
                    n.EndCell = endCell;
                    n.EndWidth = endWidth;
                }
                else n.Duration = new Rational(duration, tpm);
                return true;
            }
        }
        return false;
    }

    private static bool TryParseFollowerLine(string line, out int duration, out int endCell, out int endWidth, bool requireEndCellWidth = false)
    {
        duration = 0;
        endCell = 0;
        endWidth = 1;

        if (!line.StartsWith('#')) return false;

        // support both >s (SLD) and >c (SLC) follower lines
        int gtIdx = -1;
        int markerLen = 0;
        if (line.Contains(">s")) { gtIdx = line.IndexOf(">s"); markerLen = 2; }
        else if (line.Contains(">c")) { gtIdx = line.IndexOf(">c"); markerLen = 2; }
        if (gtIdx < 1) return false;

        var durationStr = line[1..gtIdx];
        if (!int.TryParse(durationStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out duration)) return false;

        var afterMarker = line[(gtIdx + markerLen)..];
        if (afterMarker.Length >= 2)
        {
            endCell = HexCharToInt(afterMarker[0]);
            endWidth = WidthHexCharToInt(afterMarker[1]);
        }
        else if (requireEndCellWidth) return false;

        return true;
    }

    private static void ParseCellWidth(string code, int startIdx, ChuNote note, List<Alert> alerts, int lineNum, UgcChart chart)
    {
        if (code.Length > startIdx)
        {
            note.Cell = HexCharToInt(code[startIdx]);
            if (code.Length > startIdx + 1)
                note.Width = WidthHexCharToInt(code[startIdx + 1]);
            else
                alerts.Add(new Alert(Warning, $"音符缺少 width: {code}") { Line = lineNum, RelevantNote = FormatNoteRef(note, chart) });
        }
        else
        {
            alerts.Add(new Alert(Warning, $"音符缺少 cell 和 width: {code}") { Line = lineNum, RelevantNote = FormatNoteRef(note, chart) });
        }
    }

    private static void ParseAirNote(string code, ChuNote note, List<Alert> alerts, int lineNum, UgcChart chart)
    {
        // Matches UgcGenerator: "a" + cell + width + two-letter direction + targetNote [ + "_" + airHoldDuration for AHD ]
        if (code.Length < 5)
        {
            alerts.Add(new Alert(Warning, $"AIR 音符代码过短: {code}") { Line = lineNum });
            note.Type = "AIR";
            return;
        }

        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        var afterCellWidth = code[3..];
        var underscoreIdx = afterCellWidth.IndexOf('_');
        var mainPart = underscoreIdx >= 0 ? afterCellWidth[..underscoreIdx] : afterCellWidth;

        if (mainPart.Length < 2)
        {
            alerts.Add(new Alert(Warning, $"AIR 音符方向代码过短: {code}") { Line = lineNum });
            note.Type = "AIR";
            return;
        }

        var dir = mainPart[..2];
        if (AirDirections.TryGetValue(dir, out var airType))
        {
            note.Type = airType;
        }
        else
        {
            note.Type = "AIR";
            alerts.Add(new Alert(Warning, $"未知的 AIR 方向: {dir}") { Line = lineNum, RelevantNote = FormatNoteRef(note, chart) });
        }

        note.TargetNote = mainPart.Length > 2 ? mainPart[2..] : "N";

        if (underscoreIdx >= 0 && note.Type == "AHD")
        {
            var durStr = afterCellWidth[(underscoreIdx + 1)..];
            if (int.TryParse(durStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ahdDuration))
                note.Duration = new Rational(ahdDuration, chart.TicksPerBeat * 4);
        }
    }

    private static void ParseChrNote(string code, ChuNote note, List<Alert> alerts, int lineNum, UgcChart chart)
    {
        note.Type = "CHR";
        if (code.Length < 3)
        {
            alerts.Add(new Alert(Warning, $"CHR 音符代码过短: {code}") { Line = lineNum });
            return;
        }

        ParseCellWidth(code, 1, note, alerts, lineNum, chart);
        var extraRaw = code.Length > 3 ? code[3..] : "";
        if (ChrExtras.TryGetValue(extraRaw, out var chrDir))
            note.Tag = chrDir;
        else
            note.Tag = extraRaw;
    }

    private static int HexCharToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => 0,
        };
    }

    private static int WidthHexCharToInt(char c)
    {
        return c switch
        {
            >= '1' and <= '9' => c - '1' + 1,
            >= 'A' and <= 'G' => c - 'A' + 10,
            >= 'a' and <= 'g' => c - 'a' + 10,
            _ => 1,
        };
    }

    private static string FormatNoteRef(ChuNote note, UgcChart chart)
    {
        var tpm = chart.TicksPerBeat * 4;
        var (m, o) = Utils.BarAndTick(note.Time, tpm, 0);
        return $"#{m}'{o}:{note.Type}";
    }
}

