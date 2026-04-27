"""Integration tests — full multi-feature examples from docs/Script.md."""

from __future__ import annotations
from easycon_grammar import ast
from easycon_script_lsp.features.formatting import format_document


def _strip_trailing_newline(s: str) -> str:
    if s.endswith("\n"):
        return s[:-1]
    return s


# ---------------------------------------------------------------------------
# 安全等待函数 (Script.md lines 560-564)
# ---------------------------------------------------------------------------

def test_safe_wait(parse):
    source = (
        "FUNC safe_wait($ms:INT)\n"
        "IF $ms > 0\n"
        "WAIT $ms\n"
        "ENDIF\n"
        "ENDFUNC\n"
    )
    prog = parse(source)
    assert len(prog.statements) == 1
    func = prog.statements[0]
    assert isinstance(func, ast.FuncDeclStmt)
    assert func.name == "safe_wait"
    assert len(func.body) == 1
    if_stmt = func.body[0]
    assert isinstance(if_stmt, ast.IfStmt)


# ---------------------------------------------------------------------------
# 最大值函数 (Script.md lines 567-574)
# ---------------------------------------------------------------------------

def test_max_function(parse):
    source = (
        "FUNC max($a:INT, $b:INT):INT\n"
        "IF $a > $b\n"
        "RETURN $a\n"
        "ELSE\n"
        "RETURN $b\n"
        "ENDIF\n"
        "ENDFUNC\n"
    )
    prog = parse(source)
    func = prog.statements[0]
    assert isinstance(func, ast.FuncDeclStmt)
    assert func.name == "max"
    if_stmt = func.body[0]
    assert isinstance(if_stmt, ast.IfStmt)
    assert len(if_stmt.else_body) == 1
    assert isinstance(if_stmt.else_body[0], ast.ReturnStmt)


# ---------------------------------------------------------------------------
# 等待图像出现 (Script.md lines 605-618)
# ---------------------------------------------------------------------------

def test_wait_for_image(parse):
    source = (
        "$max_attempts = 30\n"
        "FOR $i = 1 TO $max_attempts\n"
        "$match = @dialog_confirm\n"
        "IF $match > 90\n"
        'PRINT dialog appeared\n'
        "BREAK\n"
        "ENDIF\n"
        "WAIT 1000\n"
        "NEXT\n"
    )
    prog = parse(source)
    assert len(prog.statements) == 2
    for_stmt = prog.statements[1]
    assert isinstance(for_stmt, ast.ForStmt)
    # Body: assignment, if_block, wait — 3 items (IF body is inside the IfStmt)
    assert len(for_stmt.body) == 3


# ---------------------------------------------------------------------------
# 循环检测闪光 (Script.md lines 625-637)
# ---------------------------------------------------------------------------

def test_shiny_check_loop(parse):
    source = (
        "FOR 1000\n"
        "$shiny = @shiny_feature\n"
        "IF $shiny > 95\n"
        'ALERT found shiny\n'
        'PRINT shiny confidence: & $shiny\n'
        "BREAK\n"
        "ENDIF\n"
        "A\n"
        "WAIT 2000\n"
        "NEXT\n"
    )
    prog = parse(source)
    for_stmt = prog.statements[0]
    assert isinstance(for_stmt, ast.ForStmt)
    # Body[0] is the shiny assignment, Body[1] is the IF block
    assert isinstance(for_stmt.body[1], ast.IfStmt)
    if_stmt = for_stmt.body[1]
    # IF body: ALERT, PRINT, BREAK — break is at index 2
    assert isinstance(if_stmt.body[2], ast.BreakStmt)


# ---------------------------------------------------------------------------
# 综合应用 自动刷闪光 (Script.md lines 844-870)
# ---------------------------------------------------------------------------

def test_full_shiny_script(parse):
    source = (
        "FUNC check_shiny\n"
        "$match = @shiny_feature\n"
        "IF $match > 95\n"
        "RETURN TRUE\n"
        "ELSE\n"
        "RETURN FALSE\n"
        "ENDIF\n"
        "ENDFUNC\n"
        "FOR 100\n"
        "A\n"
        "WAIT 2000\n"
        "IF check_shiny()\n"
        'ALERT found shiny\n'
        "BREAK\n"
        "ENDIF\n"
        "HOME\n"
        "WAIT 1000\n"
        "NEXT\n"
    )
    prog = parse(source)
    func = prog.statements[0]
    assert isinstance(func, ast.FuncDeclStmt)
    assert func.name == "check_shiny"
    for_stmt = prog.statements[1]
    assert isinstance(for_stmt, ast.ForStmt)


