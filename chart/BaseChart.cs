using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

public interface IBaseChart;

/**
 * 所有的谱面均应该继承自此类。
 * 此类中提供了Notes列表，作为存储音符的核心列表；存储的音符类型是特定于谱面的（继承时传入泛型）
 * 此外，应当重写以下四个抽象的getter。
 */
public abstract class BaseChart<TNote>: IBaseChart where TNote: BaseNote
{
    /**
     * 所有音符构成的列表
     */
    public List<TNote> Notes = [];
    
    /**
     * 所有BPM构成的列表
     */
    public BPMList BpmList = [];
    
    /**
     * 所有拍号声明构成的列表。
     *
     * 已知该内容，目前在游戏内暂无实质性的效果。maimai中无任何效果，chunithm则会把这个作为显示小节和拍子参考线时候额依据、但也仅影响显示效果，对游戏本身无影响。
     */
    public List<MET> MetList = [];
    
    /**
     * 所有变速声明构成的列表。
     *
     * maimai不会用到，ongeki和chunithm才会用到。
     */
    public List<(Rational Time, Rational Duration, decimal Multiplier)> SflList = [];
    
    /**
     * 谱面开头的“哒哒哒哒”声音的个数。
     *
     * 具体而言，有两种指定方式：
     * 1. 直接设置本属性。则会利用游戏内部自带的CLK_DEF机制，会按每拍一次的频率生成指定个数的"哒"声。
     * 2. 设置ExplicitClocks属性。这个直接对应于谱面中的CLK语句，有几行就是几声。
     */
    public int ClockCount
    {
        get => ExplicitClocks?.Count ?? field;
        set
        {
            field = value;
            ExplicitClocks = null;
        }
    } = 4;
    
    /**
     * 这是MA2/OGKR语句中，通过CLK指令所显式指定的哒哒哒哒的时刻。
     * 除去极个别官谱外，一般来说极少会用到。详见ClockCount属性上的注释。
     */
    public List<Rational>? ExplicitClocks;
    
    /**
     * 根据BPMList中的声明，将小节时间转换为秒。
     */
    public Rational ToSecond(Rational barTime) => BpmList.ToSecond(barTime);
    
    /**
     * 谱面开头的BPM
     */
    public virtual decimal StartBpm {
        get
        {
            Utils.Assert(BpmList[0].Time == 0, "BPM列表的开头必须为0时刻");
            return BpmList[0].Bpm;
        }
    }
    
    /**
     * 获得谱面开始的时刻（即谱面中第一个音符的开始时刻）。单位为秒（注意单位与Note的Time不同！）
     */
    public virtual decimal StartTime => (decimal)ToSecond(Notes.First().Time);

    /**
     * 获得谱面结束的时刻（即谱面中最后一个音符的完成时刻）。单位为秒（注意单位与Note的EndTime不同！）
     */
    public virtual decimal EndTime => (decimal)ToSecond(Notes.Max(x=>x.EndTime));
    
    /**
     * 总音符数量（物量）。
     *
     * 注：具体实现很可能会需要重写此方法，因为对绝大多数的游戏，“物量”都不是简单的等于Note数量的加和的，很可能会和判定等有关
     */
    public virtual int TotalNotes => Notes.Count;

    // 内部使用，供子类重写，实现比只用Time更复杂的排序逻辑
    protected virtual IEnumerable<TNote> SortNotes()
    {
        return Notes.OrderBy(n => n.Time); // 默认按照Time排序，子类可以重写此方法实现更复杂的排序逻辑
    }
    
    public virtual void Sort()
    {
        // 分别把BpmList和Notes，依照Time做稳定排序。排序务必要稳定！
        var sortedBpms = BpmList.OrderBy(b => b.Time).ToList(); // LINQ OrderBy 是稳定排序
        BpmList.Clear();
        BpmList.AddRange(sortedBpms);

        MetList = MetList.OrderBy(x => x.Time).ToList();
        SflList = SflList.OrderBy(x => x.Time).ToList();
        if (ExplicitClocks != null) ExplicitClocks = ExplicitClocks.Order().ToList();
        
        Notes = SortNotes().ToList(); // LINQ OrderBy 是稳定排序
    }
    
    /**
     * 对整首歌曲，应用一个偏移量进行整体平移。
     * <param name="offset">偏移量，正数表示歌曲整体向后，负数表示歌曲整体向前。</param>
     * <param name="bpm">上述偏移量所对应的Bpm。若不传，默认使用歌曲开头的BPM（即chart.StartBpm）。</param>
     */
    public virtual void Shift(Rational offset, decimal? bpm = null)
    {
        bpm ??= StartBpm;
        
        if (offset < 0)
        { // 向前平移。此时存在的一种极端情况就是指定的区间跨过了多个BPM区间。
            // 传入的bpm参数本质是一种写死的InvariantBar，因此要把它转为可变Bar，才是真正的要去应用的offset。
            offset = -BpmList.ConvertTime(0, -offset, bpm, null);
        }
        else if (offset > 0)
        { // 向后平移。需要把传入的offset的量换算到乐曲开头BPM下，才是真正的量。
            offset = offset * (Rational)StartBpm / (Rational)bpm;
        }

        // 对BpmList和MetList的处理：需要确保首项为0
        BpmList = new BPMList(BpmList.Select(x => x with { Time = x.Time + offset })
            .Skip(BpmList.Count(x => x.Time <= 0) - 1) // 至多只保留一个非正项，其他的舍弃。直接Count-1这么写是没有问题的，因为Skip传入-1等价于传入0。
            .Select((x, i) => i == 0 ? x with { Time = 0 } : x)); // 把第一项（唯一的可能非正项）强制设为0
        MetList = MetList.Select(x => x with { Time = x.Time + offset }) // 同上
            .Skip(MetList.Count(x => x.Time <= 0) - 1)
            .Select((x, i) => i == 0 ? x with { Time = 0 } : x).ToList();
        
        // 对SflList和ExplicitClocks的处理：加上offset后，只保留>=0的项
        SflList = SflList.Select(x => x with { Time = x.Time + offset })
            .Where(x => x.Time >= 0).ToList();
        if (ExplicitClocks != null) 
            ExplicitClocks = ExplicitClocks.Select(x => x + offset).Where(x => x >= 0).ToList();
        
        // Notes，时间加上offset，但注意需要对children进行递归操作
        HashSet<BaseNote> processed = [];
        Notes = Notes.Where(x => addOffset(x).Time >= 0).ToList();
        
        BaseNote addOffset(BaseNote note)
        {
            if (!processed.Contains(note))
            {
                note.Time += offset;
                processed.Add(note);
                foreach (var child in note.Children) addOffset(child);
            }
            return note;
        }
    }
}