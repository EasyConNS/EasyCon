using System.ComponentModel;

public enum SearchMethod
{
    [Description("平方差匹配")]
    SqDiff = 0,
    [Description("标准差匹配")]
    SqDiffNormed = 1,
    [Description("相关匹配")]
    CCorr = 2,
    [Description("标准相关匹配")]
    CCorrNormed = 3,
    [Description("相关系数匹配")]
    CCoeff = 4,
    [Description("标准相关系数匹配")]
    CCoeffNormed = 5,
    [Description("XY平均值边缘检测")]
    EdgeDetectXY = 6,
    [Description("拉普拉斯边缘检测")]
    EdgeDetectLaplacian = 7,
}