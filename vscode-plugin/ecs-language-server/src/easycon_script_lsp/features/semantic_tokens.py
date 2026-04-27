"""Semantic token support for ECS LSP."""

from __future__ import annotations

from lsprotocol.types import SemanticTokens

from easycon_grammar import ast
from easycon_grammar.parser import Parser
from easycon_script_lsp.utils.token_map import get_semantic_token_type


def get_semantic_tokens(
    source: str, program: ast.Program | None = None
) -> SemanticTokens:
    """Build semantic tokens from source, optionally using AST for type overrides."""
    parser = Parser()
    tokens = parser.tokenize(source)

    overrides: dict[tuple[int, int], str] = {}
    if program:
        overrides = _build_overrides(program)

    data: list[int] = []
    prev_line = 0
    prev_col = 0

    for tok in tokens:
        # Override enum member tokens when the tokenizer can't
        # distinguish context (e.g. UP as key vs key_mod vs direction)
        pos = (tok["line"], tok["column"])
        tok_type = overrides.get(pos, tok["type"])

        sem_type = get_semantic_token_type(tok_type)
        if sem_type is None:
            continue

        line = tok["line"] - 1  # 0-based for LSP
        col = tok["column"] - 1
        length = len(tok["value"])

        # Delta encoding
        delta_line = line - prev_line
        if delta_line == 0:
            delta_col = col - prev_col
        else:
            delta_col = col
            prev_line = line
            prev_col = col

        data.extend([delta_line, delta_col, length, sem_type, 0])

    return SemanticTokens(data=data)


def _build_overrides(program: ast.Program) -> dict[tuple[int, int], str]:
    """Walk AST and collect positions where token type should be overridden."""
    overrides: dict[tuple[int, int], str] = {}

    def walk(stmts: list[ast.Stmt]) -> None:
        for stmt in stmts:
            if isinstance(stmt, ast.KeyPressStmt):
                # The key part should be BUTTON_KEY (like A/B/X/Y)
                overrides[(stmt.loc.line, stmt.loc.column)] = "BUTTON_KEY"
            elif isinstance(stmt, ast.KeyActStmt):
                # key part → BUTTON_KEY, key_mod part → KEY_MOD
                overrides[(stmt.loc.line, stmt.loc.column)] = "BUTTON_KEY"
                mod_line = stmt.loc.line
                mod_col = stmt.loc.column + len(stmt.key) + 1
                overrides[(mod_line, mod_col)] = "KEY_MOD"
            elif isinstance(stmt, (ast.StickActStmt, ast.StickPressStmt)):
                # Direction follows key with a space
                dir_line = stmt.loc.line
                dir_col = stmt.loc.column + len(stmt.key) + 1
                overrides[(dir_line, dir_col)] = "DIRECTION_KEY"
            # Recurse into block statements
            if isinstance(stmt, ast.IfStmt):
                walk(stmt.body)
                for _, elif_body in stmt.elifs:
                    walk(elif_body)
                if stmt.else_body:
                    walk(stmt.else_body)
            elif isinstance(stmt, ast.ForStmt):
                walk(stmt.body)
            elif isinstance(stmt, ast.WhileStmt):
                walk(stmt.body)
            elif isinstance(stmt, ast.FuncDeclStmt):
                walk(stmt.body)

    walk(program.statements)
    return overrides
