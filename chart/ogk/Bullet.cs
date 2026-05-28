using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;


public interface IBullet
{
    public BulletPallete? Detail { get; set; }
    public Rational Time { get; }
}

/**
 * 声明子弹的细节行为，包括运动路径、速度、类型、尺寸等。
 *
 * record各项声明的默认值，对应于一种子弹静止不动的最简单情况，也就是官谱中的A0（绿谱当中能见到的子弹基本都是这种情况）
 */
public record BulletPallete(
    BulletShooter Shooter = BulletShooter.UPS, // 发出子弹的位置

    bool TargetToPlayer = false, // true表示该子弹射向玩家（取子弹生成时玩家的当前位置为目标位置）；false表示子弹射向基于TargetOffset所算出的目标位置。
    int TargetOffset = 0, // 子弹的终点位置相对于起点位置的偏移量 (仅当TargetToPlayer为false时有效)。多数情况下，此值设置为0，得到的是不动的子弹，反之就是动的子弹。
    int RandomOffsetDist = 0, // 如果不为0，则是随机子弹，结束位置会在正常位置的基础上随机偏移至多N格。
    
    decimal Speed = 1.0m,
    BulletSize Size = BulletSize.N,
    BulletType Type = BulletType.CIR
);

public class Bullet: IBullet
{
#pragma warning disable CS8767 // Bullet中的Detail是不能为null的
    public BulletPallete Detail { get; set; } = new();
#pragma warning restore CS8767
    
    public Rational Time { get; set; }
    public int Pos;

    public BulletDamage Damage = BulletDamage.NML;
}

public record BeamPoint(Rational Time, int Pos, int Width) : OgkLanePoint(Time, Pos);
public class Beam: OgkBaseLane<BeamPoint>, IBullet
{
    public int ObliqueOffset = 0; // 如果不为0，表示这个激光是倾斜激光(OBS)，否则是一般的激光(BMS)
    
    // 激光是没有IBullet的Detail属性的。但接口还必须实现一个，所以实现成返回null、不准设置。
    public BulletPallete? Detail { get => null; set => throw new InvalidOperationException("Beam has no bullet detail!"); }
}
