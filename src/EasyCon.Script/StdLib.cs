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
        # 依据 PP-OCRv5 / PP-OCRv6 官方 ONNX 导出实测同参：det = (px/255 - 0.25) / 0.5；rec = px/255 - 0.25。
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

        # 本地图像文件 → 张量（与 NET_IMAGE 同管线同 mode 词表；'c' 后缀 = cover + 居中裁剪）
        FUNC NET_IMAGE_FILE($path: STRING, $w: INT, $h: INT): PTR
            RETURN __NET_IMAGE_FILE__($path, $w, $h, "gray")
        ENDFUNC

        FUNC NET_IMAGE_FILE($path: STRING, $w: INT, $h: INT, $mode: STRING): PTR
            RETURN __NET_IMAGE_FILE__($path, $w, $h, $mode)
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

        # rec 推理通用步骤：采集 ROI → rec 宽度换算（PP_REC_WIDTH）→ 归一化（PPOCR_NORM）
        # → 推理。输出驻留宿主线程槽，由任意解码原语消费（CTC_FOLD / NET_ARGMAX+NET_LABEL /
        # NET_OUT 自实现策略）。返回输出元素个数（0 = 失败）。
        FUNC PP_OCR_INFER($net: PTR, $x: INT, $y: INT, $w: INT, $h: INT): INT
            $img = NET_IMAGE($x, $y, $w, $h, PP_REC_WIDTH($w, $h), 32, "rgb")
            $t = PPOCR_NORM($img, "rec")
            $n = NET_RUNH($net, $t)
            NET_FREE $t
            NET_FREE $img
            RETURN $n
        ENDFUNC

        # ---- 共享采集形态：同帧多区域只采集一次。$frame 为整帧 base64（如 FRAME() 全屏），
        #      区域坐标相对该帧，逐区域 ROI 裁剪在宿主内完成（省 N-1 次采集 + base64 编解码）。
        #      命名与采集形态同名重载（第二参 STRING = 帧），约定同 NET_IMAGE 的 base64 形态。
        FUNC PP_OCR_INFER($net: PTR, $frame: STRING, $x: INT, $y: INT, $w: INT, $h: INT): INT
            $roi = ROI($frame, $x, $y, $w, $h)
            $img = NET_IMAGE($roi, PP_REC_WIDTH($w, $h), 32, "rgb")
            $t = PPOCR_NORM($img, "rec")
            $n = NET_RUNH($net, $t)
            NET_FREE $t
            NET_FREE $img
            RETURN $n
        ENDFUNC

        # PP-OCR rec：固定 ROI → 数字串。$base 由脚本按所用模型代际指定（'0' 的 class 下标：
        # v5 字典 16163 / v6 字典 33，std 不硬编码任何代际常数；blank=0，'0'-'9' 需在字典中连续）。
        # 忠实输出折叠结果（含模型伪影的前导零）；数值化/去前导零属游戏策略，调用方 INT() 自理。
        FUNC PP_OCR_DIGITS($net: PTR, $base: INT, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            $n = PP_OCR_INFER($net, $x, $y, $w, $h)
            RETURN CTC_FOLD(0, "0123456789", $base)
        ENDFUNC

        FUNC PP_OCR_DIGITS($net: PTR, $frame: STRING, $base: INT, $x: INT, $y: INT, $w: INT, $h: INT): STRING
            $n = PP_OCR_INFER($net, $frame, $x, $y, $w, $h)
            RETURN CTC_FOLD(0, "0123456789", $base)
        ENDFUNC

        # PP-OCR rec 通用文本：PP_OCR_INFER + 标签表层整字典折叠（NET_LABELS 行号即索引，
        # class c → NET_LABEL(c-1)）。去重/blank 重置与 CTC_FOLD 洞同语义，无窗口过滤
        # （数字子集场景用 PP_OCR_DIGITS）。加载字典会整体替换 NET_LABELS 全局表；
        # 解码策略需定制时可在脚本侧经 NET_ROWS + NET_ARGMAX 自实现。
        FUNC PP_OCR_TEXT($net: PTR, $x: INT, $y: INT, $w: INT, $h: INT, $dictPath: STRING): STRING
            $n = PP_OCR_INFER($net, $x, $y, $w, $h)
            NET_LABELS $dictPath
            $text = ""
            $prev = -1
            $i = 0
            WHILE $i < NET_ROWS()
                $cls = NET_ARGMAX($i)
                IF $cls == 0
                    $prev = -1
                ELSE
                    IF $cls != $prev
                        $text = $text + NET_LABEL($cls - 1)
                    ENDIF
                    $prev = $cls
                ENDIF
                $i = $i + 1
            END
            RETURN $text
        ENDFUNC

        FUNC PP_OCR_TEXT($net: PTR, $frame: STRING, $x: INT, $y: INT, $w: INT, $h: INT, $dictPath: STRING): STRING
            $n = PP_OCR_INFER($net, $frame, $x, $y, $w, $h)
            NET_LABELS $dictPath
            $text = ""
            $prev = -1
            $i = 0
            WHILE $i < NET_ROWS()
                $cls = NET_ARGMAX($i)
                IF $cls == 0
                    $prev = -1
                ELSE
                    IF $cls != $prev
                        $text = $text + NET_LABEL($cls - 1)
                    ENDIF
                    $prev = $cls
                ENDIF
                $i = $i + 1
            END
            RETURN $text
        ENDFUNC

        # ---- PP-OCR det 文本找框（det 全图一次 → 框 → rec 逐框；det 模型经 NET_LOAD 由脚本显式加载）----
        # 限边缩放：scale = 960/max(w,h)，小图不放大（钳 1）；rw/rh 32 对齐上取整（pad 在右/下，
        # 不影响逆映射）。__DET_BOXES__ 阈值 0.30（PaddleOCR det_db_thresh 惯例），逆映射
        # screen = det/scale + roi，±2px unclip，阅读序缓存。返回框数（-1 = 输出缺失/形状错）。
        FUNC PP_DET_SCALE($w: INT, $h: INT): DOUBLE
            $s = 960.00 / $w
            IF $h > $w
                $s = 960.00 / $h
            ENDIF
            IF $s > 1.00
                $s = 1.00
            ENDIF
            RETURN $s
        ENDFUNC

        FUNC PP_DET_W($w: INT, $h: INT): INT
            RETURN (INT($w * PP_DET_SCALE($w, $h) + 0.5) + 31) / 32 * 32
        ENDFUNC

        FUNC PP_DET_H($w: INT, $h: INT): INT
            RETURN (INT($h * PP_DET_SCALE($w, $h) + 0.5) + 31) / 32 * 32
        ENDFUNC

        FUNC PP_DET_FIND($net: PTR, $x: INT, $y: INT, $w: INT, $h: INT): INT
            $s = PP_DET_SCALE($w, $h)
            $img = NET_IMAGE($x, $y, $w, $h, PP_DET_W($w, $h), PP_DET_H($w, $h), "rgb")
            $t = PPOCR_NORM($img, "det")
            $n = NET_RUNH($net, $t)
            $count = __DET_BOXES__(0.30, $s, $x, $y, PP_DET_W($w, $h), PP_DET_H($w, $h))
            NET_FREE $t
            NET_FREE $img
            RETURN $count
        ENDFUNC

        FUNC PP_DET_FIND($net: PTR, $frame: STRING, $x: INT, $y: INT, $w: INT, $h: INT): INT
            $s = PP_DET_SCALE($w, $h)
            $roi = ROI($frame, $x, $y, $w, $h)
            $img = NET_IMAGE($roi, PP_DET_W($w, $h), PP_DET_H($w, $h), "rgb")
            $t = PPOCR_NORM($img, "det")
            $n = NET_RUNH($net, $t)
            $count = __DET_BOXES__(0.30, $s, $x, $y, PP_DET_W($w, $h), PP_DET_H($w, $h))
            NET_FREE $t
            NET_FREE $img
            RETURN $count
        ENDFUNC

        # ---- YOLO26 适配（端到端免 NMS 导出；输出 [1,300,6] = x1,y1,x2,y2,conf,cls）----
        # letterbox 几何在此计算（配方持语义），与宿主 __NET_IMAGE__ "letter" 模式同式：
        # scale = 640/max(w,h)，newW/H = INT(w*scale + 0.5)，pad = (640-new)/2（整除地板）。
        # 端到端 stride 校验（末维 = 6，防 end2end=False 导出 [1,8400,·] 错位解码）在此配方层：
        # stride = 输出元素数 / NET_ROWS()。__YOLO_DECODE__ 只做过滤 + 逆映射 (v - pad)/scale
        # + roi + 内容区裁剪（Ultralytics predict 同约定），命中缓存经 YOLO_COUNT / YOLO_CLS /
        # YOLO_SCORE / YOLO_X1..Y2 读取（结果按分数降序）。返回命中数（-1 = stride 不符/输出缺失）。
        FUNC YOLO_DETECT($net: PTR, $x: INT, $y: INT, $w: INT, $h: INT, $thresh: DOUBLE): INT
            $scale = 640.00 / $w
            IF $h > $w
                $scale = 640.00 / $h
            ENDIF
            $padX = (640 - INT($w * $scale + 0.5)) / 2
            $padY = (640 - INT($h * $scale + 0.5)) / 2
            $img = NET_IMAGE($x, $y, $w, $h, 640, 640, "letter")
            $norm = NET_SCALE($img, 1.00 / 255, 0)
            $n = NET_RUNH($net, $norm)
            IF $n <= 0
                NET_FREE $norm
                NET_FREE $img
                RETURN 0
            ENDIF
            $rows = NET_ROWS()
            IF $n / $rows != 6
                NET_FREE $norm
                NET_FREE $img
                RETURN -1
            ENDIF
            $count = __YOLO_DECODE__($thresh, $scale, $padX, $padY, $x, $y)
            NET_FREE $norm
            NET_FREE $img
            RETURN $count
        ENDFUNC
        """;
}