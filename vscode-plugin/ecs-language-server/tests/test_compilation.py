"""Compilation tests — Compilation.compile() pipeline."""

from __future__ import annotations
from easycon_grammar import ast


def test_compile_valid_program(compile_source):
    result = compile_source("$a = 1\nA\n")
    assert result.has_errors is False
    assert result.program is not None
    assert isinstance(result.program, ast.Program)
    assert len(result.program.statements) == 2


def test_compile_with_import(compile_source):
    result = compile_source('IMPORT "lib.ecs"\n$b = 2\n')
    assert result.has_errors is False
    assert isinstance(result.program.statements[0], ast.ImportStmt)


def test_compile_parse_error(compile_source):
    result = compile_source("$a = =\n")
    assert result.has_errors is True
    assert result.program is None
    assert len(result.errors) >= 1


def test_compile_unknown_button(compile_source):
    """Semantic validation catches unknown button names."""
    result = compile_source("Z 100\n")
    assert result.has_errors is True


def test_compile_unknown_func(compile_source):
    """Semantic validation catches unknown function calls."""
    result = compile_source("NONEXISTENT()\n")
    assert result.has_errors is True


def test_compile_complex_valid(compile_source):
    """A non-trivial valid program compiles without errors."""
    source = (
        "FUNC add($a, $b):INT\n"
        "RETURN $a + $b\n"
        "ENDFUNC\n"
        "$result = add(3, 4)\n"
        "IF $result > 0\n"
        "PRINT result is positive\n"
        "ENDIF\n"
    )
    result = compile_source(source)
    assert result.has_errors is False
    assert result.program is not None
