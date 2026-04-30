using MuConvert.chart;
using MuConvert.utils;

namespace MuConvert.generator;

public interface IGenerator<TChart> where TChart : IBaseChart
{
    public (string, List<Alert>) Generate(TChart chart);
}