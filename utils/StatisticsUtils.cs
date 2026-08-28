using MuConvert.chart;
using Rationals;

namespace MuConvert.utils;

public static class StatisticsUtils
{
    // 当前音符落在了哪些BPM区间内、分别有多长。
    public static List<(int bpmIdx, decimal bpm, Rational start, Rational len)> CalcBpmRanges<T>(Rational time, Rational endTime, BaseChart<T> chart) where T: BaseNote
    {
        List<(int, decimal, Rational, Rational)> result = []; 
        var now = time.CanonicalForm; 
        var end = endTime.CanonicalForm; 
        var isFirstRange = true; // 通过这个变量和对应的逻辑，确保返回的BpmRanges至少含有一个元素。即使note本身是0长度的，返回的BpmRanges也能有一个len=0的元素。
        while (now < end || isFirstRange) 
        { 
            var bpmIdx = chart.BpmList.FindIndex(now); 
            var curBpmRangeEnd = bpmIdx < chart.BpmList.Count - 1 ? chart.BpmList[bpmIdx + 1].Time : 9999999; // 当前BPM区间的结束时刻
            var len = Utils.Min(end, curBpmRangeEnd) - now; // 音符落在本区间内的长度为，从当前时刻开始，到（本区间结束或音符结束的较早者）
            result.Add((bpmIdx, chart.BpmList[bpmIdx].Bpm, now, len.CanonicalForm)); 
            now = (now + len).CanonicalForm; 
            isFirstRange = false;
        } 
        return result;
    }
    
    // 该实现等同于游戏DLL中的Manager.NotesReader.getProgJudgeGrid
    private static Rational GetProgJudgeGrid(decimal bpm, int progJudgeBpm = 240)
    {
        int exp = (int)Math.Floor(Math.Log2((double)bpm / progJudgeBpm)); // 相比于一拍，应该加倍的次数
        exp = Math.Clamp(exp, -5, 2); // 游戏DLL中的getProgJudgeGrid函数实现，满足输出范围一定在(3/384~384/384)之间（对应7.5<=bpm<1920），对超出上述范围的bpm会把结果clamp保持在这个范围内。因此我们也保持完全相同的实现。
        return (Rational)Math.Pow(2, exp) / 4; // 除以4是因为，progJudgeBpm下的Grid是一拍，一拍是1/4小节
    }
    
    // 具体的机制研究的也不是太明白，只是尽力实现了下
    // 对该Hold所落入的每个BPM区间，都是每隔gridSize生成一个中间判定点
    internal static int CalcHoldJudgeCount<T>(Rational time, Rational endTime, BaseChart<T> chart, int progJudgeBpm = 240) where T : BaseNote
    {
        var bpmRanges = CalcBpmRanges(time, endTime, chart);
        
        var result = 0;
        foreach (var (_, bpm, _, len) in bpmRanges)
        {
            var gridSize = GetProgJudgeGrid(bpm, progJudgeBpm);
            result += Math.Max((int)(len / gridSize).Ceil(), 1);
        }
        return result;
    }
}