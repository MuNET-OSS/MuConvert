using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using MuConvert.Antlr;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;

namespace MuConvert.parser;

public class ErrorListener(SimaiParser simaiParser): BaseErrorListener, IAntlrErrorListener<int>
{
    // 语法分析的错误报告函数
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException? e)
    {
        var parser = (Parser)recognizer;
        if (e == null)
        { // 被recoverInline了。此时给个警告就可以了。（PS:只有在宽松模式下才会触发，严格模式是禁止recoverInline、直接丢InputMismatch的）
            if (msg.StartsWith("extraneous"))
            {
                simaiParser.alerts.Add(new Alert(Warning, 
                    string.Format(Locale.RecoverInlineExtraneousToken, GetTokenErrorDisplay(offendingSymbol)), 
                    line: line, relevantNote: _contextText(parser.Context)));
                return;
            }
            else if (msg.StartsWith("missing"))
            {
                simaiParser.alerts.Add(new Alert(Warning, 
                    string.Format(Locale.RecoverInlineMissingToken, GetTokenErrorDisplay(offendingSymbol), parser.GetExpectedTokens().ToString(parser.Vocabulary)), 
                    line: line, relevantNote: _contextText(parser.Context)));
                return;
            }
        }

        Alert.LEVEL level = simaiParser.StrictLevel == SimaiParser.StrictLevelEnum.Lax ? Warning : Error;
        string msgPostFix = simaiParser.StrictLevel == SimaiParser.StrictLevelEnum.Lax ? Locale.LaxTryfixReminder : "";
        string message;
        switch (e)
        {
            case InputMismatchException:
                message = string.Format(Locale.InputMismatchException, GetTokenErrorDisplay(offendingSymbol), e.GetExpectedTokens().ToString(recognizer.Vocabulary));
                message += msgPostFix;
                break;
            case NoViableAltException ne:
                var iSt = (ITokenStream)parser.InputStream;
                message = string.Format(Locale.NoViableAltException, EscapeWSAndQuote(iSt == null ? "<unknown input>" : (ne.StartToken.Type != -1 ? iSt.GetText(ne.StartToken, ne.OffendingToken) : "<EOF>")));
                message += msgPostFix;
                break;
            default:
                message = string.Format(Locale.AntlrUnknownError, msg);
                break;
        }
        simaiParser.alerts.Add(new Alert(level, message, line: line, relevantNote: _contextText(parser.Context)));
    }

    // 词法分析的错误报告函数
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        var lexer = (Lexer)recognizer;
        // 遵照Lexer.NotifyListeners的实现写出的，获得出错的token的详细内容的代码。
        var input = (ICharStream)lexer.InputStream;
        string errContent = lexer.GetErrorDisplay(input.GetText(Interval.Of(lexer.TokenStartCharIndex, input.Index)));
        
        if (simaiParser.StrictLevel == SimaiParser.StrictLevelEnum.Strict)
        { // 严格模式下，不准恢复，抛异常
            simaiParser.alerts.Add(new Alert(Error, 
                string.Format(Locale.LexerNoViableAltExceptionStrict, errContent), 
                line: line, relevantNote: errContent));
            throw new ParseCanceledException(e);
        }
        simaiParser.alerts.Add(new Alert(Warning, 
            string.Format(Locale.LexerNoViableAltException, errContent), 
            line: line, relevantNote: errContent));
    }

    // 从context获得为适合放进relevantNote里的形式
    private string? _contextText(RuleContext? context)
    {
        while (true)
        {
            if (context == null) return null;
            if (context.GetText().Length >= 5) return context.GetText();
            context = context.Parent;
        }
    }

    # region 用于暴露DefaultErrorStrategy内部的GetTokenErrorDisplay函数
    private class _ES : DefaultErrorStrategy
    {
        public new string GetTokenErrorDisplay(IToken t) => base.GetTokenErrorDisplay(t);
        public new string EscapeWSAndQuote(string s) => base.EscapeWSAndQuote(s);
    }
    private _ES _es = new(); // 仅用作调用里面的方法
    private string GetTokenErrorDisplay(IToken t) => _es.GetTokenErrorDisplay(t);
    private string EscapeWSAndQuote(string s) => _es.EscapeWSAndQuote(s);
    # endregion
}

