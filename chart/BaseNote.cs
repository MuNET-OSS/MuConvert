using Rationals;

namespace MuConvert.chart;

public abstract class BaseNote
{
    /**
     * 音符的开始时刻。以小节为单位（分数时间）
     */
    public virtual Rational Time { get; set => field = value.CanonicalForm; }

    /**
     * 音符的结束时刻。以小节为单位（分数时间）
     *
     * 默认实现中实现为等于开始时刻（瞬间音符，没有持续时间）。有持续时间的音符应当重写此属性。
     */
    public virtual Rational EndTime => Time;
    
    /**
     * 如果有某个音符，包含另一个/一些音符作为子音符，这些子音符本身不会出现在Chart的Notes列表内：
     * 则应将这些子音符放在这里，以便Chart还能“通过某种方式索引到它们”（目前只有BaseChart.Shift函数需要用到这一特性），而不是根本找不到。
     */
    public List<BaseNote> Children = [];
}
