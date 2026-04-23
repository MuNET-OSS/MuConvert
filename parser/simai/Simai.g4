/*
 * Simai的ANTLR4语法定义。
 * Simai官方文档：https://w.atwiki.jp/simai/pages/1002.html
 * 本语法同时实现了一些“常见非标准语法”以确保解析的鲁棒性。
 */
grammar Simai;

options { language=CSharp; }

// ---------------------------------------------------------------------------
// 词法
// ---------------------------------------------------------------------------

WS: [ \t\r\n]+ -> channel(HIDDEN);
COMMENT: '||' ~[\r\n]* -> channel(HIDDEN);

COMMA: ',';

TAP_TO_STAR: '$$' | '$';
STAR_TO_TAP: '@';
NO_STAR: '?' | '!';

KEY: [1-8];
TOUCH_AREA: 'A' [1-8] | 'B' [1-8] | 'C' [1-2]? | 'D' [1-8] | 'E' [1-8];

SLIDE_TYPE: '-' | 'v' | '<' | '>' | '^' | 'p' | 'q' | 'pp' | 'qq' | 's' | 'z' | 'w' | 'V' KEY;  // 只有V后面需要多跟一个键位号
slideType: SLIDE_TYPE;

INT: [0-9];

int: (KEY | INT)+;
number: int ('.' int)?;

CHART_END: 'E';// 谱面结束那个E

MODIFIER: [bxf]; // 语法层不去检查modifier和tap/hold的搭配和合理性，都丢给语义层去搞
modifiers: (MODIFIER | TAP_TO_STAR | STAR_TO_TAP | NO_STAR)*;

// ---------------------------------------------------------------------------
// 语法
// ---------------------------------------------------------------------------

chart: (notations COMMA)* CHART_END? EOF;

// 同一时刻的所有标记，包括note标记、bpm标记等等
notations: (bpmTag | absulouteStepTag | metTag)* noteGroup?;

noteGroup: note eachNote*;
FALSE_EACH: '`';
eachSeparators: '/' | FALSE_EACH+;
eachNote: eachSeparators note;

bpmTag: (lp+='(')+ number (rp+=')')+;
absulouteStepTag: (lp+='{')+ '#' number (rp+='}')+;
metTag: (lp+='{')+ int (rp+='}')+;

note: slide (sharedHeadSlide)* | tap | KEY+ | hold | touch | touchHold; // tap+是因为，simai允许123这种语法、和1/2/3是等价的，但仅限tap之间。

tap: KEY modifiers;

// 出于兼容性（以及simai本身设计的不合理？）考虑，会到处放置很多的modifiers以确保都能解析，解析的时候要把所有的modifiers取并集。
hold: KEY modifiers 'h' modifiers (duration modifiers)?;

touch: TOUCH_AREA modifiers;

touchHold: TOUCH_AREA modifiers 'h' modifiers (duration modifiers)?;

duration: (lp+='[')+ (beats | '#' number) (rp+=']')+;
beats: int ':' int;
    
slideDuration: (lp+='[')+ (
        beats
        | '#' number
        | waitTime '##' asBpm '#' (beats | number)
        | waitTime '##' (beats | number)
        | asBpm '#' (beats | number)
    ) (rp+=']')+;
waitTime: number;
asBpm: number;

slide: tap slideBody;
sharedHeadSlide: '*' slideBody;

slideBody // 根据Simai文档规定，分为两种情况
    : slideType KEY (slideType KEY)* modifiers slideDuration modifiers // 只有最后一段星星有时间指定
    | slideType KEY (slideDuration slideType KEY)* modifiers slideDuration modifiers // 每一段星星都有独立的时间指定
    ;
