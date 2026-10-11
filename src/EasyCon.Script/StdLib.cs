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

        # 返回脚本运行时长时间戳，单位毫秒（运行时值 __TIME__ 编译为 TIME syscall；
        # 脚本面仅此一个入口——直接写 __TIME__ 亦等价）
        FUNC TIME(): INT
            RETURN __TIME__
        ENDFUNC

        # ---- 实用工具（std 定 API/语义，热路径打洞加速）----

        # JSON 路径查询（.a.b[0]）；查询语法演化经本层
        FUNC JQ($json: STRING, $query: STRING): STRING
            RETURN __JQ__($json, $query)
        ENDFUNC

        # 字节→文本解码（UTF-8/UTF-16 多字节语义由宿主保证——std 无 CHR 原语不可自实现）。
        # 仅接受 byte 数组（byte[] 的脚本侧来源待后续宿主原语提供）。
        FUNC ENCODE($array: BYTE[]): STRING
            RETURN __ENCODE__($array, "utf8")
        ENDFUNC
        FUNC ENCODE($array: BYTE[], $type: STRING): STRING
            RETURN __ENCODE__($array, $type)
        ENDFUNC

        # ---- 文件便捷件（std 组合低级句柄原语；IFileSystem 契约保证无效句柄安全：
        # FOPEN 失败 -1、FREAD 失败空串、FWRITE 失败 -1、FCLOSE 无效句柄 no-op，无需脚本守卫）----

        FUNC READFILE($path: STRING): STRING
            $h = FOPEN($path, "r")
            $s = FREAD($h, 0)
            FCLOSE $h
            RETURN $s
        ENDFUNC

        FUNC WRITEFILE($path: STRING, $data: STRING)
            $h = FOPEN($path, "w")
            FWRITE $h, $data
            FCLOSE $h
        ENDFUNC

        FUNC APPENDFILE($path: STRING, $data: STRING)
            $h = FOPEN($path, "a")
            FWRITE $h, $data
            FCLOSE $h
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
            RETURN OCR($x, $y, $w, $h, "chi_sim")
        ENDFUNC

        FUNC OCR($x: INT, $y: INT, $w: INT, $h: INT, $lang: STRING): STRING
            OCR_INIT $lang
            RETURN __TESSERACT__($x, $y, $w, $h, $lang)
        ENDFUNC

        FUNC ROI($img: STRING, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            RETURN __ROI__($img, $x, $y, $w, $h)
        ENDFUNC

        # ---- PP-OCR 适配（纯标准库实现，宿主无特例）----
        # 依据 PP-OCRv5 官方 ONNX 导出实测：det = (px/255 - 0.25) / 0.5；rec = px/255 - 0.25。
        FUNC PPOCR_NORM($tensor: PTR, $kind: STRING): PTR
            IF $kind == "det"
                RETURN NET_SCALE($tensor, 1.00 / 255 / 0.50, 0 - 0.50)
            ENDIF
            RETURN NET_SCALE($tensor, 1.00 / 255, 0 - 0.25)
        ENDFUNC

        # base64 图像 → PP-OCR 归一化张量（RGB [1,3,H,W]）
        FUNC PPOCR_TENSOR($img: STRING, $w: INT, $h: INT): PTR
            RETURN PPOCR_NORM(__NET_IMAGE__($img, $w, $h, "rgb"), "rec")
        ENDFUNC

        FUNC PPOCR_TENSOR($img: STRING, $w: INT, $h: INT, $kind: STRING): PTR
            RETURN PPOCR_NORM(__NET_IMAGE__($img, $w, $h, "rgb"), $kind)
        ENDFUNC

        # ---- NET 句柄族（通用 ONNX；大数组驻留宿主，脚本只见句柄）----
        # mode: "gray"（[1,1,H,W]）/ "rgb"（[1,3,H,W]，CHW）；像素 0-255 原始值，
        # 归一化经 NET_SCALE 自行组合。
        FUNC NET_IMAGE($img: STRING, $w: INT, $h: INT): PTR
            RETURN __NET_IMAGE__($img, $w, $h, "gray")
        ENDFUNC

        FUNC NET_IMAGE($img: STRING, $w: INT, $h: INT, $mode: STRING): PTR
            RETURN __NET_IMAGE__($img, $w, $h, $mode)
        ENDFUNC

        FUNC NET_IMAGE($x: INT, $y: INT, $w: INT, $h: INT, $rw: INT, $rh: INT): PTR
            RETURN __NET_IMAGE__(__CAPTURE__($x, $y, $w, $h), $rw, $rh, "gray")
        ENDFUNC

        FUNC NET_IMAGE($x: INT, $y: INT, $w: INT, $h: INT, $rw: INT, $rh: INT, $mode: STRING): PTR
            RETURN __NET_IMAGE__(__CAPTURE__($x, $y, $w, $h), $rw, $rh, $mode)
        ENDFUNC

        FUNC OCR_INIT($lang: STRING): BOOL
            RETURN __TESSERACT_INIT__($lang, __APP__ + "/Tessdata", "DEFAULT", "SINGLE_LINE")
        ENDFUNC

        # ---- PP-OCR rec 行识别配方（ppocr 不走 OCR 面，经 NET 族 + 本配方；宿主打洞 CTC 折叠）----
        # rec 输入行高约定 32px，宽度按宽高比换算并 8 对齐（PP-OCR rec 配方常数）。
        FUNC PP_REC_WIDTH($w: INT, $h: INT): INT
            RETURN ($w * 32 / $h + 7) / 8 * 8
        ENDFUNC

        # CTC 贪心折叠最近一次 NET_RUNH 输出 [1,T,C]：连续同类别去重、blank 帧重置、
        # charset 窗口 [base, base+len) 外类别跳过。打洞实现；替代解码策略可在脚本侧
        # 经 NET_ROWS + NET_ARGMAX 逐帧遍历自行实现。
        FUNC CTC_FOLD($blank: INT, $charset: STRING, $base: INT): STRING
            RETURN __CTC_FOLD__($blank, $charset, $base)
        ENDFUNC

        # PP-OCRv5 rec：固定 ROI → 数字串（'0'-'9' 字典下标 16163-16172，blank=0，
        # 配方常数与 PPOCR_NORM 同源：PP-OCRv5 官方字典实测）。
        # 忠实输出折叠结果（含模型伪影的前导零）；数值化/去前导零属游戏策略，调用方 INT() 自理。
        FUNC PP_OCR_DIGITS($net: PTR, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            $img = NET_IMAGE($x, $y, $w, $h, PP_REC_WIDTH($w, $h), 32, "rgb")
            $t = PPOCR_NORM($img, "rec")
            $n = NET_RUNH($net, $t)
            $text = CTC_FOLD(0, "0123456789", 16163)
            NET_FREE $t
            NET_FREE $img
            RETURN $text
        ENDFUNC
        """;
}