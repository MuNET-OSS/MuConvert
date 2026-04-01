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

TAP_TO_STAR: '$$' | '$';
STAR_TO_TAP: '?' | '!';

SIMPLE_SLIDE: '-' | 'v' | '<' | '>' | '^' | 'p' | 'q' | 'pp' | 'qq' | 's' | 'z' | 'w';

TOUCH_AREA: 'A' [1-8] | 'B' [1-8] | 'C' [1-2]? | 'D' [1-8] | 'E' [1-8];

KEY: [1-8];

INT: [0-9]+;
FLOAT: [0-9]+ ('.' [0-9]+)?;
number: KEY | INT | FLOAT;

CHART_END: 'E';// 谱面结束那个E

MODIFIER: [bxf]; // 语法层不去检查modifier和tap/hold的搭配和合理性，都丢给语义层去搞

modifiers: (MODIFIER | TAP_TO_STAR | STAR_TO_TAP)*;

// ---------------------------------------------------------------------------
// 语法
// ---------------------------------------------------------------------------

chart: (notations ',')* CHART_END? EOF;

notations // 同一时刻的所有标记，包括note标记、bpm标记等等
    : (bpmTag 
    | absulouteStepTag // 暂不支持，但这毕竟是合法的语法
    | metTag
    | noteGroup
    )*;
    
noteGroup: note (eachNote | falseEachNote)*;

eachNote: '/' note;
falseEachNote: '`' note;

bpmTag: '(' number ')';
absulouteStepTag: '{' '#' number '}';
metTag: '{' INT '}';

note: slide+ | tap+ | hold | touch | touchHold; // tap+是因为，simai允许123这种语法、和1/2/3是等价的，但仅限tap之间。

tap: KEY modifiers;

// 出于兼容性（以及simai本身设计的不合理？）考虑，会到处放置很多的modifiers以确保都能解析，解析的时候要把所有的modifiers取并集。
hold: KEY modifiers 'h' modifiers duration modifiers;

touch: TOUCH_AREA modifiers;

touchHold: TOUCH_AREA modifiers 'h' modifiers duration modifiers;

duration: '[' (beats | '#' number) ']';
beats: INT ':' INT;
    
slideDuration: '[' (
        beats
        | '#' number
        | waitTime '##' asBpm '#' (beats | number)
        | waitTime '##' (beats | number)
        | asBpm '#' (beats | number)
    ) ']';
waitTime: number;
asBpm: number;

slide: (tap | '*') slideSegment* finalSlideSegment;
slideSegment: slideType KEY slideDuration?;
finalSlideSegment: slideType KEY modifiers slideDuration? modifiers;

slideType: SIMPLE_SLIDE | 'V' KEY; // 只有V后面需要多跟一个键位号
