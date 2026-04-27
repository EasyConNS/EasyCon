"""Completion support for ECS LSP."""

from __future__ import annotations

from lsprotocol.types import CompletionItem, CompletionItemKind, CompletionList

from easycon_grammar import ast
from easycon_script_lsp.constants import ECS_KEYWORDS, ECS_BUILTIN_FUNCTIONS, ECS_FFI_TYPES, BUILTIN_DOCS
from easycon_script_lsp.utils.ast_walker import ASTWalker


def get_completions(program: ast.Program | None, trigger: str | None = None) -> CompletionList:
    """Return completion items for the given program state."""
    items: list[CompletionItem] = []
    seen: set[str] = set()

    def add(label: str, kind: CompletionItemKind, detail: str = "", documentation: str = ""):
        if label in seen:
            return
        seen.add(label)
        items.append(
            CompletionItem(
                label=label,
                kind=kind,
                detail=detail or None,
                documentation=documentation or None,
            )
        )

    # Keywords
    for kw in ECS_KEYWORDS:
        add(kw, CompletionItemKind.Keyword)

    # Built-in functions
    for builtin in ECS_BUILTIN_FUNCTIONS:
        doc = BUILTIN_DOCS.get(builtin, "")
        add(builtin, CompletionItemKind.Function, detail="内置函数", documentation=doc)

    # FFI type names (for EXTERN FUNC declarations)
    for ffi_type in ECS_FFI_TYPES:
        add(ffi_type, CompletionItemKind.TypeParameter, detail="FFI 类型")

    # Declared symbols from AST
    if program is not None:
        walker = ASTWalker(program)
        for sym in walker.iter_symbols():
            if sym.kind == "constant":
                add(sym.name, CompletionItemKind.Constant)
            elif sym.kind == "variable":
                add(sym.name, CompletionItemKind.Variable)
            elif sym.kind == "function":
                add(sym.name, CompletionItemKind.Function)

    return CompletionList(is_incomplete=False, items=items)
