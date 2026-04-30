namespace MuConvert.chart;

public interface IBaseChart;

/**
 * 所有的谱面均应该继承自此类。
 * 此类中提供了Notes列表，作为存储音符的核心列表；存储的音符类型是特定于谱面的（继承时传入泛型）
 * 此外，应当重写以下四个抽象的getter。
 */
public abstract class BaseChart<TNote> : IBaseChart
{
    /**
     * 所有音符构成的列表
     */
    public List<TNote> Notes = [];
    
    /**
     * 谱面开头的BPM
     */
    public abstract decimal StartBpm { get; }
    
    /**
     * 获得谱面开始的时刻（即谱面中第一个音符的开始时刻）。单位为秒
     */
    public abstract decimal StartTime { get; }

    /**
     * 获得谱面结束的时刻（即谱面中最后一个音符的完成时刻）。单位为秒
     */
    public abstract decimal EndTime { get; }
    
    /**
     * 总音符数量（物量）
     */
    public abstract int TotalNotes { get; }
}