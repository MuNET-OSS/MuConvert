using Rationals;

namespace MuConvert.utils;

public class Alert
{
    public enum LEVEL { Error, Warning, Info, Debug }

    public LEVEL Level;
    public Rational? TimeInBar;
    public double? TimeInSeconds;
    public int? Line;

    public string? RelevantNote;
    public string Description;
    
    public Alert(LEVEL level, string description)
    {
        Level = level;
        Description = description;
    }
    
    public Alert(LEVEL level, string description, int? line = null, string? relevantNote = null)
        : this(level, description)
    {
        Line = line;
        RelevantNote = relevantNote;
    }
    
    public Alert(LEVEL level, string description, Rational? timeInBar = null, double? timeInSeconds = null, int? line = null, string? relevantNote = null)
        : this(level, description, line, relevantNote)
    {
        TimeInBar = timeInBar;
        TimeInSeconds = timeInSeconds;
    }
    
    public Alert(LEVEL level, string description, (mai.MaiChart, Rational) barTime, int? line = null, string? relevantNote = null)
        : this(level, description, line, relevantNote)
    {
        var (chart, time) = barTime; 
        TimeInBar = time; 
        if (chart.BpmList.Count > 0) TimeInSeconds = (double)chart.ToSecond(time);
    }

    public override string ToString()
    {
        List<string> tags = [];
        if (Line != null) tags.Add(string.Format(Locale.MessageLine, Line));
        if (TimeInBar != null && TimeInSeconds != null) tags.Add(string.Format(Locale.MessageTimeAndBar, TimeInBar.Value.CanonicalForm, TimeInSeconds.Value));
        else if (TimeInBar != null) tags.Add(string.Format(Locale.MessageBar, TimeInBar.Value.CanonicalForm));
        else if (TimeInSeconds != null) tags.Add(string.Format(Locale.MessageTime, TimeInSeconds.Value));
        if (RelevantNote != null) tags.Add(string.Format(Locale.MessageParsing, RelevantNote));
        var tagString = tags.Count > 0 ? $"({Locale.MessageAt} {string.Join(", ", tags)}) " : "";
        
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

public class ConversionException : Exception
{
    public ConversionException(List<Alert> alerts): base(alerts.LastOrDefault()?.ToString())
    {
        Alerts = alerts;
    }

    public ConversionException(List<Alert> alerts, Exception? innerException) : base(alerts.LastOrDefault()?.ToString(), innerException)
    {
        Alerts = alerts;
    }

    public List<Alert> Alerts;

    public override string Message => string.Join("\n", Alerts.Select(a => a.ToString()));
}