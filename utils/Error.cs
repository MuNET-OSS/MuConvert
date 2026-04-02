using MuConvert.chart;
using Rationals;

namespace MuConvert.utils;

public class Alert
{
    public enum LEVEL { Error, Warning, Info, Debug }

    public LEVEL Level;
    public (Chart, Rational)? BarTime;
    public int? Line;

    public string? RelevantNote;
    public string Description;
    
    public Alert(LEVEL level, string description, (Chart, Rational)? barTime = null, int? line = null, string? relevantNote = null)
    {
        Level = level;
        Description = description;
        BarTime = barTime;
        Line = line;
        RelevantNote = relevantNote;
    }

    public override string ToString()
    {
        List<string> tags = [];
        if (Line != null) tags.Add(string.Format(Locale.MessageLine, Line));
        if (BarTime != null)
        {
            var (chart, time) = BarTime.Value;
            tags.Add(string.Format(Locale.MessageTime, time, (float)chart.ToSecond(time)));
        }
        if (RelevantNote != null) tags.Add(string.Format(Locale.MessageParsing, RelevantNote));
        var tagString = tags.Count > 0 ? $"(${Locale.MessageAt} {string.Join(", ", tags)}) " : "";
        
        string head = "";
        switch (Level)
        {
            case LEVEL.Error:
                head = Locale.Error;
                break;
            case LEVEL.Warning:
                head = Locale.Warning;
                break;
            case LEVEL.Info:
                head = Locale.Info;
                break;
            case LEVEL.Debug:
                head = Locale.Debug;
                break;
        }
        
        return $"{tagString}{head} {Description}";
    }
}

public class ConversionException(List<Alert> alerts) : Exception
{
    public List<Alert> Alerts = alerts;

    public override string Message => string.Join("\n", Alerts.Select(a => a.ToString()));
}