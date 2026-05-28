using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;

public class OgkChart: BaseChart<OgkNote>
{
    public string Designer { get; set; } = ""; // 谱师

    public decimal ProgJudgeBpm = 240m; // 用于给Hold生成中间判定点的BPM值，绝大多数情况下不需要手动改动。

    // 全局声明的“子弹伤害类型与伤害数值的映射关系”。绝大多数情况下都是这个默认值，不需要做改动。
    public Dictionary<BulletDamage, decimal> BulletDamages = new() {
        [BulletDamage.NML] = 1.0m, [BulletDamage.STR] = 2.0m, [BulletDamage.DNG] = 4.0m,
    };
    public decimal BeamDamage = 2.0m;
    
    /**
     * 游戏中所有的轨道。
     * 包括：红绿蓝线、左墙右墙（也就是侧键的轨道）、以及部分谱面中会出现的彩色线
     */
    public List<Lane> Lanes = [];

    /**
     * 游戏中所有的伤害弹。
     * 目前包括子弹(Bullet)和激光(Beam)两种子类型。
     */
    public List<IBullet> Bullets = [];

    /**
     * 指定切换敌人的时刻。
     *
     * 可以不显式指定（保持默认的空数组状态），此时游戏会自动应用“前半小怪、后半boss”的机制。
     * Type是一个字符串，游戏内的典型取值："WAVE1" "WAVE2" "BOSS"
     */
    public List<(Rational Time, string Type)> EnemyList = [];
    
    // 部分谱面中会有，上方的敌人在上面进行水平方向的移动的情况。
    public List<EnemyMovement> EnemyMovements = [];

    public override decimal StartTime => Math.Min(base.StartTime, (decimal)ToSecond(Bullets.First().Time));
    public override decimal EndTime => Math.Max(base.EndTime, (decimal)ToSecond(Bullets.Last().Time));
    public override int TotalNotes => CountNotes()["T_TOTAL"];

    /**
     * 基于谱面IR，计算ogkr中T_xxx的几个统计量（即谱面的"物量"信息）。
     *
     * 返回的字典将包含以下七个key：
     * - T_TAP：非侧键Tap类判定数量。等于"Tap数量+Hold数量"（即Hold的头判也计入），不包括任何侧键
     * - T_HOLD：非侧键Hold类判定数量。等于每条非侧键Hold的"头判定+中间判定"之和
     * - T_SIDE：侧键Tap类判定数量。同T_TAP，但只计侧键
     * - T_SHOLD：侧键Hold类判定数量。同T_HOLD，但只计侧键
     * - T_FLICK：Flick数量
     * - T_BELL：Bell数量
     * - T_TOTAL：T_TAP + T_HOLD + T_SIDE + T_SHOLD + T_FLICK（注意，T_BELL不计入T_TOTAL）
     */
    public Dictionary<string, int> CountNotes()
    {
        int tTap = 0, tSide = 0, tHold = 0, tShold = 0, tFlick = 0, tBell = 0;

        foreach (var note in Notes)
        {
            switch (note)
            {
                case Bell:
                    tBell++;
                    break;
                case Flick:
                    tFlick++;
                    break;
                case Hold hold:
                {
                    var judges = StatisticsUtils.CalcHoldJudgeCount(hold.Time, hold.EndTime, this, (int)ProgJudgeBpm);
                    if (hold.Lane.Type == LaneType.Wall) { tSide++; tShold += judges; }
                    else { tTap++; tHold += judges; }
                    break;
                }
                case Tap tap:
                {
                    if (tap.Lane.Type == LaneType.Wall) tSide++;
                    else tTap++;
                    break;
                }
            }
        }
        return new Dictionary<string, int>
        {
            ["T_TOTAL"] = tTap + tHold + tSide + tShold + tFlick,
            ["T_TAP"] = tTap,
            ["T_HOLD"] = tHold,
            ["T_SIDE"] = tSide,
            ["T_SHOLD"] = tShold,
            ["T_FLICK"] = tFlick,
            ["T_BELL"] = tBell,
        };
    }
    
    public override void Sort()
    {
        base.Sort();
        // 在base通用实现的基础上，额外排序我们自己新增的四个字段
        Lanes = Lanes.OrderBy(x => x.Time).ToList();
        Bullets = Bullets.OrderBy(x => x.Time).ToList();
        EnemyList.Sort();
        EnemyMovements = EnemyMovements.OrderBy(x => x.Time).ToList();
    }

    public override void Shift(Rational offset, decimal? bpm = null)
    {
        bpm ??= StartBpm;
        offset = _calcOffsetForShift(offset, bpm.Value);
        
        base.Shift(offset, bpm);
        // 在base通用实现的基础上，额外移动我们自己新增的四个字段
        Lanes = Lanes.Where(x => addOffset(x).Time >= 0).ToList();
        EnemyList = EnemyList.Select(x => x with {Time = x.Time + offset}).Where(x=>x.Time >= 0).ToList();
        EnemyMovements = EnemyMovements.Where(x => addOffset(x).Time >= 0).ToList();
        
        foreach (var bul in Bullets)
        {
            if (bul is Bullet bullet) bullet.Time += offset;
            else if (bul is Beam beam) beam.Points = beam.Points.Select(x => x with { Time = x.Time + offset }).ToList();
            else throw Utils.Fail();
        }
        Bullets = Bullets.Where(x => x.Time >= 0).ToList();
        
        T addOffset<T>(T lane)where T: OgkBaseLane<OgkLanePoint>
        {
            lane.Points = lane.Points.Select(x => x with {Time = x.Time + offset}).ToList();
            return lane;
        }
    }
}