/**
 * 最宽松的ErrorStrategy，尽全力恢复不让谱面整个垮掉
 */
public class LaxErrorStrategy : DefaultErrorStrategy
{
    protected override IToken SingleTokenDeletion(Parser recognizer)
    {
        if (recognizer.CurrentToken?.Type == SimaiLexer.COMMA) return null!; // 不准删逗号
        return base.SingleTokenDeletion(recognizer);
    }

    private HashSet<int> insertionForbidden = [
        SimaiLexer.COMMA, SimaiLexer.KEY, SimaiLexer.SLIDE_TYPE, SimaiLexer.TOUCH_AREA, SimaiLexer.INT,
        SimaiLexer.CHART_END, SimaiLexer.MODIFIER, SimaiLexer.FALSE_EACH
    ]; // 逗号，和不确定的可能引起歧义的符号，一律不允许补充
    
    protected override IToken GetMissingSymbol(Parser recognizer)
    {
        IToken currentToken = recognizer.CurrentToken;
        
        // 不准插入insertionForbidden里提到的元素
        var insertionCandidates = GetExpectedTokens(recognizer).ToList().Where(x => !insertionForbidden.Contains(x)).ToList();
        if (insertionCandidates.Count == 0) throw new InputMismatchException(recognizer); // 等价于SingleTokenInsertion返回false的情况，recoverInline失败、转交给上层recover处理
        int minElement = insertionCandidates[0];
        
        string tokenText = minElement != -1 ? $"<missing {recognizer.Vocabulary.GetDisplayName(minElement)}>" : "<missing EOF>";
        IToken current = currentToken;
        IToken token = ((ITokenStream) recognizer.InputStream).LT(-1);
        if (current.Type == -1 && token != null)
            current = token;
        return this.ConstructToken(((ITokenStream) recognizer.InputStream).TokenSource, minElement, tokenText, current);
    }

    private static Dictionary<string, int> _literals = Enumerable.Range(1, SimaiLexer.ruleNames.Length)
        .ToDictionary(i => SimaiLexer.DefaultVocabulary.GetLiteralName(i), i => i);
    private List<int> recoverySetAllowed = [
        SimaiLexer.COMMA, SimaiLexer.FALSE_EACH, _literals["'/'"], _literals["'('"], _literals["'{'"]
    ]; // recover时，为了确保整个吞掉不合法的音符，而不是出现残缺的东西导致parser报错，只准同步到上面这些字符当中
    
    public override void Recover(Parser recognizer, RecognitionException e)
    {
        if (this.lastErrorIndex == recognizer.InputStream.Index && this.lastErrorStates != null && this.lastErrorStates.Contains(recognizer.State))
            recognizer.Consume();
        this.lastErrorIndex = recognizer.InputStream.Index;
        if (this.lastErrorStates == null)
            this.lastErrorStates = new IntervalSet(Array.Empty<int>());
        this.lastErrorStates.Add(recognizer.State);
        IntervalSet errorRecoverySet = this.GetErrorRecoverySet(recognizer);
        
        // 和上面的recoverySetAllowed取交集
        errorRecoverySet = new IntervalSet(errorRecoverySet.ToList().Where(x => recoverySetAllowed.Contains(x)).ToArray());
        
        this.ConsumeUntil(recognizer, errorRecoverySet);
    }
}

/**
 * 允许recoverInline，但是禁止大范围recover。
 */
public class ModerateErrorStrategy : LaxErrorStrategy
{
    private BailErrorStrategy _bail = new();
    
    public override void Recover(Parser recognizer, RecognitionException e) => _bail.Recover(recognizer, e); // 不准recover，只准recoverInline
}