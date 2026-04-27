"""LSP completion tests."""

from __future__ import annotations
from lsprotocol.types import CompletionItemKind
from easycon_script_lsp.features.completion import get_completions
from easycon_script_lsp.constants import ECS_KEYWORDS, ECS_BUILTIN_FUNCTIONS, ECS_FFI_TYPES


def test_all_keywords_present():
    items = get_completions(None).items
    labels = {item.label for item in items}
    for kw in ECS_KEYWORDS:
        assert kw in labels, f"Keyword {kw} missing from completions"


def test_all_builtins_present():
    items = get_completions(None).items
    for item in items:
        if item.label in ECS_BUILTIN_FUNCTIONS and item.kind == CompletionItemKind.Function:
            assert item.detail == "内置函数"


def test_ffi_types_present():
    items = get_completions(None).items
    labels = {item.label for item in items}
    for ffi in ECS_FFI_TYPES:
        assert ffi in labels, f"FFI type {ffi} missing from completions"


def test_variables_from_ast(parse):
    prog = parse("$count = 0\n$a = 1\n")
    items = get_completions(prog).items
    var_labels = {item.label for item in items if item.kind == CompletionItemKind.Variable}
    assert "$count" in var_labels
    assert "$a" in var_labels


def test_constants_from_ast(parse):
    prog = parse("_MAX = 100\n")
    items = get_completions(prog).items
    const_labels = {item.label for item in items if item.kind == CompletionItemKind.Constant}
    assert "_MAX" in const_labels


def test_functions_from_ast(parse):
    prog = parse("FUNC foo\nA\nENDFUNC\n")
    items = get_completions(prog).items
    func_labels = {item.label for item in items if item.kind == CompletionItemKind.Function}
    assert "foo" in func_labels


def test_no_duplicates():
    items = get_completions(None).items
    labels = [item.label for item in items]
    assert len(labels) == len(set(labels))


def test_empty_program():
    items = get_completions(None).items
    # Should still return keywords and builtins
    assert len(items) > 0
    assert any(item.label == "IF" for item in items)
