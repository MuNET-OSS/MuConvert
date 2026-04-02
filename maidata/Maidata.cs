using System.Text;

namespace MuConvert.maidata;

public record MaidataChart(string Level, string NoteDesigner, string Inote);

public class Maidata : Dictionary<string, string>
{
    /**
     * 便捷的获得一个maidata中的所有谱面的方法。
     * 除了铺面之外的信息，如title、artist、first等，仍可通过一般的dict方法加以获得。
     */
    public Dictionary<int, MaidataChart> Levels
    {
        get
        {
            var result = new Dictionary<int, MaidataChart>();
            foreach (var (k ,v) in this)
            {
                if (k.StartsWith("inote_"))
                {
                    if (!int.TryParse(k.Replace("inote_", ""), out var id)) continue;
                    var level = this.GetValueOrDefault($"lv_{id}", "");
                    var noteDesigner = this.GetValueOrDefault($"des_{id}", "");
                    result.Add(id, new MaidataChart(level, noteDesigner, v));
                }
            }
            return result;
        }
    }
    
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
        if (key.StartsWith("inote") || key.StartsWith("lv") || key.StartsWith("first") || key.StartsWith("wholebpm"))
            value = value.Trim(); // 对部分字段，要trim一下；但不能对所有的字段都trim，比如如果对title进行trim，如月车站就寄了。
        this[key] = value;
    }
    
    public string Title => this.GetValueOrDefault("title", "");
    public string Artist => this.GetValueOrDefault("artist", "");
    public float? WholeBpm => float.TryParse(this.GetValueOrDefault("wholebpm", ""), out var wholebpm) ? wholebpm : null;
    public float First => float.TryParse(this.GetValueOrDefault("first", ""), out var first) ? first : 0f;
    public int ClockCount => int.TryParse(this.GetValueOrDefault("clock_count", ""), out var clockCount) ? clockCount : 4;
    public (float, float?)? Demo
    {
        get
        {
            if (!float.TryParse(this.GetValueOrDefault("demo_seek", ""), out var demoStart)) return null;
            float? demoLen = float.TryParse(this.GetValueOrDefault("demo_len", ""), out var v) ? v : null;
            return (demoStart, demoLen);
        }
    }
    
}