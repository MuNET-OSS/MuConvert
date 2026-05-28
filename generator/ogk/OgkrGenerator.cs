using System.Text;
using MuConvert.generator;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.ogk;

public class OgkrGenerator: IGenerator<OgkChart>
{
    // 除非你知道你在做什么，不然以下两个变量请勿修改！
    public int RSL = 1920;
    public int RSL_X = 4096;

    private List<Alert> alerts = [];
    public (string, List<Alert>) Generate(OgkChart chart)
    {
        chart.Sort();

        // 各类“需要分配ID”的对象，这里集中排序、ID分配好。
        var orderedLanes = SortLanesByTypeGroup(chart.Lanes);
        var generalLanes = AssignGroupIds(orderedLanes, chart.EnemyMovements); // EnemyMovements已经在chart.Sort()里排好序了，无需再排
        var paletteIds = AssignPaletteIds(chart);

        var sb = new StringBuilder();
        sb.AppendLine();

        EmitHeader(sb, chart);
        sb.AppendLine();
        sb.AppendLine();

        EmitBPalette(sb, paletteIds);
        sb.AppendLine();
        sb.AppendLine();

        EmitComposition(sb, chart);
        sb.AppendLine();
        sb.AppendLine();

        EmitLane(sb, generalLanes);
        sb.AppendLine();

        EmitLaneBlock(sb, generalLanes);
        sb.AppendLine();
        sb.AppendLine();

        EmitBullet(sb, chart, paletteIds);
        sb.AppendLine();
        sb.AppendLine();

        EmitBeam(sb, chart);
        sb.AppendLine();
        sb.AppendLine();

        EmitBell(sb, chart, paletteIds);
        sb.AppendLine();
        sb.AppendLine();

        EmitFlick(sb, chart);
        sb.AppendLine();
        sb.AppendLine();

        EmitNotes(sb, chart, generalLanes);
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("[TOTAL]");
        sb.AppendLine();

        return (sb.ToString(), alerts);
    }

    // 遵循官谱的顺序：首先按照Lane的类型（左右墙、红绿蓝线、彩色线），再按时间顺序
    private static List<Lane> SortLanesByTypeGroup(List<Lane> lanes)
    {
        int Order(Lane l) => l switch
        {
            Wall w when w.Direction == Direction.L => 0,
            Wall w when w.Direction == Direction.R => 1,
            _ => l.Type switch
            {
                LaneType.Red => 2,
                LaneType.Green => 3,
                LaneType.Blue => 4,
                LaneType.Colorful => 5,
                _ => 99,
            }
        };
        // 输入的lanes已经被chart.Sort()按Time升序排好序了，这里只需再做一次按类型的稳定排序即可。
        return lanes.OrderBy(Order).ToList();
    }

    private static Dictionary<OgkBaseLane<OgkLanePoint>, int> AssignGroupIds(List<Lane> lanes, List<EnemyMovement> enemies)
    {
        var ids = new Dictionary<OgkBaseLane<OgkLanePoint>, int>();
        var c = 0;
        foreach (var l in lanes) ids[l] = c++;
        foreach (var e in enemies) ids[e] = c++;
        return ids;
    }

    // 给所有BulletPallete分配字符串ID。按其在Bullets/Bells中首次出现的顺序分配，同时去重（按record的结构相等性）。
    private static Dictionary<BulletPallete, string> AssignPaletteIds(OgkChart chart)
    {
        var ids = new Dictionary<BulletPallete, string>();
        var counter = 360; // 起始值为 A0 = 10 * 36
        void Add(BulletPallete? p)
        {
            if (p == null) return;
            if (!ids.ContainsKey(p)) ids[p] = Utils.IToH(counter++, 36);
        }

        foreach (var bul in chart.Bullets) Add(bul.Detail);
        foreach (var note in chart.Notes)
            if (note is IBullet bul) Add(bul.Detail);
        return ids;
    }

