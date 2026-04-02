using MuConvert.chart;
using MuConvert.utils;

namespace MuConvert.generator;

public interface IGenerator
{
    public (string, List<Alert>) Generate(Chart chart);
}