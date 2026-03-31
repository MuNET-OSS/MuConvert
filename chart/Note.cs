using Rationals;

namespace MuConvert.chart;

public class Note
{
    public Chart chart;
    public Rational time;

    public Note(Chart chart, Rational time)
    {
        this.chart = chart;
        this.time = time;
    }
}