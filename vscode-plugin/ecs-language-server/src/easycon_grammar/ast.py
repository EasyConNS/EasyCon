"""AST nodes for EasyCon Python script engine."""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Optional, List, Any


@dataclass
class TextLocation:
    line: int = 0
    column: int = 0


@dataclass
class AstNode:
    loc: TextLocation = field(default_factory=TextLocation)


# Expressions

@dataclass
class Expr(AstNode):
    pass


@dataclass
class LiteralExpr(Expr):
    value: Any = None


@dataclass
class VariableExpr(Expr):
    name: str = ""
    read_only: bool = False


@dataclass
class ConstVarExpr(VariableExpr):
    pass


@dataclass
class ExtVarExpr(Expr):
    name: str = ""


@dataclass
class BinaryExpr(Expr):
    op: str = ""
    left: Expr = field(default_factory=Expr)
    right: Expr = field(default_factory=Expr)


@dataclass
class UnaryExpr(Expr):
    op: str = ""
    operand: Expr = field(default_factory=Expr)


@dataclass
class ParenExpr(Expr):
    inner: Expr = field(default_factory=Expr)


@dataclass
class IndexDefExpr(Expr):
    items: List[Expr] = field(default_factory=list)


@dataclass
class IndexExpr(Expr):
    name: str = ""
    index: Expr = field(default_factory=Expr)


@dataclass
class SliceExpr(Expr):
    name: str = ""
    start: Optional[Expr] = None
    end: Expr = field(default_factory=Expr)


@dataclass
class CallExpr(Expr):
    name: str = ""
    args: List[Expr] = field(default_factory=list)


# External function parameter info

@dataclass
class ExternParamInfo:
    name: str = ""
    type: str = ""


# Statements

@dataclass
class Stmt(AstNode):
    comment: str = ""


@dataclass
class EmptyStmt(Stmt):
    pass


@dataclass
class ImportStmt(Stmt):
    module: str = ""


@dataclass
class ConstantDeclStmt(Stmt):
    name: str = ""
    value: Expr = field(default_factory=Expr)


@dataclass
class AssignmentStmt(Stmt):
    target: Expr = field(default_factory=Expr)
    op: str = "="
    value: Expr = field(default_factory=Expr)


@dataclass
class ReturnStmt(Stmt):
    value: Optional[Expr] = None


@dataclass
class CallStmt(Stmt):
    name: str = ""
    args: List[Expr] = field(default_factory=list)
    is_call: bool = False  # True if from CALL keyword, False if direct (PRINT, etc.)


@dataclass
class WaitStmt(Stmt):
    duration: Expr = field(default_factory=Expr)
    omitted: bool = False


@dataclass
class KeyPressStmt(Stmt):
    key: str = ""
    duration: Optional[Expr] = None


@dataclass
class KeyActStmt(Stmt):
    key: str = ""
    up: bool = False


@dataclass
class StickPressStmt(Stmt):
    key: str = ""
    direction: str = ""
    duration: Optional[Expr] = None


@dataclass
class StickActStmt(Stmt):
    key: str = ""
    direction: str = ""


@dataclass
class BreakStmt(Stmt):
    level: int = 1


@dataclass
class ContinueStmt(Stmt):
    pass


# Block statements

@dataclass
class IfStmt(Stmt):
    condition: Expr = field(default_factory=Expr)
    body: List[Stmt] = field(default_factory=list)
    elifs: List[tuple] = field(default_factory=list)  # [(Expr, [Stmt])]
    else_body: List[Stmt] = field(default_factory=list)


@dataclass
class ForStmt(Stmt):
    iter_name: Optional[str] = None
    lower: Optional[Expr] = None
    upper: Expr = field(default_factory=Expr)
    step: Optional[Expr] = None
    body: List[Stmt] = field(default_factory=list)
    infinite: bool = False


@dataclass
class WhileStmt(Stmt):
    condition: Expr = field(default_factory=Expr)
    body: List[Stmt] = field(default_factory=list)


@dataclass
class FuncDeclStmt(Stmt):
    name: str = ""
    params: List[str] = field(default_factory=list)
    body: List[Stmt] = field(default_factory=list)


@dataclass
class BeepStmt(Stmt):
    frequency: Expr = field(default_factory=Expr)
    duration: Expr = field(default_factory=Expr)


@dataclass
class AmiiboStmt(Stmt):
    index: Expr = field(default_factory=Expr)


@dataclass
class ExternFuncStmt(Stmt):
    name: str = ""
    params: List[ExternParamInfo] = field(default_factory=list)
    return_type: str = ""
    library: str = ""


@dataclass
class Program(AstNode):
    statements: List[Stmt] = field(default_factory=list)
