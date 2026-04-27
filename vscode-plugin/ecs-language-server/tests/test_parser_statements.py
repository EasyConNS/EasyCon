"""Parser statement tests — verify AST node types and fields for all statements."""

from __future__ import annotations
import pytest
from easycon_grammar import ast


# ---------------------------------------------------------------------------
# Button press
# ---------------------------------------------------------------------------

def test_button_press_simple(parse):
    prog = parse("A\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyPressStmt)
    assert stmt.key == "A"
    assert stmt.duration is None


def test_button_press_with_duration(parse):
    prog = parse("A 100\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyPressStmt)
    assert stmt.key == "A"
    assert stmt.duration.value == 100


BUTTON_NAMES = [
    "A", "B", "X", "Y", "L", "R", "ZL", "ZR",
    "HOME", "CAPTURE", "PLUS", "MINUS", "LCLICK", "RCLICK",
]


@pytest.mark.parametrize("button", BUTTON_NAMES)
def test_all_button_keys(parse, button):
    prog = parse(f"{button}\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyPressStmt)
    assert stmt.key.upper() == button.upper()


# ---------------------------------------------------------------------------
# Button state
# ---------------------------------------------------------------------------

def test_button_down(parse):
    prog = parse("HOME DOWN\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyActStmt)
    assert stmt.key == "HOME"
    assert stmt.up is False


def test_button_up(parse):
    prog = parse("HOME UP\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyActStmt)
    assert stmt.key == "HOME"
    assert stmt.up is True


# ---------------------------------------------------------------------------
# Stick control
# ---------------------------------------------------------------------------

def test_stick_direction(parse):
    """LS UP without duration returns StickActStmt."""
    prog = parse("LS UP\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.StickActStmt)
    assert stmt.key == "LS"
    assert stmt.direction == "UP"


def test_stick_angle(parse):
    """LS 90 — numeric stick angle."""
    prog = parse("LS 90\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.StickActStmt)
    assert stmt.key == "LS"
    assert stmt.direction == "90"


def test_stick_timing(parse):
    """LS UP,100 with duration returns StickPressStmt."""
    prog = parse("LS UP,100\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.StickPressStmt)
    assert stmt.key == "LS"
    assert stmt.direction == "UP"
    assert stmt.duration.value == 100


def test_stick_angle_timing(parse):
    """RS 45,200 — numeric angle with duration."""
    prog = parse("RS 45,200\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.StickPressStmt)
    assert stmt.key == "RS"
    assert stmt.direction == "45"
    assert stmt.duration.value == 200


def test_stick_reset(parse):
    prog = parse("LS RESET\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.StickActStmt)
    assert stmt.key == "LS"


# ---------------------------------------------------------------------------
# PRINT / ALERT
# ---------------------------------------------------------------------------

def test_print_bare_text(parse):
    prog = parse("PRINT Hello World\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "PRINT"
    assert len(stmt.args) == 1  # Preprocessed into quoted string


def test_print_quoted(parse):
    prog = parse('PRINT "Hello World"\n')
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "PRINT"


def test_print_variable(parse):
    prog = parse("PRINT $count\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "PRINT"


def test_print_concat(parse):
    prog = parse("PRINT A & B\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "PRINT"


def test_alert(parse):
    prog = parse("ALERT Done\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "ALERT"


# ---------------------------------------------------------------------------
# WAIT
# ---------------------------------------------------------------------------

def test_wait(parse):
    prog = parse("WAIT 100\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.WaitStmt)
    assert stmt.duration.value == 100
    assert stmt.omitted is False


def test_wait_omitted(parse):
    prog = parse("100\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.WaitStmt)
    assert stmt.omitted is True
    assert stmt.duration.value == 100


# ---------------------------------------------------------------------------
# IF statements
# ---------------------------------------------------------------------------

def test_if_simple(parse):
    prog = parse("IF $a > 0\nPRINT yes\nENDIF\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.IfStmt)
    assert len(stmt.body) == 1
    assert len(stmt.elifs) == 0
    assert len(stmt.else_body) == 0


def test_if_else(parse):
    prog = parse("IF $a > 0\nPRINT pos\nELSE\nPRINT neg\nENDIF\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.IfStmt)
    assert len(stmt.body) == 1
    assert len(stmt.else_body) == 1


def test_if_elif_else(parse):
    prog = parse(
        "IF $score >= 90\nPRINT A\n"
        "ELIF $score >= 60\nPRINT B\n"
        "ELSE\nPRINT C\n"
        "ENDIF\n"
    )
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.IfStmt)
    assert len(stmt.elifs) == 1
    assert len(stmt.else_body) == 1


def test_if_nested(parse):
    prog = parse(
        "IF $a > 0\n"
        "IF $a < 100\nPRINT ok\nENDIF\n"
        "ENDIF\n"
    )
    outer = prog.statements[0]
    assert isinstance(outer, ast.IfStmt)
    inner = outer.body[0]
    assert isinstance(inner, ast.IfStmt)


# ---------------------------------------------------------------------------
# FOR loops
# ---------------------------------------------------------------------------

def test_for_count(parse):
    prog = parse("FOR 10\nA\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.upper.value == 10
    assert stmt.iter_name is None
    assert len(stmt.body) == 1


def test_for_range(parse):
    prog = parse("FOR $i = 1 TO 10\nA\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.iter_name == "$i"
    assert stmt.lower.value == 1
    assert stmt.upper.value == 10


def test_for_variable_count(parse):
    prog = parse("FOR $count\nA\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.iter_name is None


def test_for_step(parse):
    prog = parse("FOR $i = 0 TO 100 STEP 10\nA\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.iter_name == "$i"
    assert stmt.lower.value == 0
    assert stmt.upper.value == 100
    assert stmt.step is not None
    assert stmt.step.value == 10


def test_for_count_with_step(parse):
    prog = parse("FOR 10 STEP 2\nA\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.upper.value == 10
    assert stmt.step is not None
    assert stmt.step.value == 2


def test_for_var_with_step(parse):
    prog = parse("$n = 5\nFOR $n STEP 3\nA\nNEXT\n")
    stmt = prog.statements[1]
    assert isinstance(stmt, ast.ForStmt)
    assert isinstance(stmt.upper, ast.VariableExpr)
    assert stmt.step is not None
    assert stmt.step.value == 3


def test_for_const_with_step(parse):
    prog = parse("_N = 10\nFOR _N STEP 2\nA\nNEXT\n")
    stmt = prog.statements[1]
    assert isinstance(stmt, ast.ForStmt)
    assert isinstance(stmt.upper, ast.ConstVarExpr)
    assert stmt.step is not None
    assert stmt.step.value == 2


def test_for_infinite(parse):
    prog = parse("FOR\nBREAK\nNEXT\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ForStmt)
    assert stmt.infinite is True


def test_for_nested(parse):
    prog = parse(
        "FOR $i = 1 TO 3\n"
        "FOR $j = 1 TO 3\nA\nNEXT\n"
        "NEXT\n"
    )
    outer = prog.statements[0]
    assert isinstance(outer, ast.ForStmt)
    inner = outer.body[0]
    assert isinstance(inner, ast.ForStmt)


# ---------------------------------------------------------------------------
# BREAK / CONTINUE
# ---------------------------------------------------------------------------

def test_break(parse):
    prog = parse("FOR 10\nBREAK\nNEXT\n")
    body = prog.statements[0].body
    assert isinstance(body[0], ast.BreakStmt)
    assert body[0].level == 1


def test_break_level(parse):
    prog = parse("FOR 10\nBREAK 2\nNEXT\n")
    body = prog.statements[0].body
    assert isinstance(body[0], ast.BreakStmt)
    assert body[0].level == 2


def test_continue(parse):
    prog = parse("FOR 10\nCONTINUE\nNEXT\n")
    body = prog.statements[0].body
    assert isinstance(body[0], ast.ContinueStmt)


# ---------------------------------------------------------------------------
# Variable assignment
# ---------------------------------------------------------------------------

def test_assignment_simple(parse):
    prog = parse("$a = 1\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.AssignmentStmt)
    assert stmt.op == "="
    assert isinstance(stmt.target, ast.VariableExpr)
    assert stmt.target.name == "$a"


COMPOUND_ASSIGN_TESTS = [
    ("$a += 5", "+="),
    ("$a -= 3", "-="),
    ("$a *= 2", "*="),
    ("$a /= 4", "/="),
    ("$a %= 3", "%="),
]


@pytest.mark.parametrize("line,expected_op", COMPOUND_ASSIGN_TESTS)
def test_compound_assignment(parse, line, expected_op):
    prog = parse(f"{line}\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.AssignmentStmt)
    assert stmt.op == expected_op


# ---------------------------------------------------------------------------
# Constant declaration
# ---------------------------------------------------------------------------

def test_constant_decl(parse):
    prog = parse("_MAX = 100\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ConstantDeclStmt)
    assert stmt.name == "_MAX"
    assert stmt.value.value == 100


# ---------------------------------------------------------------------------
# Image variables
# ---------------------------------------------------------------------------

def test_img_var_assignment(parse):
    prog = parse("$match = @闪光度\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.AssignmentStmt)
    assert isinstance(stmt.value, ast.ExtVarExpr)
    assert stmt.value.name == "闪光度"


def test_img_var_condition(parse):
    prog = parse("IF @target > 90\nA\nENDIF\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.IfStmt)
    cond = stmt.condition
    assert isinstance(cond, ast.BinaryExpr)
    assert isinstance(cond.left, ast.ExtVarExpr)
    assert cond.left.name == "target"


# ---------------------------------------------------------------------------
# Function declaration
# ---------------------------------------------------------------------------

def test_func_decl_no_params(parse):
    prog = parse("FUNC say_hello\nPRINT hello\nENDFUNC\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.FuncDeclStmt)
    assert stmt.name == "say_hello"
    assert len(stmt.params) == 0
    assert len(stmt.body) == 1


def test_func_decl_params(parse):
    prog = parse("FUNC greet($name)\nPRINT $name\nENDFUNC\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.FuncDeclStmt)
    assert stmt.name == "greet"
    assert stmt.params == ["$name"]


def test_func_decl_return(parse):
    prog = parse("FUNC add($a, $b):INT\nRETURN $a + $b\nENDFUNC\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.FuncDeclStmt)
    assert stmt.name == "add"


# ---------------------------------------------------------------------------
# Function call
# ---------------------------------------------------------------------------

def test_call_keyword(parse):
    prog = parse("CALL say_hello\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "say_hello"
    assert stmt.is_call is True


def test_func_call_direct(parse):
    prog = parse('say_hello "World"\n')
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.CallStmt)
    assert stmt.name == "say_hello"


# ---------------------------------------------------------------------------
# IMPORT
# ---------------------------------------------------------------------------

def test_import_stmt(parse):
    prog = parse('IMPORT "module.ecs"\nA\n')
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ImportStmt)
    assert stmt.module == "module.ecs"


# ---------------------------------------------------------------------------
# EXTERN FUNC (FFI)
# ---------------------------------------------------------------------------

def test_extern_func_no_params(parse):
    prog = parse('EXTERN FUNC GetForegroundWindow():PTR FROM "user32.dll"\n')
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ExternFuncStmt)
    assert stmt.name == "GetForegroundWindow"
    assert stmt.return_type == "PTR"
    assert stmt.library == "user32.dll"
    assert len(stmt.params) == 0


def test_extern_func_with_params(parse):
    prog = parse(
        'EXTERN FUNC MessageBoxW($hwnd:PTR, $text:STRING, $caption:STRING, $flags:INT):INT FROM "user32.dll"\n'
    )
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ExternFuncStmt)
    assert stmt.name == "MessageBoxW"
    assert stmt.return_type == "INT"
    assert stmt.library == "user32.dll"
    assert len(stmt.params) == 4
    assert stmt.params[0].name == "$hwnd"
    assert stmt.params[0].type == "PTR"


def test_extern_func_void_return(parse):
    prog = parse('EXTERN FUNC Sleep($ms:INT):VOID FROM "kernel32.dll"\n')
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.ExternFuncStmt)
    assert stmt.return_type == "VOID"


# ---------------------------------------------------------------------------
# Comments attached to statements
# ---------------------------------------------------------------------------

def test_comment_on_statement(parse):
    prog = parse("A  # press A\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.KeyPressStmt)
    assert stmt.comment == "# press A"


# ---------------------------------------------------------------------------
# BEEP / AMIIBO
# ---------------------------------------------------------------------------

def test_beep_stmt(parse):
    prog = parse("BEEP 1000, 200\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.BeepStmt)
    assert stmt.frequency.value == 1000
    assert stmt.duration.value == 200


def test_beep_with_vars(parse):
    prog = parse("$freq = 1000\nBEEP $freq, 200\n")
    stmt = prog.statements[1]
    assert isinstance(stmt, ast.BeepStmt)
    assert isinstance(stmt.frequency, ast.VariableExpr)


def test_amiibo_stmt(parse):
    prog = parse("AMIIBO 0\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.AmiiboStmt)
    assert stmt.index.value == 0


def test_amiibo_with_var(parse):
    prog = parse("AMIIBO $index\n")
    stmt = prog.statements[0]
    assert isinstance(stmt, ast.AmiiboStmt)
    assert isinstance(stmt.index, ast.VariableExpr)
    assert stmt.index.name == "$index"
