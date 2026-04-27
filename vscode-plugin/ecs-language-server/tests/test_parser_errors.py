"""Parser error tests — verify error detection for malformed programs."""

from __future__ import annotations
import pytest
from easycon_grammar.parser import ParseError


def _parse_and_expect_error(parse, source: str, fragment: str):
    """Assert that parsing source raises ParseError containing fragment."""
    with pytest.raises(ParseError, match=fragment):
        parse(source)


# ---------------------------------------------------------------------------
# IMPORT placement
# ---------------------------------------------------------------------------

def test_import_not_at_top(parse):
    _parse_and_expect_error(parse, "A\nIMPORT \"mod.ecs\"\n", "导入")


def test_import_after_statement(parse):
    _parse_and_expect_error(
        parse,
        'IMPORT "a.ecs"\nA\nIMPORT "b.ecs"\n',
        "导入",
    )


# ---------------------------------------------------------------------------
# Duplicate / misplaced ELSE / ELIF
# ---------------------------------------------------------------------------

def test_duplicate_else(parse):
    _parse_and_expect_error(
        parse,
        "IF $a > 0\nA\nELSE\nB\nELSE\nC\nENDIF\n",
        "Else",
    )


def test_elif_after_else(parse):
    _parse_and_expect_error(
        parse,
        "IF $a > 0\nA\nELSE\nB\nELIF $a > 5\nC\nENDIF\n",
        "Elif",
    )


# ---------------------------------------------------------------------------
# Unclosed blocks
# ---------------------------------------------------------------------------

def test_unclosed_if(parse):
    _parse_and_expect_error(parse, "IF $a > 0\nA\n", "没有正确结束")


def test_unclosed_for(parse):
    _parse_and_expect_error(parse, "FOR 10\nA\n", "没有正确结束")


def test_unclosed_func(parse):
    _parse_and_expect_error(parse, "FUNC foo\nA\n", "没有正确结束")


# ---------------------------------------------------------------------------
# Unmatched closers
# ---------------------------------------------------------------------------

def test_unmatched_endif(parse):
    _parse_and_expect_error(parse, "A\nENDIF\n", "多余的结束语句")


def test_unmatched_next(parse):
    _parse_and_expect_error(parse, "A\nNEXT\n", "多余的结束语句")


# ---------------------------------------------------------------------------
# FUNC / EXTERN inside blocks
# ---------------------------------------------------------------------------

def test_func_inside_block(parse):
    _parse_and_expect_error(
        parse,
        "IF $a > 0\nFUNC foo\nA\nENDFUNC\nENDIF\n",
        "函数必须在顶层",
    )


def test_extern_inside_block(parse):
    _parse_and_expect_error(
        parse,
        'IF $a > 0\nEXTERN FUNC foo():VOID FROM "dll"\nENDIF\n',
        "EXTERN FUNC只能在顶层",
    )


# ---------------------------------------------------------------------------
# ELIF/ELSE without IF
# ---------------------------------------------------------------------------

def test_elif_no_if(parse):
    _parse_and_expect_error(parse, "A\nELIF $a > 0\nB\nENDIF\n", "ELIF需要对应的If")


def test_else_no_if(parse):
    _parse_and_expect_error(parse, "A\nELSE\nB\nENDIF\n", "ELSE需要对应的If")


# ---------------------------------------------------------------------------
# Syntax errors (from Lark)
# ---------------------------------------------------------------------------

def test_invalid_syntax(parse):
    """Malformed expression should produce a parse error."""
    with pytest.raises(ParseError):
        parse("$a = =\n")
