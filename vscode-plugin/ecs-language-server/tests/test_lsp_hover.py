"""LSP hover tests."""

from __future__ import annotations
from lsprotocol.types import Position
from easycon_script_lsp.features.hover import get_hover


def test_hover_keyword():
    result = get_hover(None, "IF")
    assert result is not None
    assert result.contents is not None


def test_hover_button_key():
    result = get_hover(None, "A")
    assert result is not None


def test_hover_stick_key():
    result = get_hover(None, "LS")
    assert result is not None


def test_hover_direction():
    result = get_hover(None, "UP")
    assert result is not None


def test_hover_builtin():
    result = get_hover(None, "PRINT")
    assert result is not None


def test_hover_unknown_word():
    result = get_hover(None, "XYZZY")
    assert result is None


def test_hover_constant(parse):
    prog = parse("_MAX = 100\n")
    result = get_hover(prog, "_MAX")
    assert result is not None


def test_hover_variable(parse):
    prog = parse("$count = 0\n")
    result = get_hover(prog, "$count")
    assert result is not None


def test_hover_function(parse):
    prog = parse("FUNC foo\nA\nENDFUNC\n")
    result = get_hover(prog, "foo")
    assert result is not None


def test_hover_extern_func(parse):
    prog = parse('EXTERN FUNC Sleep($ms:INT):VOID FROM "kernel32.dll"\n')
    result = get_hover(prog, "Sleep")
    assert result is not None


def test_hover_with_position(parse):
    """Hover with AST-based position lookup."""
    prog = parse("WAIT 100\n")
    result = get_hover(prog, "WAIT", Position(line=0, character=1))
    assert result is not None


def test_hover_empty_program():
    """Empty program falls back to word matching."""
    result = get_hover(None, "TRUE")
    assert result is not None