    private void EmitHeader(StringBuilder sb, OgkChart chart)
    {
        sb.AppendLine("[HEADER]");
        sb.AppendLine("VERSION\t1\t7\t0");
        sb.AppendLine($"CREATOR\t{chart.Designer}");
        var (first, common, max, min) = chart.BpmList.BPM_DEF();
        sb.AppendLine(FormattableString.Invariant($"BPM_DEF\t{first:F3}\t{common:F3}\t{max:F3}\t{min:F3}"));
        var firstMet = chart.MetList[0];
        sb.AppendLine($"MET_DEF\t{firstMet.Numerator}\t{firstMet.Denominator}");
        sb.AppendLine($"TRESOLUTION\t{RSL}");
        sb.AppendLine($"XRESOLUTION\t{RSL_X}");
        sb.AppendLine($"CLK_DEF\t{chart.ClockCount * (RSL / 4)}");
        sb.AppendLine(FormattableString.Invariant($"PROGJUDGE_BPM\t{chart.ProgJudgeBpm:F3}"));
        sb.AppendLine("TUTORIAL\t0");
        sb.AppendLine(FormattableString.Invariant($"BULLET_DAMAGE\t{chart.BulletDamages[BulletDamage.NML]:F3}"));
        sb.AppendLine(FormattableString.Invariant($"HARDBULLET_DAMAGE\t{chart.BulletDamages[BulletDamage.STR]:F3}"));
        sb.AppendLine(FormattableString.Invariant($"DANGERBULLET_DAMAGE\t{chart.BulletDamages[BulletDamage.DNG]:F3}"));
        sb.AppendLine(FormattableString.Invariant($"BEAM_DAMAGE\t{chart.BeamDamage:F3}"));

        var counts = chart.CountNotes();
        sb.AppendLine($"T_TOTAL\t{counts["T_TOTAL"]}");
        sb.AppendLine($"T_TAP\t{counts["T_TAP"]}");
        sb.AppendLine($"T_HOLD\t{counts["T_HOLD"]}");
        sb.AppendLine($"T_SIDE\t{counts["T_SIDE"]}");
        sb.AppendLine($"T_SHOLD\t{counts["T_SHOLD"]}");
        sb.AppendLine($"T_FLICK\t{counts["T_FLICK"]}");
        sb.AppendLine($"T_BELL\t{counts["T_BELL"]}");
    }

