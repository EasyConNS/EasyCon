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
    [Description("严格匹配")]
    StrictMatch = 6,
    [Description("随机严格匹配")]
    StrictMatchRND = 7,
    [Description("透明度匹配")]
    OpacityDiff = 8,
    [Description("相似匹配")]
    SimilarMatch = 9,
}