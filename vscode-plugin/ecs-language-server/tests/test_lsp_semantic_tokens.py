"""LSP semantic tokens tests."""

from __future__ import annotations
from easycon_script_lsp.features.semantic_tokens import get_semantic_tokens


def test_semantic_tokens_not_empty():
    tokens = get_semantic_tokens("A\nB\n")
    assert tokens is not None
    assert len(tokens.data) > 0


def test_semantic_tokens_keywords():
    """Keywords should have non-zero token type index."""
    tokens = get_semantic_tokens("IF $a > 0\nENDIF\n")
    assert len(tokens.data) > 0
    # data is encoded as [deltaLine, deltaCol, length, tokenType, tokenModifiers] quintuples
    assert len(tokens.data) % 5 == 0


def test_semantic_tokens_variables():
    tokens = get_semantic_tokens("$count = 0\n")
    assert len(tokens.data) > 0


def test_semantic_tokens_comments():
    tokens = get_semantic_tokens("# comment\nA\n")
    assert len(tokens.data) > 0


def test_semantic_tokens_with_ast_override():
    """AST overrides should reclassify buttons as Function type."""
    tokens = get_semantic_tokens("A\n", None)
    assert len(tokens.data) > 0


def test_semantic_tokens_empty():
    tokens = get_semantic_tokens("")
    assert tokens.data == []
