using MuConvert.chart;
using MuConvert.utils;
using Rationals;

namespace MuConvert.mai;

public class MaiChart: BaseChart<Note>
{
    public string DefaultTouchSize = "M1";

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
     * 获得谱面开始的时刻（即谱面中第一个音符的开始时刻）。
     * 
     * 返回的Duration可以理解成“从谱面开头到出现第一个音符所经过的时长”。
     * 所以同样的，它也有Bar、InvariantBar、Seconds的不同形态，因此使用Duration的形式存储。
     */
    public new Duration StartTime => new(new PseudoNote(this)) {Bar = Notes[0].Time};

    public override int TotalNotes => Statistics.Total;

    public bool IsDxChart => Notes.Any(note => // 判定DX谱的标准：存在
        note is Touch || note.IsEx || (note.IsBreak && note is not Tap) || // Touch 或者 保护套 或者 非Tap/Star的绝赞
        note is Slide { segments.Count: > 1 }); // 星星段数大于1（fes星星）

    public Statistics Statistics => new(this);
    
    /**
     * 这是MA2语句中，通过CLK指令所显式指定的哒哒哒哒的时刻。
     * 一般来说极少会用到，这里只是忠实地记录一下；一方面符合我们“0信息损失”的原则、忠实地记录铺面中的信息；
     * 另一方面，可以用作ClockCount自动推导的来源之一。
     * 普通用户理论上极少会用到这个东西。
     */
    public List<Rational>? ExplicitClocks;

    protected override IEnumerable<Note> SortNotes()
    {
        return Notes.OrderBy(note => note.Time).ThenBy(n=>n.FalseEachIdx);
    }
}
