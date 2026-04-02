using MuConvert.chart;
using MuConvert.utils;

namespace MuConvert.parser;

public interface IParser
{
    public (Chart, List<Alert>) Parse(string text);
}