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
}
