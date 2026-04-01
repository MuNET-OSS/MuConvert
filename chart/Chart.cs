using Rationals;

namespace MuConvert.chart;

public class Chart
{
    public BPMList BpmList = [];
    public List<Note> Notes = [];
    
    public string DefaultTouchSize = "M1";
    
    public Rational ToSecond(Rational barTime) => BpmList.ToSecond(barTime);
}
