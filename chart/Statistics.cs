using MuConvert.utils;
using Rationals;

namespace MuConvert.chart;

public class Statistics
{
    private readonly Dictionary<string, int> _data = [];
    public IReadOnlyDictionary<string, int> Data => _data;

    // 烟花数量
    public int Firework { get; private set; } = 0;

    private void AddNote(Note note)
    {
        string prefix = "NM";
        if (note.IsBreak && note.IsEx) prefix = "BX";
        else if (note.IsBreak) prefix = "BR";
        else if (note.IsEx) prefix = "EX";
        string type;
        if (note is Hold) type = "HLD";
        else if (note is Star) type = "STR";
        else if (note is Tap) type = "TAP";
        else if (note is Touch touch)
        {
            type = "TTP";
            if (note is TouchHold) type = "THO";
            if (touch.IsFirework) Firework++;
        }
        else if (note is Slide slide)
        {
            type = "SLD";
            if (slide.OwnHead != null) AddNote(slide.OwnHead);
        }
        else throw Utils.Fail();

        var res = prefix + type;
        _data[res] = _data.GetValueOrDefault(res) + 1;
        
        // TTM_EACHPAIRS 双押数量
        if (note is Tap)
        {
            if (note.Time == _now && note.FalseEachIdx == _nowFalseEachIndex) TTM_EACHPAIRS++;
            _now = note.Time;
            _nowFalseEachIndex = note.FalseEachIdx;
        }
    }

    internal Statistics(Chart chart)
    {
        foreach (var note in chart.Notes) AddNote(note);
    }

    // 音符总数（总物量）
    public int Total => _data.Values.Sum();

    // 返回按音符类型分组的数量。所返回的字典中会包含的key：TAP,STR,HLD,SLD,TTP,THO
    public Dictionary<string, int> ByNoteType => _data.GroupBy(x => x.Key[2..5])
            .ToDictionary(x => x.Key, x => x.Sum(v => v.Value))
            .EnsureKeys(["TAP", "STR", "HLD", "SLD", "TTP", "THO"]);
    
    // 返回按音符的修饰符分组的数量。所返回的字典中会包含的key：NM,BR,EX,BX
    public Dictionary<string, int> ByModifiers => _data.GroupBy(x => x.Key[..2])
        .ToDictionary(x => x.Key, x => x.Sum(v => v.Value))
        .EnsureKeys(["NM", "BR", "EX", "BX"]);
    
    // 返回按游戏结算画面上屏判定表分类的数量。所返回的字典中会包含的key：TAP,HOLD,SLIDE,TOUCH,BREAK
    public Dictionary<string, int> ByScoring => _data.GroupBy(x =>
        {
            if (x.Key[0] == 'B') return "BREAK";
            var type = x.Key[2..5];
            if (type is "HLD" or "THO") return "HOLD";
            else if (type == "SLD") return "SLIDE";
            else if (type == "TTP") return "TOUCH";
            else if (type is "TAP" or "STR") return "TAP";
            else throw Utils.Fail();
        })
        .ToDictionary(x => x.Key, x => x.Sum(v => v.Value))
        .EnsureKeys(["TAP","HOLD","SLIDE","TOUCH","BREAK"]);
    
    // 绝赞总数
    public int Break => _data.Where(x=>x.Key[0] == 'B').Sum(x=>x.Value);
    
    // 保护套总数
    public int EX => _data.Where(x=>x.Key[1] == 'X').Sum(x=>x.Value);
    
    // 修正物量（Slide算3个，Hold算2个，Break算5个）
    public int WeightedNoteCount {
        get
        {
            var d = ByScoring;
            return d["TAP"] + d["TOUCH"] + d["HOLD"] * 2 + d["SLIDE"] * 3 + d["BREAK"] * 5;
        }
    }
    
    // 旧框计算规则下的分数
    public int OldScore => WeightedNoteCount * 500 + Break * 100;
    
    // 粉1个tap的损失
    public double Great1Loss => 100.0d / WeightedNoteCount;

    public override string ToString()
    {
        var t = ByNoteType;
        var m = ByModifiers;
        List<string> r = [$"Tap: {t["TAP"]}", $"Hold: {t["HLD"]}", $"Star: {t["STR"]}", 
            $"Slide: {t["SLD"]}", $"Touch: {t["TTP"]}", $"Touch Hold: {t["THO"]}",
            $"Total: {Total}", 
            $"Break: {ByModifiers["BR"] + ByModifiers["BX"]}", $"Ex: {ByModifiers["EX"] + ByModifiers["BX"]}", 
            $"Firework: {Firework}"];
        return string.Join(", ", r);
    }

    public int T_JUDGE_HLD { get; private set; } = 0;
    public int TTM_EACHPAIRS { get; private set; } = 0;
    
    private Rational _now = -1; // 计算双押个数用
    private int _nowFalseEachIndex = 0;
    
    private int getProgJudgeGrid(decimal bpm)
    {
        if (bpm < 15) return 3;
        int exp = (int)Math.Min(Math.Floor(Math.Log2((double)bpm / 15)), 6);
        return 6 * (int)Math.Pow(2, exp);
    }
}