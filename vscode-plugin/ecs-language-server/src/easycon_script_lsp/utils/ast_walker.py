"""AST traversal utilities for the ECS LSP."""

from __future__ import annotations
from dataclasses import dataclass, field
from typing import Iterator, Optional, Set

from easycon_grammar import ast


@dataclass
class SymbolInfo:
    name: str
    kind: str  # "constant", "variable", "function", "parameter"
    loc: ast.TextLocation
    children: list[SymbolInfo] = field(default_factory=list)


@dataclass
class ScopeInfo:
    variables: Set[str] = field(default_factory=set)
    functions: Set[str] = field(default_factory=set)
    constants: Set[str] = field(default_factory=set)


class ASTWalker:
    """Walk ECS AST to extract symbols and scope information."""

    def __init__(self, program: ast.Program):
        self.program = program

    # ------------------------------------------------------------------ #
    #  Document symbols
    # ------------------------------------------------------------------ #
    def iter_symbols(self) -> Iterator[SymbolInfo]:
        """Yield all top-level symbols."""
        for stmt in self.program.statements:
            sym = self._stmt_to_symbol(stmt)
            if sym is not None:
                yield sym

    def _stmt_to_symbol(self, stmt: ast.Stmt) -> SymbolInfo | None:
        if isinstance(stmt, ast.ConstantDeclStmt):
            return SymbolInfo(name=stmt.name, kind="constant", loc=stmt.loc)

        if isinstance(stmt, ast.AssignmentStmt):
            target = stmt.target
            if isinstance(target, ast.VariableExpr) and not target.read_only:
                return SymbolInfo(name=target.name, kind="variable", loc=target.loc)

        if isinstance(stmt, ast.FuncDeclStmt):
            children = [
                SymbolInfo(name=p, kind="parameter", loc=stmt.loc)
                for p in stmt.params
            ]
            return SymbolInfo(
                name=stmt.name, kind="function", loc=stmt.loc, children=children
            )

        if isinstance(stmt, ast.ExternFuncStmt):
            children = [
                SymbolInfo(name=p.name, kind="parameter", loc=stmt.loc)
                for p in stmt.params
            ]
            return SymbolInfo(
                name=stmt.name, kind="function", loc=stmt.loc, children=children
            )

        return None

    # ------------------------------------------------------------------ #
    #  Scope at position (for completion)
    # ------------------------------------------------------------------ #
    def get_scope_at_position(self, line: int, column: int) -> ScopeInfo:
        """Return visible names at the given 0-based position."""
        scope = ScopeInfo()
        self._collect_top_level_scope(scope)
        self._collect_local_scope(self.program.statements, line, column, scope)
        return scope

    def _collect_top_level_scope(self, scope: ScopeInfo) -> None:
        for stmt in self.program.statements:
            if isinstance(stmt, ast.ConstantDeclStmt):
                scope.constants.add(stmt.name)
            elif isinstance(stmt, ast.AssignmentStmt):
                target = stmt.target
                if isinstance(target, ast.VariableExpr) and not target.read_only:
                    scope.variables.add(target.name)
            elif isinstance(stmt, ast.FuncDeclStmt):
                scope.functions.add(stmt.name)
            elif isinstance(stmt, ast.ExternFuncStmt):
                scope.functions.add(stmt.name)

    def _collect_local_scope(
        self,
        statements: list[ast.Stmt],
        line: int,
        column: int,
        scope: ScopeInfo,
    ) -> bool:
        """Recursively collect local variables visible at (line, column).

        Returns True if the position was found inside this block.
        """
        for stmt in statements:
            if isinstance(stmt, ast.FuncDeclStmt):
                if self._contains(stmt.loc, line, column, statements):
                    for p in stmt.params:
                        scope.variables.add(p)
                    if self._collect_local_scope(stmt.body, line, column, scope):
                        return True

            elif isinstance(stmt, (ast.IfStmt, ast.ForStmt, ast.WhileStmt)):
                body = []
                if isinstance(stmt, ast.IfStmt):
                    body = stmt.body
                    for _, elif_body in stmt.elifs:
                        body.extend(elif_body)
                    body.extend(stmt.else_body)
                elif isinstance(stmt, ast.ForStmt):
                    body = stmt.body
                    if stmt.iter_name:
                        scope.variables.add(stmt.iter_name)
                elif isinstance(stmt, ast.WhileStmt):
                    body = stmt.body

                if self._collect_local_scope(body, line, column, scope):
                    return True

        return False

    @staticmethod
    def _contains(
        loc: ast.TextLocation, line: int, column: int, block: list[ast.Stmt]
    ) -> bool:
        """Rough check: position is at or after the statement's start."""
        return loc.line <= line + 1
