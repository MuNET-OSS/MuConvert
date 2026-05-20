using MuConvert.utils;
using Rationals;

namespace MuConvert.ogk;

public record OgkLanePoint(Rational Time, int Pos);

public abstract class OgkBaseLane<T> where T: OgkLanePoint
{
    public List<T> Points = [];

    public Rational Time => Points.First().Time;
}

public class Lane(LaneType type): OgkBaseLane<OgkLanePoint>
{
    public LaneType Type => type;
    public bool IsTransparent = false;
}

public class ColorfulLane() : Lane(LaneType.Colorful)
{
    public required ColorfulLaneColor Color;
    public int Brightness = 2;
}

// 这里的Start，是相对于Wall的开始时刻的！不是绝对时间！
public record Block(Rational Start, Rational Duration, int Pos, int EndPos);

public class Wall() : Lane(LaneType.Wall)
{
    public required Direction Direction;
    
    /**
     * 给墙壁上添加“阻挡区域”。
     * 注意！Block中的Start是相对于Wall的起始时刻(Time)的，而不是绝对时间。
     * 
     * 默认的“墙壁”描述的仅仅是场地的有效范围，显然玩家摇杆是可以穿越出去的；
     * 但如果这里加上Blocks的话，玩家摇杆就无法穿越出去了。
     */
    public List<Block> Blocks = [];
}

public class EnemyMovement: OgkBaseLane<OgkLanePoint>;
