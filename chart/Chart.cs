using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

public class Chart
{
    public BPMList BpmList = [];
    public List<Note> Notes = [];
    
    public string DefaultTouchSize = "M1";
    public bool IsUtage = false;
    public int ClockCount = 4;
    
    public Rational ToSecond(Rational barTime) => BpmList.ToSecond(barTime);

    public void Sort()
    {
        // 分别把BpmList和Notes，依照Time做稳定排序。排序务必要稳定！
        var sortedBpms = BpmList.OrderBy(b => b.Time).ToList(); // LINQ OrderBy 是稳定排序
        BpmList.Clear();
        BpmList.AddRange(sortedBpms);

        Notes = Notes.OrderBy(n => n.Time).ThenBy(n=>n.FalseEachIdx).ToList(); // LINQ OrderBy 是稳定排序
    }
    
    // 谱面开头的BPM
    public decimal StartBpm {
        get
        {
            Utils.Assert(BpmList[0].Time == 0, "BPM列表的开头必须为0时刻");
            return BpmList[0].Bpm;
        }
    }

    /**
     * 获得“谱面中第一个音符的时刻”，或者返回的Duration也可以理解成“从谱面开头到出现第一个音符所经过的时长”。
     * 所以同样的，它也有Bar、InvariantBar、Seconds的不同形态，因此使用Duration的形式存储。
     */
    public Duration FirstNoteTime => new(new PseudoNote(this)) {Bar = Notes[0].Time};
    
    /**
     * 对整首歌曲，应用一个偏移量进行整体平移。
     * <param name="offset">偏移量，正数表示歌曲整体向后，负数表示歌曲整体向前。</param>
     * <param name="bpm">上述偏移量所对应的Bpm。若不传，默认使用歌曲开头的BPM（即chart.StartBpm）。</param>
     */
    public void Shift(Rational offset, decimal? bpm = null)
    {
        bpm ??= StartBpm;
        
        if (offset < 0)
        { // 向前平移。此时存在的一种极端情况就是指定的区间跨过了多个BPM区间。
            // 传入的bpm参数本质是一种写死的InvariantBar，因此要把它转为可变Bar，才是真正的要去应用的offset。
            offset = -Duration.ConvertTime(0, -offset, (Rational)bpm, null, BpmList);
        }
        else if (offset > 0)
        { // 向后平移。需要把传入的offset的量换算到乐曲开头BPM下，才是真正的量。
            offset = offset * (Rational)StartBpm / (Rational)bpm;
            BpmList[0] = BpmList[0] with {Time = -offset}; // 暂时把BPMList设为负值，这样一会应用offset之后就加回来了。
        }

        var tmpBpmList = BpmList.Select(x => x with { Time = x.Time + offset }).ToList();
        var lastNonPositive = tmpBpmList.FindLastIndex(x=>x.Time <= 0); // 对offset为负的情况，要把被移进负区间的bpm给掰回来。
        if (lastNonPositive != -1) tmpBpmList[lastNonPositive] = tmpBpmList[lastNonPositive] with {Time = 0};
        BpmList = new BPMList(tmpBpmList.Where(x => x.Time >= 0));
        Utils.Assert(BpmList[0].Time == 0, "BPM列表的开头必须为0时刻");
        Notes = Notes.Select(x => { x.Time += offset; return x; }).Where(x => x.Time >= 0).ToList();
    }

    public bool IsDxChart => Notes.Any(note => // 判定DX谱的标准：存在
        note is Touch || note.IsEx || (note.IsBreak && note is not Tap) || // Touch 或者 保护套 或者 非Tap/Star的绝赞
        note is Slide { segments.Count: > 1 }); // 星星段数大于1（fes星星）
    
    // TODO 把谱面统计搬到Chart类下面来
    
    
}