    private void EmitBPalette(StringBuilder sb, Dictionary<BulletPallete, string> palettes)
    {
        sb.AppendLine("[B_PALETTE]");
        foreach (var (palette, id) in palettes)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"BPL\t{id}\t{palette.Shooter}\t{palette.TargetOffset}\t{(palette.TargetToPlayer ? "PLR" : "FIX")}\t{palette.Speed:F6}\t{palette.Size}\t{palette.Type}\t{palette.RandomOffsetDist}"));
        }
    }

    private void EmitComposition(StringBuilder sb, OgkChart chart)
    {
        sb.AppendLine("[COMPOSITION]");

        // BPM：跳过首项（隐含于HEADER的BPM_DEF）
        foreach (var b in chart.BpmList.Skip(1))
        {
            var (t, g) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine(FormattableString.Invariant($"BPM\t{t}\t{g}\t{b.Bpm:F3}"));
        }

        // MET：跳过首项（隐含于HEADER的MET_DEF）。
        // ogkr中无论MET_DEF还是MET，字段顺序都是Numerator在前、Denominator在后，与maimai刚好相反
        foreach (var m in chart.MetList.Skip(1))
        {
            var (t, g) = Utils.BarAndTick(m.Time, RSL);
            sb.AppendLine($"MET\t{t}\t{g}\t{m.Numerator}\t{m.Denominator}");
        }

        // SFL
        foreach (var (time, duration, mult) in chart.SflList)
        {
            var (t, g) = Utils.BarAndTick(time, RSL);
            var durTicks = Utils.Tick(duration, RSL);
            sb.AppendLine(FormattableString.Invariant($"SFL\t{t}\t{g}\t{durTicks}\t{mult:F6}"));
        }

        // CLK（仅当显式声明时输出）
        if (chart.ExplicitClocks != null)
        {
            foreach (var time in chart.ExplicitClocks)
            {
                var (t, g) = Utils.BarAndTick(time, RSL);
                sb.AppendLine($"CLK\t{t}\t{g}");
            }
        }

        // EST
        foreach (var (time, tag) in chart.EnemyList)
        {
            var (t, g) = Utils.BarAndTick(time, RSL);
            sb.AppendLine($"EST\t{t}\t{g}\t{tag}");
        }
    }

    private void EmitOneLane<T>(StringBuilder sb, OgkBaseLane<T> lane, int id) where T: OgkLanePoint
    {
        var cmdRoot = lane switch
        {
            Wall { Direction: Direction.L } => "WL",
            Wall { Direction: Direction.R } => "WR",
            ColorfulLane => "CL",
            EnemyMovement => "EN",
            Lane { Type: LaneType.Red } => "LL",
            Lane { Type: LaneType.Green } => "LC",
            Lane { Type: LaneType.Blue } => "LR",
            Beam { ObliqueOffset: 0 } => "BM",
            Beam b when b.ObliqueOffset != 0  => "OB",
            _ => throw new InvalidOperationException($"Unsupported lane {lane} for emission"),
        };

        var n = lane.Points.Count;
        for (var i = 0; i < n; i++)
        {
            var cmd = cmdRoot + (i == 0 ? "S" : i == n - 1 ? "E" : "N");
            var p = lane.Points[i];
            var (t, g) = Utils.BarAndTick(p.Time, RSL);
            var result = $"{cmd}\t{id}\t{t}\t{g}\t{p.Pos}";

            if (lane is ColorfulLane cl)
            { // 对ColorfulLane，加上colorId和brightnessId；
                result += $"\t{(int)cl.Color}\t{cl.Brightness}";
            }
            if (i == 0 && lane is Lane { IsTransparent: true })
            { // 最后再加上表示transparent的1
                result += "\t1";
            }
            if (lane is Beam beam && p is BeamPoint pp)
            { // 对Beam类型，添加上特有的属性
                result += $"\t{pp.Width}";
                if (beam.ObliqueOffset != 0) result += $"\t{beam.ObliqueOffset}"; // 对OBS/OBN/OBE，所有行都要带上shootPosXUnitOffset字段（虽然仅OBS的值有意义）。
            }
            sb.AppendLine(result);
        }
        sb.AppendLine();
    }

    private void EmitLane(StringBuilder sb, Dictionary<OgkBaseLane<OgkLanePoint>, int> lanes)
    {
        sb.AppendLine("[LANE]");
        foreach (var (lane, id) in lanes) 
            EmitOneLane(sb, lane, id);
    }

    private void EmitLaneBlock(StringBuilder sb, Dictionary<OgkBaseLane<OgkLanePoint>, int> ids)
    {
        sb.AppendLine("[LANE_BLOCK]");

        // 收集所有Wall上的Blocks，按“绝对的Fore时刻”排序后输出。
        // 同一Fore时刻有多个block时，右墙的block在前（与官谱观察到的惯例一致）。
        var entries = ids.Keys.OfType<Wall>()
            .SelectMany(w => w.Blocks.Select(b => (Wall: w, Block: b, ForeTime: w.Time + b.Start)))
            .OrderBy(x => x.ForeTime)
            .ThenBy(x => x.Wall.Direction == Direction.R ? 0 : 1)
            .ToList();

        foreach (var (wall, block, foreTime) in entries)
        {
            var rearTime = foreTime + block.Duration;
            var (fT, fG) = Utils.BarAndTick(foreTime, RSL);
            var (rT, rG) = Utils.BarAndTick(rearTime, RSL);
            var (fXU, fXG) = Utils.BarAndTick(block.Pos, RSL_X);
            var (rXU, rXG) = Utils.BarAndTick(block.EndPos, RSL_X);
            sb.AppendLine($"LBK\t{ids[wall]}\t{fT}\t{fG}\t{fXU}\t{fXG}\t{rT}\t{rG}\t{rXU}\t{rXG}");
        }
    }

    private void EmitBullet(StringBuilder sb, OgkChart chart, Dictionary<BulletPallete, string> palettes)
    {
        sb.AppendLine("[BULLET]");
        foreach (var bullet in chart.Bullets.OfType<Bullet>())
        {
            var (t, g) = Utils.BarAndTick(bullet.Time, RSL);
            sb.AppendLine($"BLT\t{palettes[bullet.Detail]}\t{t}\t{g}\t{bullet.Pos}\t{bullet.Damage}");
        }
    }

    private void EmitBeam(StringBuilder sb, OgkChart chart)
    {
        sb.AppendLine("[BEAM]");
        var recordId = 0;
        foreach (var beam in chart.Bullets.OfType<Beam>())
            EmitOneLane(sb, beam, recordId++);
    }

    private void EmitBell(StringBuilder sb, OgkChart chart, Dictionary<BulletPallete, string> palettes)
    {
        sb.AppendLine("[BELL]");
        foreach (var bell in chart.Notes.OfType<Bell>())
        {
            var (t, g) = Utils.BarAndTick(bell.Time, RSL);
            var palId = bell.Detail != null ? palettes[bell.Detail] : "--";
            sb.AppendLine($"BEL\t{t}\t{g}\t{bell.Pos.Round()}\t{palId}");
        }
    }

    private void EmitFlick(StringBuilder sb, OgkChart chart)
    {
        sb.AppendLine("[FLICK]");
        foreach (var flick in chart.Notes.OfType<Flick>())
        {
            var (t, g) = Utils.BarAndTick(flick.Time, RSL);
            var cmd = flick.IsEx ? "CFK" : "FLK";
            sb.AppendLine($"{cmd}\t{t}\t{g}\t{flick.Pos.Round()}\t{flick.Direction}");
        }
    }

    private void EmitNotes(StringBuilder sb, OgkChart chart, Dictionary<OgkBaseLane<OgkLanePoint>, int> laneIds)
    {
        sb.AppendLine("[NOTES]");
        foreach (var tap in chart.Notes.OfType<Tap>())
        {
            if (!laneIds.TryGetValue(tap.Lane, out var laneId))
            {
                alerts.Add(new Alert(Warning, "音符所引用的Lane未在LANE段中登记", (chart, tap.Time)));
                continue;
            }
            var cmd = tap.IsEx ? "CTP" : "TAP";
            var (t, g) = Utils.BarAndTick(tap.Time, RSL);
            var (xU, xG) = Utils.BarAndTick(tap.Pos, RSL_X);
            var paramStr = $"{laneId}\t{t}\t{g}\t{xU}\t{xG}";
            if (tap is Hold hold)
            {
                cmd = hold.IsEx ? "CHD" : "HLD";
                var (rT, rG) = Utils.BarAndTick(hold.EndTime, RSL);
                var (rXU, rXG) = Utils.BarAndTick(hold.EndPos, RSL_X);
                paramStr += $"\t{rT}\t{rG}\t{rXU}\t{rXG}";
            }
            sb.AppendLine($"{cmd}\t{paramStr}");
        }
    }
}
