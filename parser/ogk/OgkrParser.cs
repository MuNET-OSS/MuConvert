using System.Globalization;
using MuConvert.chart;
using MuConvert.parser;
using MuConvert.utils;
using Rationals;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.ogk;

public class OgkrParser: IParser<OgkChart>
{
    private readonly OgkChart chart = new();
    private readonly List<Alert> alerts = [];

    private int RSL = 1920;  // TickResolution，每个tUnit中的tGrid细分数
    private int RSL_X = 4096;  // XResolution，每个xUnit中的xGrid细分数

    // 读取到 BPM_DEF / MET_DEF 时，把读到的值存储下来。因为最后会需要在BpmList/MetList的开头，补充上0时刻数据。
    private decimal _bpmDef = 60m;
    private (int Numerator, int Denominator) _metDef = (4, 4);

    // 当前正在处理的段落
    private string _section = "";

    // ogkr中，有许多类指令，其中是包含ID引用的。这里在解析的过程中把这些“字符串ID与对象的对应关系”都缓存下来。
    private readonly Dictionary<string, BulletPallete> _palette = new();
    private readonly Dictionary<string, Lane> _laneById = new();
    private readonly Dictionary<string, EnemyMovement> _enemyMvmtById = new();
    private readonly Dictionary<string, Beam> _beamById = new();

