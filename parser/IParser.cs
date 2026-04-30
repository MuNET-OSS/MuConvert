using MuConvert.chart.mai;
using MuConvert.utils;

namespace MuConvert.parser;

public interface IParser
{
    public (MaiChart, List<Alert>) Parse(string text);
}