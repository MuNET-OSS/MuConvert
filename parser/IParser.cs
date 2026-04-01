using MuConvert.chart;
using MuConvert.utils;

namespace MuConvert.parser;

public interface IParser
{
    public (Chart, List<Message>) Parse(string text);
}

public class ParsingException(List<Message> messages) : Exception
{
    public List<Message> Messages = messages;
}