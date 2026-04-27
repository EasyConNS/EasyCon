"""Document symbol support for ECS LSP."""

from __future__ import annotations

from lsprotocol.types import DocumentSymbol, SymbolKind, Position, Range

from easycon_grammar import ast
from easycon_script_lsp.utils.ast_walker import ASTWalker


def get_document_symbols(program: ast.Program) -> list[DocumentSymbol]:
    """Build a list of DocumentSymbol from the AST."""
    walker = ASTWalker(program)
    symbols: list[DocumentSymbol] = []

    for sym in walker.iter_symbols():
        ds = _to_document_symbol(sym)
        if ds is not None:
            symbols.append(ds)

    return symbols


def _to_document_symbol(sym) -> DocumentSymbol | None:
    kind_map = {
        "constant": SymbolKind.Constant,
        "variable": SymbolKind.Variable,
        "function": SymbolKind.Function,
        "parameter": SymbolKind.Variable,
    }
    kind = kind_map.get(sym.kind, SymbolKind.Object)

    start = Position(line=max(0, sym.loc.line - 1), character=max(0, sym.loc.column - 1))
    end = Position(line=start.line, character=start.character + len(sym.name))

    children = [_to_document_symbol(c) for c in sym.children]
    children = [c for c in children if c is not None]

    return DocumentSymbol(
        name=sym.name,
        kind=kind,
        range=Range(start=start, end=end),
        selection_range=Range(start=start, end=end),
        children=children or None,
    )
