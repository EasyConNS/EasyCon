"""LSP document symbols tests."""

from __future__ import annotations
from lsprotocol.types import SymbolKind
from easycon_script_lsp.features.document_symbols import get_document_symbols


def test_symbol_constant(parse):
    prog = parse("_MAX = 100\n")
    symbols = get_document_symbols(prog)
    const_symbols = [s for s in symbols if s.kind == SymbolKind.Constant]
    assert len(const_symbols) == 1
    assert const_symbols[0].name == "_MAX"


def test_symbol_variable(parse):
    prog = parse("$count = 0\n")
    symbols = get_document_symbols(prog)
    var_symbols = [s for s in symbols if s.kind == SymbolKind.Variable]
    assert len(var_symbols) >= 1
    assert any(s.name == "$count" for s in var_symbols)


def test_symbol_function(parse):
    prog = parse("FUNC foo\nA\nENDFUNC\n")
    symbols = get_document_symbols(prog)
    func_symbols = [s for s in symbols if s.kind == SymbolKind.Function]
    assert len(func_symbols) >= 1
    assert any(s.name == "foo" for s in func_symbols)


def test_symbol_function_params(parse):
    prog = parse("FUNC greet($name)\nPRINT $name\nENDFUNC\n")
    symbols = get_document_symbols(prog)
    func_sym = next(s for s in symbols if s.name == "greet")
    assert func_sym.children is not None
    param_names = [c.name for c in func_sym.children]
    assert "$name" in param_names


def test_symbol_extern_func(parse):
    prog = parse('EXTERN FUNC Sleep($ms:INT):VOID FROM "kernel32.dll"\n')
    symbols = get_document_symbols(prog)
    func_symbols = [s for s in symbols if s.kind == SymbolKind.Function]
    assert any(s.name == "Sleep" for s in func_symbols)


def test_symbol_empty_program(parse):
    """Empty program returns empty list."""
    prog = parse("# comment\n")
    symbols = get_document_symbols(prog)
    assert symbols == []


def test_symbol_ordering(parse):
    """Symbols preserve declaration order."""
    prog = parse("_A = 1\n$b = 2\nFUNC c\nA\nENDFUNC\n")
    symbols = get_document_symbols(prog)
    names = [s.name for s in symbols]
    assert names == ["_A", "$b", "c"]
