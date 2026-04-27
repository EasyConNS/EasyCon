"""Go-to-definition support for ECS LSP."""

from __future__ import annotations

import re

from lsprotocol.types import Location, Position, Range

from easycon_grammar import ast


# Pattern for word extraction, matching the old extension's definition provider
_WORD_PATTERN = re.compile(r"[\$_@]?[\w一-鿿！]+")


def get_definition(
    program: ast.Program,
    source: str,
    position: Position,
) -> Location | None:
    """Return the definition location for the symbol at the given position."""
    word, word_range = _word_at_position(source, position)
    if not word:
        return None

    decl_loc = _find_declaration(program, word)
    if decl_loc is None:
        return None

    return Location(
        uri="",  # filled by client
        range=Range(
            start=Position(line=max(0, decl_loc.line - 1), character=max(0, decl_loc.column - 1)),
            end=Position(line=max(0, decl_loc.line - 1), character=decl_loc.column + len(_decl_name(program, word))),
        ),
    )


def _word_at_position(source: str, position: Position) -> tuple[str | None, tuple[int, int] | None]:
    """Extract the identifier at the given position from source text."""
    lines = source.splitlines()
    if position.line >= len(lines):
        return None, None

    line_text = lines[position.line]
    col = position.character

    # Find the best match that covers the cursor position
    for m in _WORD_PATTERN.finditer(line_text):
        if m.start() <= col < m.end():
            return m.group(), (position.line, m.start())

    return None, None


def _find_declaration(program: ast.Program, word: str) -> ast.TextLocation | None:
    """Search AST for the declaration of the given symbol name."""
    if word.startswith("$$") or word.startswith("$"):
        return _find_var_decl(program, word)
    if word.startswith("_"):
        return _find_const_decl(program, word)
    if word.startswith("@"):
        return _find_ext_var_decl(program, word)
    # Plain identifier → function declaration or builtin
    return _find_func_decl(program, word)


def _find_var_decl(program: ast.Program, name: str) -> ast.TextLocation | None:
    """Find declaration of a variable ($name or $$name)."""
    for stmt in program.statements:
        loc = _stmt_var_decl(stmt, name)
        if loc is not None:
            return loc
    return None


def _stmt_var_decl(stmt: ast.Stmt, name: str) -> ast.TextLocation | None:
    """Recurse into statement to find variable declaration."""
    if isinstance(stmt, ast.AssignmentStmt):
        target = stmt.target
        if isinstance(target, ast.VariableExpr) and target.name == name:
            return target.loc

    if isinstance(stmt, ast.ForStmt):
        if stmt.iter_name and f"${stmt.iter_name}" == name:
            return stmt.loc
        for s in stmt.body:
            loc = _stmt_var_decl(s, name)
            if loc is not None:
                return loc

    if isinstance(stmt, ast.FuncDeclStmt):
        if name in stmt.params:
            return stmt.loc
        for s in stmt.body:
            loc = _stmt_var_decl(s, name)
            if loc is not None:
                return loc

    if isinstance(stmt, (ast.IfStmt, ast.WhileStmt)):
        children = _child_stmts(stmt)
        for child_list in children:
            for s in child_list:
                loc = _stmt_var_decl(s, name)
                if loc is not None:
                    return loc

    return None


def _find_const_decl(program: ast.Program, name: str) -> ast.TextLocation | None:
    """Find declaration of a constant (_name)."""
    for stmt in program.statements:
        if isinstance(stmt, ast.ConstantDeclStmt) and stmt.name == name:
            return stmt.loc
    return None


def _find_ext_var_decl(program: ast.Program, name: str) -> ast.TextLocation | None:
    """External variables (@name) are defined externally, no declaration to find."""
    return None


def _find_func_decl(program: ast.Program, name: str) -> ast.TextLocation | None:
    """Find declaration of a function (regular or extern)."""
    for stmt in program.statements:
        if isinstance(stmt, ast.FuncDeclStmt) and stmt.name.upper() == name.upper():
            return stmt.loc
        if isinstance(stmt, ast.ExternFuncStmt) and stmt.name.upper() == name.upper():
            return stmt.loc
    return None


def _decl_name(program: ast.Program, word: str) -> str:
    """Return the declared name for the symbol (for display)."""
    for stmt in program.statements:
        if isinstance(stmt, ast.FuncDeclStmt) and stmt.name.upper() == word.upper():
            return stmt.name
        if isinstance(stmt, ast.ExternFuncStmt) and stmt.name.upper() == word.upper():
            return stmt.name
    return word


def _child_stmts(stmt: ast.Stmt) -> list[list[ast.Stmt]]:
    """Return child statement lists for block statements."""
    if isinstance(stmt, ast.IfStmt):
        children = [stmt.body] + [b for _, b in stmt.elifs]
        if stmt.else_body:
            children.append(stmt.else_body)
        return children
    if isinstance(stmt, ast.ForStmt):
        return [stmt.body]
    if isinstance(stmt, ast.WhileStmt):
        return [stmt.body]
    if isinstance(stmt, ast.FuncDeclStmt):
        return [stmt.body]
    return []
