using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;

public class OgkChart: BaseChart<OgkNote>
{
    public string Designer { get; set; } = ""; // 谱师

    // 全局声明的“子弹伤害类型与伤害数值的映射关系”。绝大多数情况下都是这个默认值，不需要做改动。
    public Dictionary<BulletDamage, decimal> BulletDamages = new() {
        [BulletDamage.NML] = 1.0m, [BulletDamage.STR] = 2.0m, [BulletDamage.DNG] = 4.0m,
    };
    
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
}