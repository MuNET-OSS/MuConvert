using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;

public class OgkChart: BaseChart<OgkNote>
{
    public string Designer { get; set; } = ""; // 谱师

    public decimal ProgJudgeBpm = 240m; // 用于给Hold生成中间判定点的BPM值

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
    public override int TotalNotes => throw new NotImplementedException();

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