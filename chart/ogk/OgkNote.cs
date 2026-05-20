using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;

/**
 * 所有游戏内的参与计分的基础音符所构成的基类。
 * 包括：Tap(普通键和侧键)、Hold(普通键和侧键)、Flick、Bell四种子类。
 */
public abstract class OgkNote: BaseNote
{
    /**
     * 音符的水平位置。取值范围[-24,24]。
     * 之所以用Rational，是因为极个别TAP/HOLD音符可能不在整数位置上；但绝大多数音符，包括所有的bell、flick，都一定是在整数位置上的。
     */
    public Rational Pos { get; set; }
    
    public bool IsEx;
}

public class Tap: OgkNote
{
    public required Lane Lane;
}

public class Hold : Tap
{
    public override Rational EndTime { get; set; }
    public Rational EndPos;
}

public class Flick : OgkNote
{
    public Direction Direction;
}

public class Bell: OgkNote, IBullet
{
    public BulletPallete? Detail { get; set; } = null; // 大多数的bell默认是原地不动的。如果指定此属性，则可以实现和子弹类似的可动bell的效果。
}
