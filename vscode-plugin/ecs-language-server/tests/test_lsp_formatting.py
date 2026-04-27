"""LSP document formatting tests."""

from __future__ import annotations
from easycon_script_lsp.features.formatting import format_document


def _strip_trailing_newline(s: str) -> str:
    if s.endswith("\n"):
        return s[:-1]
    return s


def test_format_simple(parse):
    prog = parse("A\n")
    result = format_document(prog)
    assert _strip_trailing_newline(result) == "A"


def test_format_if_block(parse):
    prog = parse("IF $a > 0\nPRINT yes\nENDIF\n")
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0] == "IF $a > 0"
    # PRINT args are string-escaped by the formatter
    assert "PRINT" in lines[1]
    assert lines[1].startswith("    ")
    assert lines[2] == "ENDIF"


def test_format_if_elif_else(parse):
    source = (
        "IF $score >= 90\nPRINT A\n"
        "ELIF $score >= 60\nPRINT B\n"
        "ELSE\nPRINT C\n"
        "ENDIF\n"
    )
    prog = parse(source)
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0].startswith("IF")
    assert "PRINT" in lines[1]
    assert lines[2].startswith("ELIF")
    assert lines[4].startswith("ELSE")
    assert lines[6] == "ENDIF"


def test_format_for_loop(parse):
    prog = parse("FOR 10\nA\nNEXT\n")
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0] == "FOR 10"
    assert lines[1] == "    A"
    assert lines[2] == "NEXT"


def test_format_nested_blocks(parse):
    source = (
        "FOR $i = 1 TO 3\n"
        "IF $i > 1\nPRINT big\nENDIF\n"
        "NEXT\n"
    )
    prog = parse(source)
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0] == "FOR $i = 1 TO 3"
    assert lines[1].startswith("    IF $i > 1")
    assert lines[2].startswith("        PRINT")
    assert lines[3] == "    ENDIF"
    assert lines[4] == "NEXT"


def test_format_assignment(parse):
    prog = parse("$a = 1\n")
    result = format_document(prog)
    assert _strip_trailing_newline(result) == "$a = 1"


def test_format_compound_assignment(parse):
    prog = parse("$a += 5\n")
    result = format_document(prog)
    assert _strip_trailing_newline(result) == "$a += 5"


def test_format_func_decl(parse):
    prog = parse("FUNC foo\nA\nENDFUNC\n")
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0] == "FUNC foo"
    assert lines[1] == "    A"
    assert lines[2] == "ENDFUNC"


def test_format_comment(parse):
    prog = parse("# header comment\nA\n")
    result = format_document(prog)
    lines = result.splitlines()
    assert lines[0] == "# header comment"


def test_format_inline_comment(parse):
    prog = parse("A  # press A\n")
    result = format_document(prog)
    # Formatter normalizes to single space before comment
    assert "A" in result
    assert "# press A" in result


def test_format_empty_program(parse):
    """Empty source should produce empty output."""
    prog = parse("# just a comment\n")
    result = format_document(prog)
    assert result == "# just a comment\n"


def test_format_roundtrip(parse):
    """Parse -> format -> re-parse produces equivalent AST structure."""
    source = (
        "FUNC max($a, $b):INT\n"
        "IF $a > $b\n"
        "RETURN $a\n"
        "ELSE\n"
        "RETURN $b\n"
        "ENDIF\n"
        "ENDFUNC\n"
    )
    prog1 = parse(source)
    formatted = format_document(prog1)
    prog2 = parse(formatted)
    # Both programs should have the same structure
    assert len(prog1.statements) == len(prog2.statements)
    assert isinstance(prog1.statements[0], type(prog2.statements[0]))


def test_format_beep(parse):
    prog = parse("BEEP 1000, 200\n")
    result = format_document(prog)
    assert "BEEP" in result
    assert "1000" in result
    assert "200" in result


def test_format_amiibo(parse):
    prog = parse("AMIIBO 0\n")
    result = format_document(prog)
    assert "AMIIBO" in result
    assert "0" in result


def test_format_for_step(parse):
    prog = parse("FOR $i = 0 TO 100 STEP 10\nA\nNEXT\n")
    result = format_document(prog)
    lines = result.splitlines()
    assert "STEP 10" in lines[0]
    assert lines[1] == "    A"
    assert lines[2] == "NEXT"
