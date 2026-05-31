using EasyCon.Script.Syntax;

namespace EasyCon.Script;

internal static class StdLib
{
    public const string StdSource =
        """
        FUNC TIME(): INT
            RETURN __TIME__
        ENDFUNC
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
            RETURN __OCR__($x, $y, $w, $h, "chi_sim")
        ENDFUNC

        FUNC OCR($x: INT, $y: INT, $w: INT, $h: INT, $lang: STRING): STRING
            RETURN __OCR__($x, $y, $w, $h, $lang)
        ENDFUNC

        FUNC ROI($img: STRING, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            RETURN __ROI__($img, $x, $y, $w, $h)
        ENDFUNC
        """;

    private static SyntaxTree? _stdTree;
    private static SyntaxTree? _visionTree;

    public static SyntaxTree GetStdTree() =>
        _stdTree ??= SyntaxTree.Parse(StdSource, isLib: true);

    public static SyntaxTree GetVisionTree() =>
        _visionTree ??= SyntaxTree.Parse(VisionSource, isLib: true);
}