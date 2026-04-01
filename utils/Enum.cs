namespace MuConvert.utils;

public enum TouchSeries
{
    A, B, C, D, E,
}

public enum SlideType
{
    SI_, // -
    SV_, // v（过中心）
    SCL, // 逆时针画大外圈（在simai中是 < 还是 > 取决于键位）
    SCR, // 顺时针画大外圈
    SUL, // p（逆时针画B区圈）
    SUR, // q（顺时针画B区圈）
    SXL, // pp
    SXR, // qq
    SLL, // V（首个点是逆时针找2个点）
    SLR, // V（首个点是顺时针找2个点）
    SSL, // s（逆时针方向开始）
    SSR, // z（顺时针方向开始）
    SF_, // w（wifi）
}
