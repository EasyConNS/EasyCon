"""Parser expression tests — verify AST expression tree structure."""

from __future__ import annotations
import pytest
from easycon_grammar import ast


def _unwrap(stmt):
    """Extract value expression from an assignment statement."""
    return stmt.value


# ---------------------------------------------------------------------------
# Literals
# ---------------------------------------------------------------------------

def test_literal_int(parse):
    prog = parse("$x = 42\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.LiteralExpr)
    assert expr.value == 42


def test_negative_int_literal(parse):
    """-42 is tokenized as a single INT (-42) by Lark, not as unary minus on 42."""
    prog = parse("$x = -42\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.LiteralExpr)
    assert expr.value == -42


def test_literal_bool_true(parse):
    prog = parse("$x = TRUE\n")
    expr = _unwrap(prog.statements[0])
    assert expr.value is True


def test_literal_bool_false(parse):
    prog = parse("$x = FALSE\n")
    expr = _unwrap(prog.statements[0])
    assert expr.value is False


def test_literal_string(parse):
    prog = parse('$x = "hello"\n')
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.LiteralExpr)
    assert expr.value == "hello"


# ---------------------------------------------------------------------------
# Variables and constants
# ---------------------------------------------------------------------------

def test_variable_expr(parse):
    prog = parse("$x = $y\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.VariableExpr)
    assert expr.name == "$y"


def test_const_var_expr(parse):
    prog = parse("$x = _MAX\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.ConstVarExpr)
    assert expr.name == "_MAX"


def test_ext_var_expr(parse):
    prog = parse("$x = @image\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.ExtVarExpr)
    assert expr.name == "image"


# ---------------------------------------------------------------------------
# Array definition
# ---------------------------------------------------------------------------

def test_array_def(parse):
    prog = parse("$x = [1, 2, 3]\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.IndexDefExpr)
    assert len(expr.items) == 3


def test_empty_array(parse):
    prog = parse("$x = []\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.IndexDefExpr)
    assert len(expr.items) == 0


# ---------------------------------------------------------------------------
# Arithmetic operators
# ---------------------------------------------------------------------------

ARITHMETIC_TESTS = [
    ("10 + 5", "+", 10, 5),
    ("10 - 3", "-", 10, 3),
    ("6 * 7", "*", 6, 7),
    ("20 / 4", "/", 20, 4),
    ("20 \\ 3", "\\", 20, 3),
    ("10 % 3", "%", 10, 3),
    ("2 ^ 8", "^", 2, 8),
]


@pytest.mark.parametrize("expr_str,expected_op,left_val,right_val", ARITHMETIC_TESTS)
def test_arithmetic(parse, expr_str, expected_op, left_val, right_val):
    prog = parse(f"$x = {expr_str}\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == expected_op
    assert expr.left.value == left_val
    assert expr.right.value == right_val


# ---------------------------------------------------------------------------
# Comparison operators
# ---------------------------------------------------------------------------

COMPARISON_TESTS = [
    ("$a > $b", ">"),
    ("$a >= $b", ">="),
    ("$a < $b", "<"),
    ("$a <= $b", "<="),
    ("$a == $b", "=="),
    ("$a != $b", "!="),
]


@pytest.mark.parametrize("expr_str,expected_op", COMPARISON_TESTS)
def test_comparison(parse, expr_str, expected_op):
    prog = parse(f"$x = {expr_str}\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == expected_op


# ---------------------------------------------------------------------------
# Logical operators
# ---------------------------------------------------------------------------

def test_logical_and(parse):
    prog = parse("$x = $a and $b\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "and"


def test_logical_or(parse):
    prog = parse("$x = $a or $b\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "or"


def test_logical_not(parse):
    prog = parse("$x = not $a\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.UnaryExpr)
    assert expr.op == "not"


# ---------------------------------------------------------------------------
# Bitwise operators
# ---------------------------------------------------------------------------

BITWISE_TESTS = [
    ("$a & $b", "&"),
    ("$a | $b", "|"),
    ("$a ^ $b", "^"),
    ("$a << 2", "<<"),
    ("$a >> 2", ">>"),
]


@pytest.mark.parametrize("expr_str,expected_op", BITWISE_TESTS)
def test_bitwise_binary(parse, expr_str, expected_op):
    prog = parse(f"$x = {expr_str}\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == expected_op


def test_bitwise_not(parse):
    prog = parse("$x = ~$a\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.UnaryExpr)
    assert expr.op == "~"


# ---------------------------------------------------------------------------
# Parenthesized expression
# ---------------------------------------------------------------------------

def test_paren_expr(parse):
    prog = parse("$x = ($a + $b) * $c\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "*"
    assert isinstance(expr.left, ast.ParenExpr)


# ---------------------------------------------------------------------------
# String concatenation
# ---------------------------------------------------------------------------

def test_string_concat(parse):
    prog = parse('$x = "hello" & "world"\n')
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "&"


# ---------------------------------------------------------------------------
# Operator precedence
# ---------------------------------------------------------------------------

def test_precedence_mul_before_add(parse):
    """* binds tighter than +: 1 + 2 * 3 → (1 + (2 * 3))"""
    prog = parse("$x = 1 + 2 * 3\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "+"
    assert isinstance(expr.right, ast.BinaryExpr)
    assert expr.right.op == "*"


def test_precedence_add_before_compare(parse):
    """+ binds tighter than ==: 1 + 2 == 3 → ((1 + 2) == 3)"""
    prog = parse("$x = 1 + 2 == 3\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "=="
    assert isinstance(expr.left, ast.BinaryExpr)
    assert expr.left.op == "+"


def test_precedence_compare_before_and(parse):
    """> binds tighter than and: a > 1 and b < 2 → ((a > 1) and (b < 2))"""
    prog = parse("$x = $a > 1 and $b < 2\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "and"


def test_precedence_and_before_or(parse):
    """and binds tighter than or: a or b and c → (a or (b and c))"""
    prog = parse("$x = $a or $b and $c\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "or"


# ---------------------------------------------------------------------------
# Complex conditions (from Script.md examples)
# ---------------------------------------------------------------------------

def test_complex_condition(parse):
    prog = parse("$x = ($score >= 60) and ($attendance >= 80)\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.BinaryExpr)
    assert expr.op == "and"
    assert isinstance(expr.left, ast.ParenExpr)
    assert isinstance(expr.right, ast.ParenExpr)


# ---------------------------------------------------------------------------
# Function call expressions
# ---------------------------------------------------------------------------

def test_call_expr(parse):
    prog = parse("$result = add(10, 20)\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.CallExpr)
    assert expr.name == "add"
    assert len(expr.args) == 2


def test_call_expr_no_args(parse):
    prog = parse("$t = TIME()\n")
    expr = _unwrap(prog.statements[0])
    assert isinstance(expr, ast.CallExpr)
    assert expr.name == "TIME"
    assert len(expr.args) == 0
