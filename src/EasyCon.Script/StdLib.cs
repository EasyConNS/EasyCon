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

        # ---- PP-OCR 适配（纯标准库实现，宿主无特例）----
        # 依据 PP-OCRv5 官方 ONNX 导出实测：det = (px/255 - 0.25) / 0.5；rec = px/255 - 0.25。
        FUNC PPOCR_NORM($tensor: INT, $kind: STRING): INT
            IF $kind == "det"
                RETURN NET_SCALE($tensor, 1.00 / 255 / 0.50, 0 - 0.50)
            ENDIF
            RETURN NET_SCALE($tensor, 1.00 / 255, 0 - 0.25)
        ENDFUNC

        # base64 图像 → PP-OCR 归一化张量（RGB [1,3,H,W]）
        FUNC PPOCR_TENSOR($img: STRING, $w: INT, $h: INT): INT
            RETURN PPOCR_NORM(__NET_IMAGE__($img, $w, $h, "rgb"), "rec")
        ENDFUNC

        FUNC PPOCR_TENSOR($img: STRING, $w: INT, $h: INT, $kind: STRING): INT
            RETURN PPOCR_NORM(__NET_IMAGE__($img, $w, $h, "rgb"), $kind)
        ENDFUNC

        # ---- NET 句柄族（通用 ONNX；大数组驻留宿主，脚本只见句柄）----
        # mode: "gray"（[1,1,H,W]）/ "rgb"（[1,3,H,W]，CHW）；像素 0-255 原始值，
        # 归一化经 NET_SCALE 自行组合。
        FUNC NET_IMAGE($img: STRING, $w: INT, $h: INT): INT
            RETURN __NET_IMAGE__($img, $w, $h, "gray")
        ENDFUNC

        FUNC NET_IMAGE($img: STRING, $w: INT, $h: INT, $mode: STRING): INT
            RETURN __NET_IMAGE__($img, $w, $h, $mode)
        ENDFUNC

        FUNC NET_IMAGE($x: INT, $y: INT, $w: INT, $h: INT, $rw: INT, $rh: INT): INT
            RETURN __NET_IMAGE__(__CAPTURE__($x, $y, $w, $h), $rw, $rh, "gray")
        ENDFUNC

        FUNC NET_IMAGE($x: INT, $y: INT, $w: INT, $h: INT, $rw: INT, $rh: INT, $mode: STRING): INT
            RETURN __NET_IMAGE__(__CAPTURE__($x, $y, $w, $h), $rw, $rh, $mode)
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
}