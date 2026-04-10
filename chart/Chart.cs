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
}
