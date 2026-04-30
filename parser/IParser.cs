using MuConvert.chart;
using MuConvert.utils;

namespace MuConvert.parser;

public interface IParser<TChart> where TChart : IBaseChart
{
    public (TChart, List<Alert>) Parse(string text);
}