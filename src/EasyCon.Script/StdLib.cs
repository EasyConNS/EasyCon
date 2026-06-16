using EasyCon.Script.Syntax;

namespace EasyCon.Script;

internal static class StdLib
{
    public const string StdSource =
        """
        # 标准文件句柄
        $STDIN = 0
        $STDOUT = 1
        $STDERR = 2

        FUNC print($output: STRING)
            FWRITE $STDOUT, $output
        ENDFUNC
        FUNC PRINT($output: STRING)
            FWRITE $STDOUT, $output
        ENDFUNC

        # 返回脚本运行时常时间戳，单位毫秒
        FUNC time(): INT
            RETURN __TIME__
        ENDFUNC
        FUNC TIME(): INT
            RETURN __TIME__
        ENDFUNC

        # __FILE__指向当前执行脚本所在目录，不含脚本文件名
        """;

    public const string VisionSource =
        """
        FUNC FRAME(): STRING
            RETURN __CAPTURE__(-1, -1, -1, -1)
        ENDFUNC

        FUNC FRAME($x: INT, $y: INT, $w: INT, $h: INT): STRING
            RETURN __CAPTURE__($x, $y, $w, $h)
        ENDFUNC

        FUNC OCR($x: INT, $y: INT, $w: INT, $h: INT): STRING
            RETURN OCR($x, $y, $w, $h, "chi_sim")
        ENDFUNC

        FUNC OCR($x: INT, $y: INT, $w: INT, $h: INT, $lang: STRING): STRING
            OCR_INIT $lang
            RETURN __OCR__($x, $y, $w, $h, $lang)
        ENDFUNC

        FUNC ROI($img: STRING, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            RETURN __ROI__($img, $x, $y, $w, $h)
        ENDFUNC

        FUNC OCR_INIT($lang: STRING): BOOL
            RETURN __OCR_INIT__($lang, __APP__ + "/Tessdata", "DEFAULT", "SINGLE_LINE")
        ENDFUNC

        #FUNC OCR_INIT($lang: STRING, $dataPath: STRING): BOOL
        #    RETURN __OCR_INIT__($lang, $dataPath, "DEFAULT", "SINGLE_LINE")
        #ENDFUNC

        #FUNC OCR_INIT($lang: STRING, $dataPath: STRING, $engineMode: STRING, $psmode: STRING): BOOL
        #    RETURN __OCR_INIT__($lang, $dataPath, $engineMode, $psmode)
        #ENDFUNC
        """;

    private static SyntaxTree? _stdTree;
    private static SyntaxTree? _visionTree;

    public static SyntaxTree GetStdTree() =>
        _stdTree ??= SyntaxTree.Parse(StdSource, isLib: true);

    public static SyntaxTree GetVisionTree() =>
        _visionTree ??= SyntaxTree.Parse(VisionSource, isLib: true);
}