# ---------------------------------------------------------------------------
# 多阶段识别 (Script.md lines 642-657)
# ---------------------------------------------------------------------------

def test_multi_stage_detection(parse):
    source = (
        "IF @rough_feature > 75\n"
        'PRINT found area\n'
        "IF @precise_feature > 90\n"
        'PRINT confirmed\n'
        "A\n"
        "WAIT 100\n"
        "ELSE\n"
        'PRINT not matching\n'
        "ENDIF\n"
        "ELSE\n"
        'PRINT not found\n'
        "ENDIF\n"
    )
    prog = parse(source)
    outer = prog.statements[0]
    assert isinstance(outer, ast.IfStmt)
    assert len(outer.else_body) == 1


# ---------------------------------------------------------------------------
# FFI 声明 (Script.md lines 774-786)
# ---------------------------------------------------------------------------

def test_ffi_declarations(parse):
    source = (
        'EXTERN FUNC Sleep($ms:INT):VOID FROM "kernel32.dll"\n'
        'EXTERN FUNC GetForegroundWindow():PTR FROM "user32.dll"\n'
        'EXTERN FUNC MessageBoxW($hwnd:PTR, $text:STRING, $caption:STRING, $flags:INT):INT FROM "user32.dll"\n'
        'EXTERN FUNC sqrt($x:DOUBLE):DOUBLE FROM "msvcrt.dll"\n'
    )
    prog = parse(source)
    assert len(prog.statements) == 4
    assert all(isinstance(s, ast.ExternFuncStmt) for s in prog.statements)
    assert prog.statements[0].name == "Sleep"
    assert prog.statements[1].name == "GetForegroundWindow"
    assert prog.statements[2].name == "MessageBoxW"
    assert prog.statements[3].name == "sqrt"


# ---------------------------------------------------------------------------
# WHILE loop
# ---------------------------------------------------------------------------

def test_while_loop(parse):
    source = (
        "WHILE $a > 0\n"
        "PRINT counting\n"
        "$a -= 1\n"
        "END\n"
    )
    prog = parse(source)
    while_stmt = prog.statements[0]
    assert isinstance(while_stmt, ast.WhileStmt)
    assert len(while_stmt.body) == 2


# ---------------------------------------------------------------------------
# Format round-trip for a complex script
# ---------------------------------------------------------------------------

def test_complex_format_roundtrip(parse):
    """Full round-trip test for the shiny hunting script."""
    source = (
        "FUNC check_shiny\n"
        "$match = @shiny_feature\n"
        "IF $match > 95\n"
        "RETURN TRUE\n"
        "ELSE\n"
        "RETURN FALSE\n"
        "ENDIF\n"
        "ENDFUNC\n"
        "FOR 100\n"
        "A\n"
        "WAIT 2000\n"
        "IF check_shiny()\n"
        'ALERT found shiny\n'
        "BREAK\n"
        "ENDIF\n"
        "HOME\n"
        "WAIT 1000\n"
        "NEXT\n"
    )
    prog1 = parse(source)
    formatted = format_document(prog1)
    prog2 = parse(formatted)
    assert len(prog1.statements) == len(prog2.statements)
    assert prog2.statements[0].name == "check_shiny"


# ---------------------------------------------------------------------------
# Nested loops with BREAK level (Script.md lines 475-482)
# ---------------------------------------------------------------------------

def test_nested_break_level(parse):
    source = (
        "FOR $i = 1 TO 10\n"
        "FOR $j = 1 TO 10\n"
        "IF $emergency\n"
        "BREAK 2\n"
        "ENDIF\n"
        "NEXT\n"
        "NEXT\n"
    )
    prog = parse(source)
    outer = prog.statements[0]
    assert isinstance(outer, ast.ForStmt)
    inner = outer.body[0]
    assert isinstance(inner, ast.ForStmt)
    break_stmt = inner.body[0].body[0]
    assert isinstance(break_stmt, ast.BreakStmt)
    assert break_stmt.level == 2
