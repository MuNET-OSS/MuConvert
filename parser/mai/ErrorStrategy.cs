using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Misc;
using MuConvert.utils;
using static MuConvert.utils.Alert.LEVEL;
using L = MuConvert.Antlr.SimaiLexer;
using P = MuConvert.Antlr.SimaiParser;
using Utils = MuConvert.utils.Utils;

namespace MuConvert.mai;

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
                    line: line, relevantNote: RelevantNote(parser.Context, recognizer, offendingSymbol)));
                return;
            }
            else if (msg.StartsWith("missing"))
            {
                simaiParser.alerts.Add(new Alert(Warning, 
                    string.Format(Locale.RecoverInlineMissingToken, GetTokenErrorDisplay(offendingSymbol), parser.GetExpectedTokens().ToString(parser.Vocabulary)), 
                    line: line, relevantNote: RelevantNote(parser.Context, recognizer, offendingSymbol)));
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
        simaiParser.alerts.Add(new Alert(level, message, line: line, relevantNote: RelevantNote(parser.Context, recognizer, offendingSymbol)));
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
    private static string? RelevantNote(RuleContext? context, IRecognizer? recognizer, IToken? offendingSymbol)
    {
        if (recognizer != null && offendingSymbol is { TokenIndex: >= 0 })
        {
            var i = offendingSymbol.TokenIndex;
            var stream = (ITokenStream)recognizer.InputStream;
            // 从offendingSymbol的位置向前、后分别找边界（逗号、双押/、伪双`），取中间的内容作为relevantNote
            int left = i-1, right = i+1;
            string RegionText() => stream.GetText(new Interval(left + 1, right - 1));
            bool IsBoundary(int type) => type == L.COMMA || type == L.FALSE_EACH || type == Utils.TokenType("/");
            
            while (left >= 0 && !IsBoundary(stream.Get(left).Type)) left--;
            while (left >= 0 || right < stream.Size) // 防止两边都到头了之后，死循环
            {
                while (right < stream.Size && !IsBoundary(stream.Get(right).Type)) right++;
                if (RegionText().Length > 5) break; // 相关区域中至少应该大于5个字符，否则不准break
                else if (left >= 0) left--; // 此时应强制left向左动一格，再继续向左找逗号
                
                while (left >= 0 && !IsBoundary(stream.Get(left).Type)) left--;
                if (RegionText().Length > 5) break;
                else if (right < stream.Size) right++;
            }
            return RegionText();
        }
        
        // 否则（recognizer == null 或 offendingSymbol不是正常token），fallback回原来的办法
        string? result = null;
        while (context != null)
        {
            if (context.GetText().Length >= 5)
            {
                result = context.GetText();
                break;
            }
            context = context.Parent;
        }
        return result;
    }

    # region 用于暴露DefaultErrorStrategy内部的GetTokenErrorDisplay函数
    private class _ES : DefaultErrorStrategy
    {
        public new string GetTokenErrorDisplay(IToken t) => base.GetTokenErrorDisplay(t);
        public new string EscapeWSAndQuote(string s) => base.EscapeWSAndQuote(s);
    }
    private static _ES _es = new(); // 仅用作调用里面的方法
    internal static string GetTokenErrorDisplay(IToken t) => _es.GetTokenErrorDisplay(t);
    private static string EscapeWSAndQuote(string s) => _es.EscapeWSAndQuote(s);
    # endregion
}

/**
 * 最宽松的ErrorStrategy，尽全力恢复不让谱面整个垮掉
 */
public class LaxErrorStrategy(SimaiParser simaiParser) : DefaultErrorStrategy
{
    protected override IToken SingleTokenDeletion(Parser recognizer)
    {
        if (recognizer.CurrentToken?.Type == L.COMMA) return null!; // 不准删逗号
        return base.SingleTokenDeletion(recognizer);
    }

    private HashSet<int> insertionForbidden = [
        L.KEY, L.SLIDE_TYPE, L.TOUCH_AREA, L.INT, L.CHART_END, L.FALSE_EACH, 
        L.MODIFIER, L.NO_STAR, L.STAR_TO_TAP, L.TAP_TO_STAR
    ]; // 不确定的可能引起歧义的符号，一律不允许补充
    private HashSet<int> insertCommaOnlyWhen = [Utils.TokenType("("), Utils.TokenType("{")];
    
    protected override IToken GetMissingSymbol(Parser recognizer)
    {
        IToken currentToken = recognizer.CurrentToken;
        
        // 不准插入insertionForbidden里提到的元素
        var insertionCandidates = GetExpectedTokens(recognizer).ToList()
            .Where(x => !insertionForbidden.Contains(x)) // 上述黑名单中的不准插入
            .Where(x => x != L.COMMA || insertCommaOnlyWhen.Contains(recognizer.InputStream.LA(1))) // 逗号，只准在后面跟着的是'('或'{'的情况下才准插入
            .ToList();
        if (insertionCandidates.Count == 0) throw new InputMismatchException(recognizer); // 等价于SingleTokenInsertion返回false的情况，recoverInline失败、转交给上层recover处理
        int minElement = insertionCandidates[0];
        
        string tokenText = minElement != -1 ? $"<missing {recognizer.Vocabulary.GetDisplayName(minElement)}>" : "<missing EOF>";
        IToken current = currentToken;
        IToken token = ((ITokenStream) recognizer.InputStream).LT(-1);
        if (current.Type == -1 && token != null)
            current = token;
        return this.ConstructToken(((ITokenStream) recognizer.InputStream).TokenSource, minElement, tokenText, current);
    }

