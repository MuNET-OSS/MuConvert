using MuConvert.chart.mai;
using MuConvert.utils;

namespace MuConvert.generator;

public interface IGenerator
{
    public (string, List<Alert>) Generate(MaiChart chart);
}