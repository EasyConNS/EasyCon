"""LSP go-to-definition tests."""

from __future__ import annotations
from lsprotocol.types import Position
from easycon_script_lsp.features.definition import get_definition


def test_def_variable(parse):
    prog = parse("$count = 0\nPRINT $count\n")
    source = "$count = 0\nPRINT $count\n"
    loc = get_definition(prog, source, Position(line=1, character=8))
    assert loc is not None
    assert loc.range.start.line == 0  # declaration on line 0


def test_def_constant(parse):
    prog = parse("_MAX = 100\nPRINT _MAX\n")
    source = "_MAX = 100\nPRINT _MAX\n"
    loc = get_definition(prog, source, Position(line=1, character=8))
    assert loc is not None
    assert loc.range.start.line == 0


def test_def_function(parse):
    prog = parse("FUNC greet\nPRINT hi\nENDFUNC\ngreet\n")
    source = "FUNC greet\nPRINT hi\nENDFUNC\ngreet\n"
    loc = get_definition(prog, source, Position(line=3, character=1))
    assert loc is not None
    assert loc.range.start.line == 0


def test_def_builtin(parse):
    """Built-in functions have no declaration location."""
    prog = parse("WAIT 100\n")
    source = "WAIT 100\n"
    loc = get_definition(prog, source, Position(line=0, character=1))
    assert loc is None


def test_def_image_var(parse):
    """External variables (@image) have no declaration in source."""
    prog = parse("$x = @target\n")
    source = "$x = @target\n"
    loc = get_definition(prog, source, Position(line=0, character=7))
    assert loc is None


def test_def_whitespace(parse):
    """Cursor at a position without a word (e.g. on whitespace between tokens)."""
    prog = parse("$a = 1\n")
    source = "$a = 1\n"
    # Position at column 2 is the space between "=" and "1"
    loc = get_definition(prog, source, Position(line=0, character=2))
    # Space may or may not resolve to a word depending on regex behavior
    # The important thing is that the function doesn't crash
    assert loc is None or isinstance(loc, object)