    protected override void ReportMissingToken(Parser recognizer)
    {
        try
        { // 如果稍后GetMissingSymbol时将会被拒绝（抛异常），则跳过警告日志的打印，反正会去打印InputMismatch
            GetMissingSymbol(recognizer);
            base.ReportMissingToken(recognizer);
        } 
        catch (InputMismatchException) {} // ignored 
    }

    private List<int> recoverySetAllowed = [
        L.COMMA, L.FALSE_EACH, Utils.TokenType("/"), Utils.TokenType("("), Utils.TokenType("{")
    ]; // recover时，为了确保整个吞掉不合法的音符，而不是出现残缺的东西导致parser报错，只准同步到上面这些字符当中
    
    public override void Recover(Parser recognizer, RecognitionException e)
    {
        if (SpecificRecover((P)recognizer, e)) return; // 是特定类型的错误、已通过SpecificRecover修复完成
        
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

    /**
     * 尝试修复一些特定类型的错误。
     * - beats中，':'误打为'-'
     */
    protected virtual bool SpecificRecover(P parser, RecognitionException e)
    {
        var ctx = parser.Context;
        var rule = ctx.RuleIndex;
        var expected = e.GetExpectedTokens()?.ToSet();
        if (expected == null) return false;
        
        if (rule == P.RULE_beats && e is InputMismatchException && 
            e.OffendingToken.Text == "-" && expected.Contains(Utils.TokenType(":")))
        { // [4:1]中，错把:打成-了
            simaiParser.alerts.Last().Level = Warning; // Error改为Warning，因为恢复了
            simaiParser.alerts.Last().Description += Locale.Fixed;
            parser.Match(L.SLIDE_TYPE);
            ctx.exception = null;
            parser.@int();
            return true;
        }
        return false;
    }

    /**
     * 重新执行func对应的解析函数，并把结果合并进oldContext。
     */
    protected void Rerun<T>(P parser, Func<T> func, T oldContext) where T : ParserRuleContext
    {
        // 缓存全局变量，便于稍后恢复现场
        var savedState = parser.State;
        var savedContext = parser.Context;
        // 设置现场为父级的状态，从而为再次调用解析函数做好准备
        parser.State = oldContext.invokingState;
        parser.Context = (ParserRuleContext)oldContext.Parent;
        // 再次调用解析函数，得到的结果（子节点）合并进oldContext、新节点删除之
        var newContext = func();
        parser.Context?.RemoveLastChild(); // func返回时会调用ExitRule，从而context还是oldContext.Parent不变，直接RemoveLastChild()即可移除newContext
        oldContext.children = oldContext.children.Concat(newContext.children).ToList();
        oldContext.exception = null;
        // 恢复现场
        parser.State = savedState;
        parser.Context = savedContext;
    }
}

/**
 * 允许recoverInline，但是禁止大范围recover。
 */
public class ModerateErrorStrategy(SimaiParser simaiParser) : LaxErrorStrategy(simaiParser)
{
    private BailErrorStrategy _bail = new();
    
    public override void Recover(Parser recognizer, RecognitionException e)
    {
        if (SpecificRecover((P)recognizer, e)) return; // 是特定类型的错误、已通过SpecificRecover修复完成，则ok
        _bail.Recover(recognizer, e); // 否则，不准recover（通过bail strategy的recover方法来抛异常），只准recoverInline
    }
}

// antlr自带的BailErrorStrategy，在RecoverInline时不会调用ErrorListener。本类只是Fix了这一点。
public class FixedBailErrorStrategy : BailErrorStrategy
{
    public override IToken RecoverInline(Parser recognizer) => throw new InputMismatchException(recognizer);
}

/**
 * 用于覆盖ANTLR Parser中的分支预测逻辑，实现更可控的分支导向和异常处理/修复。
 * 目前实现的内容：
 * - 对于缺少h的hold note，如`7[4:1]`，确保它能被分支预测成hold note，而不是tap note。
 */
public class PatchedATNSimulator : ParserATNSimulator
{
    public PatchedATNSimulator(Parser parser, ATN atn, DFA[] decisionToDfa, PredictionContextCache sharedContextCache)
        : base(parser, atn, decisionToDfa, sharedContextCache) { }

    public override int AdaptivePredict(ITokenStream input, int decision, ParserRuleContext outerContext)
    {
        int la = input.LA(1);
        if (outerContext?.RuleIndex == P.RULE_note && la is P.KEY or P.TOUCH_AREA)
        { // 对 note() 的分支预测进行修复，确保缺少h的hold note也能被正确预测为hold而不是tap
            // 跳过所有的modifiers，找modifiers的下一个token
            int i;
            for (i = 2; Utils.IsModifier(input.LA(i)); i++) {}
            int tk = input.LA(i);
            // 如果是 '[' ，则一定是缺少h的hold note的情况。只有这种情况需要特判
            // （不然的话，如果是tap则是逗号，如果是slide则是'-'等星星类型符号，如果是'h'则是正常没有缺少'h'的hold。这些情况都不需要特殊处理，正常走原逻辑即可。）
            if (tk == Utils.TokenType("["))
            {
                return la == P.TOUCH_AREA ? 6 : 4; // 6是touch hold，4是hold
            }
        }
        
        return base.AdaptivePredict(input, decision, outerContext);
    }
}

class PatchedAntlrSimaiParser : P
{
    public PatchedAntlrSimaiParser(ITokenStream input) : base(input)
    {
        Interpreter = new PatchedATNSimulator(this, _ATN, decisionToDFA, sharedContextCache);
    }
}
