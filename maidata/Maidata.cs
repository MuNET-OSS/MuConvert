using System.Globalization;
using System.Text;
using MuConvert.utils;

namespace MuConvert.maidata;

public record MaidataChart(string Inote, string? Level = null, string? NoteDesigner = null);

public class Maidata : Dictionary<string, string>
{
    /**
     * 便捷的获得一个maidata中的所有谱面的方法。
     * 除了谱面之外的信息，如title、artist、first等，可通过Infos获取；
     * 而由于，如果用一般的Dict的方法遍历/访问Maidata对象的话，拿到的是整个maidata的、包含inote等在内的所有信息。
     */
    public IReadOnlyDictionary<int, MaidataChart> Levels => _splitLevels().Item1;
    
    /**
     * 便捷的获得一个maidata中，除了谱面相关的所有信息字段的方法。
     * 而由于Maidata类继承自Dictionary，如果用一般的Dict的方法遍历/访问Maidata对象的话，拿到的是整个maidata的、包含inote等在内的所有信息。
     */
    public IReadOnlyDictionary<string, string> Infos => _splitLevels().Item2;

    /**
     * 向Maidata中添加"&ChartConvertTool=MuConvert"的信息。
     */
    public void AddToolData()
    {
        this["ChartConvertTool"] = "MuConvert";
        this["ChartConvertToolVersion"] = Utils.AppVersion;
    }

    public void AddLevel(int levelId, MaidataChart maidataChart, bool addToolData = true)
    {
        this[$"inote_{levelId}"] = maidataChart.Inote;
        if (maidataChart.Level != null) this[$"lv_{levelId}"] = maidataChart.Level;
        if (maidataChart.NoteDesigner != null) this[$"des_{levelId}"] = maidataChart.NoteDesigner;
        if (addToolData) AddToolData();
    }
    
    public Maidata() {}
    
    /**
     * 将maidata.txt的文本传给此函数，即可构造Maidata对象。
     */
    public Maidata(string maidataTxt)
    {
        string? key = null;
        var content = new StringBuilder();
        foreach (var line in maidataTxt.EnumerateLines())
        {
            if (line.Length > 0 && line[0] == '&')
            {
                // 找到了新的标签，把旧的放进去
                _putKey(key, content);
                var pos = line.IndexOf('=');
                key = line[1..pos].ToString();
                content.Append(line[(pos+1)..]);
            }
            else
            {
                content.Append('\n');
                content.Append(line);
            }
        }
        _putKey(key, content);
        if (key == null) throw new Exception(Locale.InvalidMaidataFile); // 全程没看到任何标签
    }
    
    private void _putKey(string? key, StringBuilder content)
    {
        var value = content.ToString();
        content.Clear();
        if (key == null) return;
        value = value.TrimEnd('\n'); // 字符串末尾的连续\n，说明是空白行，要去掉
        if (key.StartsWith("inote") || key.StartsWith("lv") || key == "first" || key == "wholebpm" || 
            key == "clock_count" || key.StartsWith("demo_") || key.StartsWith("ChartConvertTool"))
            value = value.Trim(); // 对部分字段，要trim一下；但不能对所有的字段都trim，比如如果对title进行trim，如月车站就寄了。
        this[key] = value;
    }

    private (Dictionary<int, MaidataChart>, Dictionary<string, string>) _splitLevels()
    {
        var levels = new Dictionary<int, MaidataChart>();
        var infos = new Dictionary<string, string>(this); // 复制一份，稍后删key
        foreach (var k in this.Keys)
        {
            if (k.StartsWith("inote_"))
            {
                if (!int.TryParse(k.Replace("inote_", ""), out var id)) continue;
                // 一边从info中移除内容，一边加到levels里去
                infos.Remove(k, out var v);
                infos.Remove($"lv_{id}", out var level);
                infos.Remove($"des_{id}", out var noteDesigner);
                levels.Add(id, new MaidataChart(v!, level, noteDesigner));
            }
        }
        return (levels, infos);
    }
    
    public string Title
    {
        get => this.GetValueOrDefault("title", "");
        set => this["title"] = value ?? "";
    }
    
    public string Artist
    {
        get => this.GetValueOrDefault("artist", "");
        set => this["artist"] = value ?? "";
    }
    
    public float? WholeBpm
    {
        get => float.TryParse(this.GetValueOrDefault("wholebpm", ""), out var wholebpm) ? wholebpm : null;
        set
        {
            if (value is null) Remove("wholebpm");
            else this["wholebpm"] = value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }
    
    public float First
    {
        get => float.TryParse(this.GetValueOrDefault("first", ""), out var first) ? first : 0f;
        set => this["first"] = $"{value:0.####}";
    }
    
    public int ClockCount
    {
        get => int.TryParse(this.GetValueOrDefault("clock_count", ""), out var clockCount) ? clockCount : 4;
        set => this["clock_count"] = value.ToString();
    }
    
    public (float, float?)? Demo
    {
        get
        {
            if (!float.TryParse(this.GetValueOrDefault("demo_seek", ""), out var demoStart)) return null;
            float? demoLen = float.TryParse(this.GetValueOrDefault("demo_len", ""), out var v) ? v : null;
            return (demoStart, demoLen);
        }
        set
        {
            if (value is null)
            {
                Remove("demo_seek");
                Remove("demo_len");
                return;
            }

            var (start, len) = value.Value;
            this["demo_seek"] = $"{start:0.####}";
            if (len is null) Remove("demo_len");
            else this["demo_len"] = $"{len:0.####}";
        }
    }

    public override string ToString()
    {
        var result = new StringBuilder();
        var (levels, infos) = _splitLevels();
        
        string[] firstKeys = ["title", "artist", "first", "des", "wholebpm"]; // 对这些键，优先、按这里指定的顺序输出。
        string[] lastKeys = ["ChartConvertTool", "ChartConvertToolVersion"]; // 对这些键，最后、按这里指定的顺序输出。
        foreach (var k in firstKeys)
        {
            if (TryGetValue(k, out var v)) result.AppendLine($"&{k}={v}");
        }
        foreach (var (k, v) in infos)
        {
            if (firstKeys.Contains(k) || lastKeys.Contains(k)) continue; // 刚刚已经输出过了，或者应该最后输出
            result.AppendLine($"&{k}={v}");
        }
        foreach (var k in lastKeys)
        {
            if (TryGetValue(k, out var v)) result.AppendLine($"&{k}={v}");
        }
        result.AppendLine();

        var levelIds = levels.Keys.ToList();
        levelIds.Sort();
        foreach (var id in levelIds)
        {
            var data = levels[id];
            if (data.Level != null) result.AppendLine($"&lv_{id}={data.Level}");
            if (data.NoteDesigner != null) result.AppendLine($"&des_{id}={data.NoteDesigner}");
            result.AppendLine($"&inote_{id}={data.Inote}");
            result.AppendLine();
        }

        return result.ToString();
    }
}