    public (OgkChart, List<Alert>) Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 段落头
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                _section = line[1..^1];
                continue;
            }

            var parts = line.Split('\t');
            try
            {
                switch (_section)
                {
                    case "HEADER": ParseHeader(parts, i + 1); break;
                    case "B_PALETTE": ParseBPalette(parts, i + 1); break;
                    case "COMPOSITION": ParseComposition(parts, i + 1); break;
                    case "LANE": ParseLane(parts, i + 1); break;
                    case "LANE_BLOCK": ParseLaneBlock(parts, i + 1); break;
                    case "BULLET": ParseBullet(parts, i + 1); break;
                    case "BEAM": ParseBeam(parts, i + 1); break;
                    case "BELL": ParseBell(parts, i + 1); break;
                    case "FLICK": ParseFlick(parts, i + 1); break;
                    case "NOTES": ParseNote(parts, i + 1); break;
                    case "TOTAL": break; // 忽略
                    default:
                        alerts.Add(new Alert(Warning,
                            $"未知段落 [{_section}] 中的指令，已忽略", line: i + 1, relevantNote: line));
                        break;
                }
            }
            catch (FormatException ex)
            {
                alerts.Add(new Alert(Error, $"无法解析指令: {ex.Message}", line: i + 1, relevantNote: line));
                throw new ConversionException(alerts, ex);
            }
        }

        // 谱面开头若没有BPM声明，则用BPM_DEF的首项补一个时刻0的BPM
        if (chart.BpmList.Count == 0 || chart.BpmList[0].Time != 0)
        {
            chart.BpmList.Insert(0, new BPM(0, _bpmDef));
        }
        // 同理MetList
        if (chart.MetList.Count == 0 || chart.MetList[0].Time != 0)
        {
            chart.MetList.Insert(0, new MET(0, _metDef.Numerator, _metDef.Denominator));
        }

        chart.Sort();
        return (chart, alerts);
    }
    
    private Rational ToBar(int tUnit, int tGrid) =>
        (tUnit + new Rational(tGrid, RSL)).CanonicalForm;
    
    private Rational ToPos(int xUnit, int xGrid) => 
        (xUnit + new Rational(xGrid, RSL_X)).CanonicalForm;
    

    private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    private static decimal ParseDec(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
    
    private void ParseHeader(string[] p, int lineNo)
    {
        var tag = p[0].ToUpperInvariant();
        switch (tag)
        {
            case "VERSION":
                // 非1.7.0时给出警告
                if (!(p.Length >= 3 && p[1] == "1" && p[2] == "7"))
                {
                    alerts.Add(new Alert(Warning, $"ogkr的版本不是1.7.0（实际为{string.Join('.', p.Skip(1))}）。对此的支持是实验性的",
                        line: lineNo, relevantNote: string.Join('\t', p)));
                }
                break;
            case "CREATOR":
                chart.Designer = p.Length > 1 ? p[1] : "";
                break;
            case "BPM_DEF":
                if (p.Length > 1) _bpmDef = ParseDec(p[1]);
                break;
            case "MET_DEF":
                if (p.Length >= 3) _metDef = (ParseInt(p[1]), ParseInt(p[2]));
                break;
            case "TRESOLUTION":
                if (p.Length > 1) RSL = ParseInt(p[1]);
                break;
            case "XRESOLUTION":
                if (p.Length > 1) RSL_X = ParseInt(p[1]);
                break;
            case "CLK_DEF":
                if (p.Length > 1) chart.ClockCount = ParseInt(p[1]) / (RSL / 4);
                break;
            case "PROGJUDGE_BPM":
                if (p.Length > 1) chart.ProgJudgeBpm = ParseDec(p[1]);
                break;
            case "TUTORIAL": // 忽略
                break;
            case "BULLET_DAMAGE":
                if (p.Length > 1) chart.BulletDamages[BulletDamage.NML] = ParseDec(p[1]);
                break;
            case "HARDBULLET_DAMAGE":
                if (p.Length > 1) chart.BulletDamages[BulletDamage.STR] = ParseDec(p[1]);
                break;
            case "DANGERBULLET_DAMAGE":
                if (p.Length > 1) chart.BulletDamages[BulletDamage.DNG] = ParseDec(p[1]);
                break;
            case "BEAM_DAMAGE":
                if (p.Length > 1) chart.BeamDamage = ParseDec(p[1]);
                break;
            default:
                if (tag.StartsWith("T_"))
                { // T_TOTAL/T_TAP/T_HOLD/...都是统计量，IR里不存
                    break;
                }
                alerts.Add(new Alert(Warning, $"HEADER段落中未知指令 '{tag}'", line: lineNo, relevantNote: string.Join('\t', p)));
                break;
        }
    }

    private void ParseBPalette(string[] p, int lineNo)
    {
        if (p[0] != "BPL")
        {
            alerts.Add(new Alert(Warning, $"B_PALETTE段落中未知指令 '{p[0]}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // BPL strID Shooter placeOffset target speed size type randDiffPos
        if (p.Length < 9)
        {
            alerts.Add(new Alert(Warning, $"BPL指令列数不足（需要9列）", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        var strId = p[1];
        var pallete = new BulletPallete(
            Shooter: Enum.Parse<BulletShooter>(p[2]),
            TargetOffset: ParseInt(p[3]),
            TargetToPlayer: p[4] switch
            {
                "PLR" => true,
                "FIX" => false,
                _ => throw new FormatException($"unknown bullet Target: {p[4]}"),
            },
            Speed: ParseDec(p[5]),
            Size: Enum.Parse<BulletSize>(p[6]),
            Type: Enum.Parse<BulletType>(p[7]),
            RandomOffsetDist: ParseInt(p[8])
        );

        if (_palette.ContainsKey(strId))
            alerts.Add(new Alert(Warning, $"BPL中strID '{strId}'重复出现，将以最后一次为准", line: lineNo, relevantNote: string.Join('\t', p)));
        _palette[strId] = pallete;
    }

    private void ParseComposition(string[] p, int lineNo)
    {
        var cmd = p[0];
        switch (cmd)
        {
            case "BPM":
                // BPM tUnit tGrid bpm
                chart.BpmList.Add(new BPM(ToBar(ParseInt(p[1]), ParseInt(p[2])), ParseDec(p[3])));
                break;
            case "MET":
                // MET tUnit tGrid numerator denominator
                chart.MetList.Add(new MET(ToBar(ParseInt(p[1]), ParseInt(p[2])), ParseInt(p[3]), ParseInt(p[4])));
                break;
            case "SFL":
                // SFL tUnit tGrid tGridLength speed
                {
                    var time = ToBar(ParseInt(p[1]), ParseInt(p[2]));
                    var duration = new Rational(ParseInt(p[3]), RSL);
                    var speed = ParseDec(p[4]);
                    chart.SflList.Add((time, duration, speed));
                }
                break;
            case "CLK":
                // CLK tUnit tGrid
                chart.ExplicitClocks ??= [];
                chart.ExplicitClocks.Add(ToBar(ParseInt(p[1]), ParseInt(p[2])));
                break;
            case "EST":
                // EST tUnit tGrid tag
                chart.EnemyList.Add((ToBar(ParseInt(p[1]), ParseInt(p[2])), p[3]));
                break;
            case "ISF":
                // ISF用于钦定指定区域内物件的变速组。IR中目前对此尚不支持，仅给一个info告知。
                alerts.Add(new Alert(Info, "当前版本尚未支持对ISF指令的解析，已忽略此行", line: lineNo, relevantNote: string.Join('\t', p)));
                break;
            default:
                alerts.Add(new Alert(Warning, $"COMPOSITION段落中未知指令 '{cmd}'", line: lineNo, relevantNote: string.Join('\t', p)));
                break;
        }
    }

    private void ParseLane(string[] p, int lineNo)
    {
        var cmd = p[0];
        if (cmd is "WLS" or "WRS" or "LLS" or "LCS" or "LRS" or "CLS" or "ENS")
        {
            StartLane(cmd, p, lineNo);
        }
        else if (cmd is "WLN" or "WRN" or "LLN" or "LCN" or "LRN" or "CLN" or "ENN")
        {
            ExtendLane(cmd, p, lineNo, isEnd: false);
        }
        else if (cmd is "WLE" or "WRE" or "LLE" or "LCE" or "LRE" or "CLE" or "ENE")
        {
            ExtendLane(cmd, p, lineNo, isEnd: true);
        }
        else
        {
            alerts.Add(new Alert(Warning, $"LANE段落中未知指令 '{cmd}'", line: lineNo, relevantNote: string.Join('\t', p)));
        }
    }

    private void StartLane(string cmd, string[] p, int lineNo)
    {
        var groupId = p[1];
        var time = ToBar(ParseInt(p[2]), ParseInt(p[3]));
        var xUnit = ParseInt(p[4]);

        if (cmd == "ENS")
        {
            var em = new EnemyMovement();
            em.Points.Add(new OgkLanePoint(time, xUnit));
            _enemyMvmtById[groupId] = em;
            chart.EnemyMovements.Add(em);
            return;
        }

        Lane lane;
        switch (cmd)
        {
            case "WLS":
                lane = new Wall { Direction = Direction.L };
                break;
            case "WRS":
                lane = new Wall { Direction = Direction.R };
                break;
            case "LLS":
                lane = new Lane(LaneType.Red);
                break;
            case "LCS":
                lane = new Lane(LaneType.Green);
                break;
            case "LRS":
                lane = new Lane(LaneType.Blue);
                break;
            case "CLS":
            {
                // CLS GroupId tUnit tGrid xUnit colorId brightnessId [isTransparent]
                var colorId = ParseInt(p[5]);
                var brightness = p.Length > 6 ? ParseInt(p[6]) : 2;
                lane = new ColorfulLane { Color = (ColorfulLaneColor)colorId, Brightness = brightness };
                break;
            }
            default:
                throw new FormatException($"Unexpected lane Start command: {cmd}");
        }

        // isTransparent字段，位置依Lane类型而异：
        // - WLS/WRS/LLS/LCS/LRS：第6个字段(p[5])
        // - CLS：第8个字段(p[7])
        var transparentIdx = cmd == "CLS" ? 7 : 5;
        if (transparentIdx < p.Length && ParseInt(p[transparentIdx]) > 0) lane.IsTransparent = true;

        lane.Points.Add(new OgkLanePoint(time, xUnit));
        _laneById[groupId] = lane;
        chart.Lanes.Add(lane);
    }

    private void ExtendLane(string cmd, string[] p, int lineNo, bool isEnd)
    {
        var groupId = p[1];
        var time = ToBar(ParseInt(p[2]), ParseInt(p[3]));
        var xUnit = ParseInt(p[4]);

        if (cmd is "ENN" or "ENE")
        {
            if (!_enemyMvmtById.TryGetValue(groupId, out var em))
            {
                alerts.Add(new Alert(Warning, $"{cmd}引用了不存在的ID={groupId}", line: lineNo, relevantNote: string.Join('\t', p)));
                return;
            }
            em.Points.Add(new OgkLanePoint(time, xUnit));
            if (isEnd) _enemyMvmtById.Remove(groupId);
            return;
        }

        if (!_laneById.TryGetValue(groupId, out var lane))
        {
            alerts.Add(new Alert(Warning, $"{cmd}引用了不存在的ID={groupId}", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }
        lane.Points.Add(new OgkLanePoint(time, xUnit));
        // 注意：lane结束后不要从_laneById中移除！
        // 因为后续NOTES段中TAP/HLD等会通过此groupId引用lane来设置音符的Lane属性，必须保证查找仍然有效。
    }

    private void ParseLaneBlock(string[] p, int lineNo)
    {
        if (p[0] != "LBK")
        {
            alerts.Add(new Alert(Warning, $"LANE_BLOCK段落中未知指令 '{p[0]}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // LBK GroupId Fore.tUnit Fore.tGrid Fore.xUnit Fore.xGrid Rear.tUnit Rear.tGrid Rear.xUnit Rear.xGrid
        var groupId = p[1];
        var foreTime = ToBar(ParseInt(p[2]), ParseInt(p[3]));
        var forePos = ToPos(ParseInt(p[4]), ParseInt(p[5]));
        var rearTime = ToBar(ParseInt(p[6]), ParseInt(p[7]));
        var rearPos = ToPos(ParseInt(p[8]), ParseInt(p[9]));

        if (!_laneById.TryGetValue(groupId, out var lane) || lane is not Wall wall)
        {
            alerts.Add(new Alert(Warning, $"LBK指令未能引用有效的Wall(groupId={groupId})。已忽略此行。", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // Block.Start是相对于Wall.Time的，不是绝对时间！详见Block函数上的注释。
        var block = new Block(foreTime - wall.Time, rearTime - foreTime, forePos, rearPos);
        wall.Blocks.Add(block);
    }

    private void ParseBullet(string[] p, int lineNo)
    {
        if (p[0] != "BLT")
        {
            alerts.Add(new Alert(Warning, $"BULLET段落中未知指令 '{p[0]}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // BLT strId tUnit tGrid xUnit BulletType
        var bplId = p[1];
        if (!_palette.TryGetValue(bplId, out var pallete))
        {
            alerts.Add(new Alert(Warning, $"BLT引用了不存在的BPL配置(id={bplId})。将使用默认配置（原地静止不动的普通子弹）。", line: lineNo, relevantNote: string.Join('\t', p)));
            pallete = new BulletPallete();
        }

        var bullet = new Bullet
        {
            Detail = pallete,
            Time = ToBar(ParseInt(p[2]), ParseInt(p[3])),
            Pos = ParseInt(p[4]),
            Damage = Enum.Parse<BulletDamage>(p[5]),
        };
        chart.Bullets.Add(bullet);
    }

    private void ParseBeam(string[] p, int lineNo)
    {
        var cmd = p[0];
        // BMS/BMN/BME: recordId tUnit tGrid xUnit widthID
        // OBS/OBN/OBE: recordId tUnit tGrid xUnit widthID shootPosXUnitOffset
        if (cmd is not ("BMS" or "BMN" or "BME" or "OBS" or "OBN" or "OBE"))
        {
            alerts.Add(new Alert(Warning, $"BEAM段落中未知指令 '{cmd}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        var recordId = p[1];
        var time = ToBar(ParseInt(p[2]), ParseInt(p[3]));
        var xUnit = ParseInt(p[4]);
        var widthId = ParseInt(p[5]);

        var isStart = cmd is "BMS" or "OBS";
        var isEnd = cmd is "BME" or "OBE";

        Beam beam;
        if (isStart)
        {
            beam = new Beam();
            if (cmd == "OBS" && p.Length > 6)
            {
                // 仅Start命令中的shootPosXUnitOffset是有用的
                beam.ObliqueOffset = ParseInt(p[6]);
            }
            _beamById[recordId] = beam;
            chart.Bullets.Add(beam);
        }
        else
        {
            if (!_beamById.TryGetValue(recordId, out var existing))
            {
                alerts.Add(new Alert(Warning, $"{cmd}引用了不存在的ID={recordId}", line: lineNo, relevantNote: string.Join('\t', p)));
                return;
            }
            beam = existing;
        }

        beam.Points.Add(new BeamPoint(time, xUnit, widthId));
        if (isEnd) _beamById.Remove(recordId);
    }

    private void ParseBell(string[] p, int lineNo)
    {
        if (p[0] != "BEL")
        {
            alerts.Add(new Alert(Warning, $"BELL段落中未知指令 '{p[0]}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // BEL tUnit tGrid xUnit bulletPallete
        var bplId = p.Length > 4 ? p[4] : "--";
        BulletPallete? detail = null;
        if (bplId != "--")
        {
            if (!_palette.TryGetValue(bplId, out var pal))
                alerts.Add(new Alert(Warning, $"BEL引用了不存在的BPL配置(id={bplId})。已视为没有特殊配置，即原地静止不动的普通bell。", line: lineNo, relevantNote: string.Join('\t', p)));
            else detail = pal;
        }

        var bell = new Bell
        {
            Time = ToBar(ParseInt(p[1]), ParseInt(p[2])),
            Pos = ParseInt(p[3]),
            Detail = detail,
        };
        chart.Notes.Add(bell);
    }

    private void ParseFlick(string[] p, int lineNo)
    {
        var cmd = p[0];
        if (cmd is not ("FLK" or "CFK"))
        {
            alerts.Add(new Alert(Warning, $"FLICK段落中未知指令 '{cmd}'", line: lineNo, relevantNote: string.Join('\t', p)));
            return;
        }

        // FLK/CFK tUnit tGrid xUnit Direction
        var flick = new Flick
        {
            Time = ToBar(ParseInt(p[1]), ParseInt(p[2])),
            Pos = ParseInt(p[3]),
            Direction = Enum.Parse<Direction>(p[4]),
            IsEx = cmd == "CFK",
        };
        chart.Notes.Add(flick);
    }

    private void ParseNote(string[] p, int lineNo)
    {
        var cmd = p[0];
        var laneId = p[1];
        switch (cmd)
        {
            case "TAP": case "CTP": case "XTP":
            {
                // TAP laneGroupId tUnit tGrid xUnit xGrid
                if (!_laneById.TryGetValue(laneId, out var lane))
                {
                    alerts.Add(new Alert(Warning, $"{cmd}引用了不存在的ID={laneId}", line: lineNo, relevantNote: string.Join('\t', p)));
                    return;
                }
                var tap = new Tap
                {
                    Lane = lane,
                    Time = ToBar(ParseInt(p[2]), ParseInt(p[3])),
                    Pos = ToPos(ParseInt(p[4]), ParseInt(p[5])),
                    IsEx = cmd != "TAP",
                };
                chart.Notes.Add(tap);
                break;
            }
            case "HLD": case "CHD": case "XHD":
            {
                // HLD laneGroupId Fore.tUnit Fore.tGrid Fore.xUnit Fore.xGrid Rear.tUnit Rear.tGrid Rear.xUnit Rear.xGrid
                if (!_laneById.TryGetValue(laneId, out var lane))
                {
                    alerts.Add(new Alert(Warning, $"{cmd}引用了不存在的ID={laneId}", line: lineNo, relevantNote: string.Join('\t', p)));
                    return;
                }

                var hold = new Hold
                {
                    Lane = lane,
                    Time = ToBar(ParseInt(p[2]), ParseInt(p[3])),
                    Pos = ToPos(ParseInt(p[4]), ParseInt(p[5])),
                    EndTime = ToBar(ParseInt(p[6]), ParseInt(p[7])),
                    EndPos = ToPos(ParseInt(p[8]), ParseInt(p[9])),
                    IsEx = cmd != "HLD",
                };
                chart.Notes.Add(hold);
                break;
            }
            default:
                alerts.Add(new Alert(Warning, $"NOTES段落中未知指令 '{cmd}'", line: lineNo, relevantNote: string.Join('\t', p)));
                break;
        }
    }
}
