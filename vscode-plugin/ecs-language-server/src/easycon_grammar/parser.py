"""Lark-based parser for EasyCon Python."""
from __future__ import annotations
import re
from pathlib import Path
from typing import List, Optional, Tuple

from dataclasses import dataclass, field
from lark import Lark, Transformer, Token, Tree

from . import ast


class ParseError(Exception):
    pass


# Load grammar
_GRAMMAR_PATH = Path(__file__).parent / "grammar" / "ecp_grammar.lark"


def _get_loc(token_or_tree) -> ast.TextLocation:
    if isinstance(token_or_tree, Token):
        return ast.TextLocation(line=token_or_tree.line or 0, column=token_or_tree.column or 0)
    if hasattr(token_or_tree, "meta") and token_or_tree.meta:
        return ast.TextLocation(line=token_or_tree.meta.line or 0, column=token_or_tree.meta.column or 0)
    return ast.TextLocation()


class _ExprTransformer(Transformer):
    """Convert Lark expression trees to AST Expr nodes."""

    def INT(self, tok: Token) -> ast.LiteralExpr:
        return ast.LiteralExpr(loc=_get_loc(tok), value=int(tok.value))

    def NUMBER(self, tok: Token) -> ast.LiteralExpr:
        return ast.LiteralExpr(loc=_get_loc(tok), value=float(tok.value))

    def STRING(self, tok: Token) -> ast.LiteralExpr:
        raw = tok.value
        if (raw.startswith('"') and raw.endswith('"')) or (raw.startswith("'") and raw.endswith("'")):
            raw = raw[1:-1]
        # Simple escape sequences
        raw = raw.replace("\\n", "\n").replace("\\t", "\t").replace("\\r", "\r").replace("\\\\", "\\")
        return ast.LiteralExpr(loc=_get_loc(tok), value=raw)

    def CONST(self, tok: Token) -> ast.ConstVarExpr:
        return ast.ConstVarExpr(loc=_get_loc(tok), name=tok.value)

    def VAR(self, tok: Token) -> ast.VariableExpr:
        return ast.VariableExpr(loc=_get_loc(tok), name=tok.value)

    def EX_VAR(self, tok: Token) -> ast.ExtVarExpr:
        name = tok.value[1:] if tok.value.startswith("@") else tok.value
        return ast.ExtVarExpr(loc=_get_loc(tok), name=name)

    def IDENT(self, tok: Token) -> ast.VariableExpr:
        return ast.VariableExpr(loc=_get_loc(tok), name=tok.value, read_only=True)

    def bool_true(self, items) -> ast.LiteralExpr:
        return ast.LiteralExpr(value=True)

    def bool_false(self, items) -> ast.LiteralExpr:
        return ast.LiteralExpr(value=False)

    def array_def(self, items) -> ast.IndexDefExpr:
        return ast.IndexDefExpr(loc=_get_loc(items[0]) if items else ast.TextLocation(), items=[i for i in items if i is not None])

    def func_call(self, items) -> ast.CallExpr:
        first = items[0]
        if isinstance(first, ast.VariableExpr):
            name = first.name
        elif isinstance(first, Token):
            name = first.value
        else:
            name = str(first)
        args = [i for i in items[1:] if i is not None]
        return ast.CallExpr(loc=_get_loc(first), name=name, args=args)

    def index_expr(self, items) -> ast.IndexExpr:
        first = items[0]
        if isinstance(first, ast.VariableExpr):
            name = first.name
        elif isinstance(first, Token):
            name = first.value
        else:
            name = str(first)
        return ast.IndexExpr(loc=_get_loc(first), name=name, index=items[1])

    def slice_expr(self, items) -> ast.SliceExpr:
        first = items[0]
        if isinstance(first, ast.VariableExpr):
            name = first.name
        elif isinstance(first, Token):
            name = first.value
        else:
            name = str(first)
        if len(items) == 2:
            return ast.SliceExpr(loc=_get_loc(first), name=name, start=None, end=items[1])
        return ast.SliceExpr(loc=_get_loc(first), name=name, start=items[1], end=items[2])

    def type_name(self, items):
        return items[0]

    def paren_expr(self, items) -> ast.ParenExpr:
        inner = items[0]
        return ast.ParenExpr(loc=_get_loc(inner) if hasattr(inner, "loc") else ast.TextLocation(), inner=inner)

    def neg(self, items) -> ast.UnaryExpr:
        return ast.UnaryExpr(op="-", operand=items[0])

    def bitnot(self, items) -> ast.UnaryExpr:
        return ast.UnaryExpr(op="~", operand=items[0])

    def lnot(self, items) -> ast.UnaryExpr:
        return ast.UnaryExpr(op="not", operand=items[0])

    def bin_or(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="or", left=items[0], right=items[1])

    def bin_and(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="and", left=items[0], right=items[1])

    def bin_eq(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="==", left=items[0], right=items[1])

    def bin_ne(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="!=", left=items[0], right=items[1])

    def bin_lt(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="<", left=items[0], right=items[1])

    def bin_le(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="<=", left=items[0], right=items[1])

    def bin_gt(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op=">", left=items[0], right=items[1])

    def bin_ge(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op=">=", left=items[0], right=items[1])

    def bin_add(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="+", left=items[0], right=items[1])

    def bin_sub(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="-", left=items[0], right=items[1])

    def bin_mul(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="*", left=items[0], right=items[1])

    def bin_div(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="/", left=items[0], right=items[1])

    def bin_idiv(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="\\", left=items[0], right=items[1])

    def bin_mod(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="%", left=items[0], right=items[1])

    def bin_bitand(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="&", left=items[0], right=items[1])

    def bin_bitor(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="|", left=items[0], right=items[1])

    def bin_xor(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="^", left=items[0], right=items[1])

    def bin_shl(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op="<<", left=items[0], right=items[1])

    def bin_shr(self, items) -> ast.BinaryExpr:
        return ast.BinaryExpr(op=">>", left=items[0], right=items[1])


class _StmtTransformer(Transformer):
    """Convert Lark statement/line trees to AST Stmt nodes."""

    def __init__(self):
        super().__init__()
        self.expr_transformer = _ExprTransformer()

    def _transform_expr(self, tree_or_token):
        if isinstance(tree_or_token, Token):
            return self.expr_transformer.transform(tree_or_token)
        return self.expr_transformer.transform(tree_or_token)

    def type_name(self, items):
        """Unwrap type_name: IDENT rule, returning the IDENT Token."""
        return items[0]

    def import_stmt(self, items) -> ast.ImportStmt:
        mod = items[1].value
        if mod.startswith('"') and mod.endswith('"'):
            mod = mod[1:-1]
        elif mod.startswith("'") and mod.endswith("'"):
            mod = mod[1:-1]
        return ast.ImportStmt(loc=_get_loc(items[0]), module=mod)

    def const_decl(self, items) -> ast.ConstantDeclStmt:
        return ast.ConstantDeclStmt(loc=_get_loc(items[0]), name=items[0].value, value=self._transform_expr(items[1]))

    def var_decl(self, items) -> ast.AssignmentStmt:
        target = self.expr_transformer.VAR(items[0])
        return ast.AssignmentStmt(loc=_get_loc(items[0]), target=target, op="=", value=self._transform_expr(items[1]))

    def assignment(self, items) -> ast.AssignmentStmt:
        target = self._transform_expr(items[0])
        op_token = items[1]
        op = op_token.value if isinstance(op_token, Token) else str(op_token)
        return ast.AssignmentStmt(loc=_get_loc(items[0]), target=target, op=op, value=self._transform_expr(items[2]))

    def assign_target(self, items) -> ast.Expr:
        return items[0]

    def func_decl(self, items) -> ast.FuncDeclStmt:
        # items[0] is FUNC_KW, items[1] is IDENT
        name_token = items[1] if len(items) > 1 else items[0]
        if isinstance(name_token, ast.VariableExpr):
            name = name_token.name
        elif isinstance(name_token, Token):
            name = name_token.value
        else:
            name = str(name_token)
        params = []
        idx = 2
        if idx < len(items) and isinstance(items[idx], list):
            params = items[idx]
            idx += 1
        return ast.FuncDeclStmt(loc=_get_loc(name_token), name=name, params=params)

    def param_list(self, items) -> List[str]:
        return [str(i.value if isinstance(i, Token) else i) for i in items]

    def param(self, items) -> str:
        return items[0].value if isinstance(items[0], Token) else str(items[0])

    def return_stmt(self, items) -> ast.ReturnStmt:
        if len(items) > 1:
            return ast.ReturnStmt(loc=_get_loc(items[0]), value=self._transform_expr(items[1]))
        if items and not (isinstance(items[0], Token) and items[0].type in ("RETURN_KW",)):
            return ast.ReturnStmt(loc=_get_loc(items[0]), value=self._transform_expr(items[0]))
        return ast.ReturnStmt()

    def extern_func_decl(self, items) -> ast.ExternFuncStmt:
        """EXTERN FUNC name(params):ret_type FROM \"lib\"

        Tree children (parens/colon are dropped by Lark):
          With params (7):    EXTERN_KW, FUNC_KW, IDENT, extern_param_list, type_name, FROM_KW, STRING
          Without params (6): EXTERN_KW, FUNC_KW, IDENT, type_name, FROM_KW, STRING
        """
        name_token = items[2]
        name = name_token.value if isinstance(name_token, Token) else str(name_token)

        params = []
        # Use negative indices for the fixed-layout tail: ... type_name, FROM_KW, STRING
        if len(items) == 7 and items[3] is not None:
            params = items[3]
            type_token = items[-3]
        else:
            type_token = items[-3]
        string_token = items[-1]

        return_type = type_token.value if isinstance(type_token, Token) else str(type_token)

        library = string_token.value
        if (library.startswith('"') and library.endswith('"')) or (library.startswith("'") and library.endswith("'")):
            library = library[1:-1]

        return ast.ExternFuncStmt(
            loc=_get_loc(name_token),
            name=name,
            params=params,
            return_type=return_type,
            library=library,
        )

    def extern_param_list(self, items) -> list:
        return [i for i in items if i is not None]

    def extern_param(self, items) -> ast.ExternParamInfo:
        name_tok = items[0]
        name = name_tok.value if isinstance(name_tok, Token) else str(name_tok)
        type_str = ""
        if len(items) >= 2 and items[1] is not None:
            type_tok = items[1]
            type_str = type_tok.value if isinstance(type_tok, Token) else str(type_tok)
        return ast.ExternParamInfo(name=name, type=type_str)

    def call_stmt(self, items) -> ast.CallStmt:
        name_token = items[1] if len(items) > 1 else items[0]
        if isinstance(name_token, ast.VariableExpr):
            name = name_token.name
        elif isinstance(name_token, Token):
            name = name_token.value
        else:
            name = str(name_token)
        return ast.CallStmt(loc=_get_loc(name_token), name=name, is_call=True)

    def wait_stmt(self, items) -> ast.WaitStmt:
        expr = items[1] if len(items) > 1 else items[0]
        return ast.WaitStmt(loc=_get_loc(items[0]), duration=self._transform_expr(expr), omitted=False)

    def wait_omitted(self, items) -> ast.WaitStmt:
        return ast.WaitStmt(loc=_get_loc(items[0]), duration=self._transform_expr(items[0]), omitted=True)

    def func_stmt(self, items) -> ast.CallStmt:
        first = items[0]
        if isinstance(first, ast.VariableExpr):
            name = first.name
        elif isinstance(first, Token):
            name = first.value
        else:
            name = str(first)
        args = [self._transform_expr(i) for i in items[1:]]
        return ast.CallStmt(loc=_get_loc(first), name=name, args=args, is_call=False)

    def key_action(self, items) -> ast.Stmt:
        key = items[0].value.upper()
        if len(items) == 1:
            return ast.KeyPressStmt(loc=_get_loc(items[0]), key=key, duration=None)
        mod = items[1]
        if isinstance(mod, Token):
            val = mod.value.upper()
            if val == "DOWN":
                return ast.KeyActStmt(loc=_get_loc(items[0]), key=key, up=False)
            if val == "UP":
                return ast.KeyActStmt(loc=_get_loc(items[0]), key=key, up=True)
        return ast.KeyPressStmt(loc=_get_loc(items[0]), key=key, duration=self._transform_expr(mod))

    def dir_action(self, items) -> ast.Stmt:
        """D-pad direction used as a button press (e.g. UP 100)."""
        key = items[0].value.upper()
        if len(items) == 1:
            return ast.KeyPressStmt(loc=_get_loc(items[0]), key=key, duration=None)
        mod = items[1]
        if isinstance(mod, Token):
            val = mod.value.upper()
            if val == "DOWN":
                return ast.KeyActStmt(loc=_get_loc(items[0]), key=key, up=False)
            if val == "UP":
                return ast.KeyActStmt(loc=_get_loc(items[0]), key=key, up=True)
        return ast.KeyPressStmt(loc=_get_loc(items[0]), key=key, duration=self._transform_expr(mod))

    def stick_action(self, items) -> ast.Stmt:
        key = items[0].value.upper()
        mod = items[1]
        if isinstance(mod, Token) and mod.value.upper() == "RESET":
            return ast.StickActStmt(loc=_get_loc(items[0]), key=key, direction="RESET")
        # mod is a tuple/list: (direction_token,) for stick_direction,
        # or (direction_token, duration_expr) for stick_direction_duration
        if isinstance(mod, (list, tuple)) and len(mod) >= 1:
            direction = self._resolve_direction(mod[0])
            if len(mod) >= 2 and mod[1] is not None:
                return ast.StickPressStmt(loc=_get_loc(items[0]), key=key, direction=direction, duration=self._transform_expr(mod[1]))
            return ast.StickActStmt(loc=_get_loc(items[0]), key=key, direction=direction)
        direction = self._resolve_direction(mod)
        return ast.StickActStmt(loc=_get_loc(items[0]), key=key, direction=direction)

    @staticmethod
    def _resolve_direction(item) -> str:
        if isinstance(item, Token):
            return item.value.upper()
        if isinstance(item, ast.LiteralExpr) and isinstance(item.value, int):
            return str(item.value)
        return str(item).upper()

    def key_mod(self, items):
        return items[0] if items else None

    def stick_mod(self, items):
        if len(items) == 1 and isinstance(items[0], Token) and items[0].value.upper() == "RESET":
            return items[0]
        return items

    def stick_direction_duration(self, items):
        # items: [direction, COMMA, expression] — filter out COMMA
        return [items[0], items[-1]]

    def stick_direction(self, items):
        # items[0] is either a DIRECTION_KEY Token or an INT Token
        raw = items[0]
        if isinstance(raw, Token) and raw.type == "INT":
            return self.expr_transformer.transform(raw)
        return raw

    def if_open(self, items) -> ast.Stmt:
        cond = items[1] if len(items) > 1 else items[0]
        return _IfOpen(loc=_get_loc(items[0]), condition=self._transform_expr(cond))

    def elif_open(self, items) -> ast.Stmt:
        cond = items[1] if len(items) > 1 else items[0]
        return _ElifOpen(loc=_get_loc(items[0]), condition=self._transform_expr(cond))

    def else_open(self, items) -> ast.Stmt:
        return _ElseOpen(loc=_get_loc(items[0]))

    def endif_stmt(self, items) -> ast.Stmt:
        return _EndifStmt(loc=_get_loc(items[0]) if items else ast.TextLocation())

    def for_open(self, items) -> ast.Stmt:
        if not items:
            return _ForOpen(loc=ast.TextLocation())
        # for_open children: [FOR_KW, for_iter_items]
        for_iter_items = items[1] if len(items) > 1 else None
        if for_iter_items is None:
            return _ForOpen(loc=ast.TextLocation())
        if isinstance(for_iter_items, list):
            # Case 3 first: VAR = lower TO upper [STEP step] — distinguished by length >= 4
            # Children without STEP: [VAR, lower, TO_KW, upper]
            # Children with STEP:    [VAR, lower, TO_KW, upper, STEP_KW, step]
            if len(for_iter_items) >= 4:
                iter_name = for_iter_items[0].value
                lower = for_iter_items[1]
                upper = for_iter_items[3]
                step = None
                if len(for_iter_items) >= 6:
                    step_raw = for_iter_items[5]
                    step = self._transform_expr(step_raw) if not isinstance(step_raw, ast.Expr) else step_raw
                if not isinstance(lower, ast.Expr):
                    lower = self._transform_expr(lower)
                if not isinstance(upper, ast.Expr):
                    upper = self._transform_expr(upper)
                return _ForOpen(loc=_get_loc(for_iter_items[0]), iter_name=iter_name, lower=lower, upper=upper, step=step)
            # Case 1: INT [STEP step] (FOR 5, FOR 5 STEP 2)
            if isinstance(for_iter_items[0], Token) and for_iter_items[0].type == "INT":
                step = self._transform_expr(for_iter_items[2]) if len(for_iter_items) >= 3 else None
                return _ForOpen(loc=_get_loc(for_iter_items[0]), upper=self._transform_expr(for_iter_items[0]), step=step)
            # Case 2: VAR [STEP step] (FOR $count, FOR $count STEP 2)
            if isinstance(for_iter_items[0], Token) and for_iter_items[0].type == "VAR":
                step = self._transform_expr(for_iter_items[2]) if len(for_iter_items) >= 3 else None
                return _ForOpen(loc=_get_loc(for_iter_items[0]), upper=self._transform_expr(for_iter_items[0]), step=step)
            # Case 2b: CONST [STEP step] (FOR _count, FOR _count STEP 2)
            if isinstance(for_iter_items[0], Token) and for_iter_items[0].type == "CONST":
                step = self._transform_expr(for_iter_items[2]) if len(for_iter_items) >= 3 else None
                return _ForOpen(loc=_get_loc(for_iter_items[0]), upper=self._transform_expr(for_iter_items[0]), step=step)
        # Single token (e.g., INT when for_iter matched first branch and returned the token directly)
        if isinstance(for_iter_items, Token):
            return _ForOpen(loc=_get_loc(for_iter_items), upper=self._transform_expr(for_iter_items))
        return _ForOpen(loc=ast.TextLocation())

    def for_iter(self, items):
        return items

    def step_bound(self, items):
        return self.expr_transformer.transform(items[0])

    def loop_bound(self, items):
        return self.expr_transformer.transform(items[0])

    def next_stmt(self, items) -> ast.Stmt:
        return _NextStmt(loc=_get_loc(items[0]) if items else ast.TextLocation())

    def while_open(self, items) -> ast.Stmt:
        cond = items[1] if len(items) > 1 else items[0]
        return _WhileOpen(loc=_get_loc(items[0]), condition=self._transform_expr(cond))

    def end_stmt(self, items) -> ast.Stmt:
        return _EndStmt(loc=_get_loc(items[0]) if items else ast.TextLocation())

    def break_stmt(self, items) -> ast.BreakStmt:
        level = 1
        loc = ast.TextLocation()
        for item in items:
            if isinstance(item, Token):
                if item.type == "INT":
                    level = int(item.value)
                loc = _get_loc(item)
        return ast.BreakStmt(loc=loc, level=level)

    def continue_stmt(self, items) -> ast.ContinueStmt:
        return ast.ContinueStmt()

    def beep_stmt(self, items) -> ast.BeepStmt:
        freq = items[1]
        dur = items[2]  # comma is consumed by Lark, not present in items
        freq_expr = self._transform_expr(freq)
        dur_expr = self._transform_expr(dur)
        return ast.BeepStmt(loc=_get_loc(items[0]), frequency=freq_expr, duration=dur_expr)

    def amiibo_stmt(self, items) -> ast.AmiiboStmt:
        index_expr = self._transform_expr(items[1]) if len(items) > 1 else ast.LiteralExpr(value=0)
        return ast.AmiiboStmt(loc=_get_loc(items[0]), index=index_expr)

    def comment(self, items):
        # Return the raw COMMENT token so line() can attach it to the statement
        return items[0] if items else None

    def comment_stmt(self, items) -> ast.EmptyStmt:
        s = ast.EmptyStmt()
        if items:
            s.comment = items[0].value
        return s

    def statement(self, items):
        return items[0] if items else ast.EmptyStmt()

    def line(self, items):
        # line: statement [comment] _NL | comment _NL | _NL
        if not items:
            return ast.EmptyStmt()
        stmt = items[0]
        if isinstance(stmt, Token) and stmt.type == "COMMENT":
            return ast.EmptyStmt(comment=stmt.value)
        # comment can be a Token (from comment rule) or the raw value already set
        if len(items) > 1:
            comment_item = items[1]
            comment_val = None
            if isinstance(comment_item, Token) and comment_item.type == "COMMENT":
                comment_val = comment_item.value
            elif isinstance(comment_item, ast.EmptyStmt) and comment_item.comment:
                comment_val = comment_item.comment
            if comment_val and isinstance(stmt, ast.Stmt):
                stmt.comment = comment_val
        return stmt

    def program(self, items) -> ast.Program:
        flat = []
        for item in items:
            if isinstance(item, list):
                flat.extend(item)
            else:
                flat.append(item)
        return _build_blocks(flat)


# Internal markers for block building

@dataclass
class _IfOpen(ast.Stmt):
    condition: ast.Expr = field(default_factory=ast.Expr)


@dataclass
class _ElifOpen(ast.Stmt):
    condition: ast.Expr = field(default_factory=ast.Expr)


@dataclass
class _ElseOpen(ast.Stmt):
    pass


@dataclass
class _EndifStmt(ast.Stmt):
    pass


@dataclass
class _ForOpen(ast.Stmt):
    iter_name: Optional[str] = None
    lower: Optional[ast.Expr] = None
    upper: Optional[ast.Expr] = None
    step: Optional[ast.Expr] = None


@dataclass
class _NextStmt(ast.Stmt):
    pass


@dataclass
class _WhileOpen(ast.Stmt):
    condition: ast.Expr = field(default_factory=ast.Expr)


@dataclass
class _EndStmt(ast.Stmt):
    pass


def _build_blocks(flat_stmts: List[ast.Stmt]) -> ast.Program:
    """Assemble flat statement list into nested blocks, matching C# Parser.ParseProgram logic."""
    result: List[ast.Stmt] = []
    stack: List[List[ast.Stmt]] = [result]

    i = 0
    while i < len(flat_stmts):
        stmt = flat_stmts[i]

        if isinstance(stmt, ast.EmptyStmt):
            stack[-1].append(stmt)
            i += 1
            continue

        if isinstance(stmt, ast.ImportStmt):
            # Import validation: must be at top
            if any(not isinstance(s, (ast.ImportStmt, ast.EmptyStmt)) for s in stack[0]):
                raise ParseError(f"导入只能在脚本开头 (line {stmt.loc.line})")
            stack[-1].append(stmt)
            i += 1
            continue

        if isinstance(stmt, ast.ExternFuncStmt):
            if len(stack) > 1:
                raise ParseError(f"EXTERN FUNC只能在顶层定义 (line {stmt.loc.line})")
            stack[-1].append(stmt)
            i += 1
            continue

        if isinstance(stmt, (_IfOpen, _ForOpen, _WhileOpen, ast.FuncDeclStmt)):
            if isinstance(stmt, ast.FuncDeclStmt) and len(stack) > 1:
                raise ParseError(f"函数必须在顶层定义 (line {stmt.loc.line})")
            stack[-1].append(stmt)
            stack.append([stmt])
            i += 1
            continue

        if isinstance(stmt, _ElifOpen):
            if len(stack) <= 1:
                raise ParseError(f"ELIF需要对应的If语句 (line {stmt.loc.line})")
            block = stack[-1]
            if not block or not isinstance(block[0], (_IfOpen, _ElifOpen)):
                raise ParseError(f"ELIF需要对应的If语句 (line {stmt.loc.line})")
            if any(isinstance(s, _ElseOpen) for s in block):
                raise ParseError(f"Else语句后不能再接Elif (line {stmt.loc.line})")
            stack[-1].append(stmt)
            i += 1
            continue

        if isinstance(stmt, _ElseOpen):
            if len(stack) <= 1:
                raise ParseError(f"ELSE需要对应的If语句 (line {stmt.loc.line})")
            block = stack[-1]
            if not block or not isinstance(block[0], (_IfOpen, _ElifOpen)):
                raise ParseError(f"ELSE需要对应的If语句 (line {stmt.loc.line})")
            if any(isinstance(s, _ElseOpen) for s in block):
                raise ParseError(f"一个If只能对应一个Else (line {stmt.loc.line})")
            stack[-1].append(stmt)
            i += 1
            continue

        if isinstance(stmt, (_EndifStmt, _NextStmt, _EndStmt)):
            if len(stack) <= 1:
                raise ParseError(f"多余的结束语句 (line {stmt.loc.line})")
            block = stack[-1]
            opener = block[0]
            valid = True
            if isinstance(stmt, _EndifStmt) and not isinstance(opener, (_IfOpen, _ElifOpen)):
                valid = False
                msg = "ENDIF需要对应的If语句"
            elif isinstance(stmt, _NextStmt) and not isinstance(opener, _ForOpen):
                valid = False
                msg = "NEXT需要对应的For语句"
            elif isinstance(stmt, _EndStmt) and isinstance(opener, ast.FuncDeclStmt):
                valid = True  # ENDFUNC handled here
            elif isinstance(stmt, _EndStmt) and not isinstance(opener, (_WhileOpen, ast.FuncDeclStmt, _IfOpen, _ElifOpen)):
                valid = False
                msg = "END需要对应的语句开头"
            else:
                msg = ""

            if not valid:
                raise ParseError(f"{msg} (line {stmt.loc.line})")

            stack.pop()
            parent = stack[-1]
            # Replace opener marker with proper AST block node
            if isinstance(opener, (_IfOpen, _ElifOpen)):
                if_stmts = block[1:]  # skip the IF marker
                if_body: List[ast.Stmt] = []
                elifs: List[Tuple[ast.Expr, List[ast.Stmt]]] = []
                else_body: List[ast.Stmt] = []
                current_block = if_body
                first_cond = opener.condition if isinstance(opener, _IfOpen) else None
                j = 0
                while j < len(if_stmts):
                    s = if_stmts[j]
                    if isinstance(s, _ElifOpen):
                        if current_block is if_body:
                            elifs.append((s.condition, []))
                        else:
                            elifs[-1] = (elifs[-1][0], current_block)
                            elifs.append((s.condition, []))
                        current_block = elifs[-1][1]
                    elif isinstance(s, _ElseOpen):
                        if elifs:
                            elifs[-1] = (elifs[-1][0], current_block)
                        current_block = else_body
                    else:
                        current_block.append(s)
                    j += 1
                if elifs and current_block is not else_body:
                    elifs[-1] = (elifs[-1][0], current_block)
                if_node = ast.IfStmt(loc=opener.loc, condition=first_cond or ast.LiteralExpr(value=True), body=if_body, elifs=elifs, else_body=else_body, comment=opener.comment)
                parent[-1] = if_node
            elif isinstance(opener, _ForOpen):
                body = block[1:]
                for_node = ast.ForStmt(loc=opener.loc, iter_name=opener.iter_name, lower=opener.lower, upper=opener.upper or ast.LiteralExpr(value=0), step=opener.step, body=body, infinite=opener.upper is None, comment=opener.comment)
                parent[-1] = for_node
            elif isinstance(opener, _WhileOpen):
                body = block[1:]
                while_node = ast.WhileStmt(loc=opener.loc, condition=opener.condition, body=body, comment=opener.comment)
                parent[-1] = while_node
            elif isinstance(opener, ast.FuncDeclStmt):
                body = block[1:]
                opener.body = body
                parent[-1] = opener
            i += 1
            continue

        # Regular statement
        stack[-1].append(stmt)
        i += 1

    if len(stack) > 1:
        opener = stack[-1][0]
        raise ParseError(f"语句块没有正确结束 (line {opener.loc.line})")

    return ast.Program(statements=result)


# Token patterns for syntax highlighting, ordered by priority.
# For overlapping patterns the longest match wins (maximal munch).
_TOKEN_PATTERNS = [
    # Script keywords (must be before identifiers)
    ("IMPORT_KW", r"IMPORT"),
    ("IF_KW", r"IF"),
    ("ELIF_KW", r"ELIF"),
    ("ELSE_KW", r"ELSE"),
    ("ENDIF_KW", r"ENDIF"),
    ("WHILE_KW", r"WHILE"),
    ("FOR_KW", r"FOR"),
    ("TO_KW", r"TO"),
    ("IN_KW", r"IN"),
    ("STEP_KW", r"STEP"),
    ("BREAK_KW", r"BREAK"),
    ("CONTINUE_KW", r"CONTINUE"),
    ("NEXT_KW", r"NEXT"),
    ("FUNC_KW", r"FUNC"),
    ("RETURN_KW", r"RETURN"),
    ("ENDFUNC_KW", r"ENDFUNC"),
    ("END_KW", r"END"),
    ("TRUE_KW", r"TRUE"),
    ("FALSE_KW", r"FALSE"),
    ("RESET_KW", r"RESET"),
    ("WAIT_KW", r"WAIT"),
    ("CALL_KW", r"CALL"),
    ("EXTERN_KW", r"EXTERN"),
    ("FROM_KW", r"FROM"),
    ("BEEP_KW", r"BEEP"),
    ("AMIIBO_KW", r"AMIIBO"),
    # Logical operators
    ("LOGIC_OP", r"or|and|not"),
    # Gamepad keys (case-insensitive). DIRECTION_KEY before BUTTON_KEY so longer
    # overlapping direction matches win. UP/DOWN/LEFT/RIGHT only in DIRECTION_KEY.
    ("STICK_KEY", r"LS|RS"),
    ("DIRECTION_KEY", r"DOWNLEFT|DOWNRIGHT|UPLEFT|UPRIGHT|UP|DOWN|LEFT|RIGHT"),
    ("BUTTON_KEY", r"LCLICK|RCLICK|CAPTURE|MINUS|HOME|PLUS|ZL|ZR|L|R|A|B|X|Y"),
    # String/number literals
    ("STRING", r'"(?:[^"\\]|\\.)*"|\'(?:[^\'\\]|\\.)*\''),
    ("NUMBER", r"-?\d+\.\d+"),
    ("INT", r"-?\d+"),
    # Variables and constants
    ("VAR", r"\$\$?[a-zA-Z0-9_一-龥][a-zA-Z0-9_一-龥]*"),
    ("CONST", r"_[a-zA-Z_一-龥][a-zA-Z0-9_一-龥]*"),
    ("EX_VAR", r"@[a-zA-Z_一-龥][a-zA-Z0-9_一-龥]*"),
    # Identifiers
    ("IDENT", r"[a-zA-Z_一-龥][a-zA-Z0-9_一-龥！]*"),
    # Assignment operators (before OP so <<=, +=, etc. win)
    ("ASSIGN_OP", r"<<=|>>=|\+=|-=|\*=|/=|\\=|%=|&=|\|=|\^=|="),
    # Comparison / arithmetic / bitwise operators
    ("OP", r"==|!=|<=|>=|<<|>>|\+|-|\*|/|\\|%|&|\||\^|~|<|>"),
    # Punctuation
    ("PAREN", r"[()]"),
    ("BRACKET", r"[\[\]]"),
    ("COMMA", r","),
    ("COLON", r":"),
    # Comments
    ("COMMENT", r"#[^\r\n]*"),
]

_COMPILED_PATTERNS = []
for _name, _pat in _TOKEN_PATTERNS:
    # Gamepad keys are case-insensitive; keywords are case-sensitive (matching Lark grammar)
    _flags = re.IGNORECASE if _name.endswith("_KEY") else 0
    _COMPILED_PATTERNS.append((_name, re.compile(_pat, _flags)))


def _tokenize(source: str) -> List[dict]:
    """Hand-written maximal-munch lexer for ECS syntax highlighting."""
    tokens: List[dict] = []
    pos = 0
    line = 1
    col = 1
    length = len(source)

    while pos < length:
        ch = source[pos]
        # Inline whitespace is ignored
        if ch in " \t":
            pos += 1
            col += 1
            continue
        # Newlines
        if ch == "\n":
            tokens.append({"type": "_NL", "value": "\n", "line": line, "column": col})
            pos += 1
            line += 1
            col = 1
            continue
        if ch == "\r":
            if pos + 1 < length and source[pos + 1] == "\n":
                tokens.append(
                    {"type": "_NL", "value": "\r\n", "line": line, "column": col}
                )
                pos += 2
            else:
                tokens.append(
                    {"type": "_NL", "value": "\r", "line": line, "column": col}
                )
                pos += 1
            line += 1
            col = 1
            continue

        best_name = None
        best_match = None
        best_len = 0
        for name, regex in _COMPILED_PATTERNS:
            m = regex.match(source, pos)
            if m:
                mlen = m.end() - m.start()
                if mlen > best_len:
                    best_len = mlen
                    best_match = m
                    best_name = name

        if best_match:
            tokens.append(
                {
                    "type": best_name,
                    "value": best_match.group(),
                    "line": line,
                    "column": col,
                }
            )
            pos += best_len
            col += best_len
        else:
            # Unknown character – emit as-is so text isn't lost
            tokens.append(
                {"type": "UNKNOWN", "value": ch, "line": line, "column": col}
            )
            pos += 1
            col += 1

    return tokens


_BUILTIN_FUNCS = {
    "WAIT", "PRINT", "ALERT", "RAND", "TIME", "LEN", "APPEND"
}

_VALID_BUTTONS = {
    "LCLICK", "RCLICK", "CAPTURE", "MINUS", "HOME", "PLUS",
    "ZL", "ZR", "L", "R", "A", "B", "X", "Y",
    "UP", "DOWN", "LEFT", "RIGHT",
}

_VALID_STICKS = {"LS", "RS"}
_VALID_DIRECTIONS = {
    "DOWNLEFT", "DOWNRIGHT", "UPLEFT", "UPRIGHT",
    "UP", "DOWN", "LEFT", "RIGHT", "RESET",
}


def _validate_program(program: ast.Program) -> List[str]:
    """Run semantic validation on a successfully parsed program."""
    errors: List[str] = []
    func_names: set[str] = set()

    def _collect_funcs(stmts: List[ast.Stmt]) -> None:
        for stmt in stmts:
            if isinstance(stmt, ast.FuncDeclStmt):
                func_names.add(stmt.name.upper())
            elif isinstance(stmt, ast.ExternFuncStmt):
                func_names.add(stmt.name.upper())
            elif isinstance(stmt, ast.IfStmt):
                _collect_funcs(stmt.body)
                for _, elif_body in stmt.elifs:
                    _collect_funcs(elif_body)
                if stmt.else_body:
                    _collect_funcs(stmt.else_body)
            elif isinstance(stmt, ast.ForStmt):
                _collect_funcs(stmt.body)
            elif isinstance(stmt, ast.WhileStmt):
                _collect_funcs(stmt.body)

    _collect_funcs(program.statements)

    def _check_stmts(stmts: List[ast.Stmt]) -> None:
        for stmt in stmts:
            if isinstance(stmt, ast.CallStmt):
                name_upper = stmt.name.upper()
                if name_upper not in _BUILTIN_FUNCS and name_upper not in func_names:
                    errors.append(f"未知的函数或按键 '{stmt.name}' (line {stmt.loc.line})")
            elif isinstance(stmt, ast.KeyPressStmt):
                if stmt.key.upper() not in _VALID_BUTTONS:
                    errors.append(f"无效的按键 '{stmt.key}' (line {stmt.loc.line})")
                if isinstance(stmt.duration, ast.VariableExpr) and stmt.duration.read_only:
                    errors.append(f"未知的标识符 '{stmt.duration.name}' (line {stmt.duration.loc.line})")
            elif isinstance(stmt, ast.KeyActStmt):
                if stmt.key.upper() not in _VALID_BUTTONS:
                    errors.append(f"无效的按键 '{stmt.key}' (line {stmt.loc.line})")
            elif isinstance(stmt, ast.StickActStmt):
                if stmt.key.upper() not in _VALID_STICKS:
                    errors.append(f"无效的摇杆 '{stmt.key}' (line {stmt.loc.line})")
                if stmt.direction.upper() not in _VALID_DIRECTIONS:
                    errors.append(f"无效的方向 '{stmt.direction}' (line {stmt.loc.line})")
            elif isinstance(stmt, ast.StickPressStmt):
                if stmt.key.upper() not in _VALID_STICKS:
                    errors.append(f"无效的摇杆 '{stmt.key}' (line {stmt.loc.line})")
                if stmt.direction.upper() not in _VALID_DIRECTIONS:
                    errors.append(f"无效的方向 '{stmt.direction}' (line {stmt.loc.line})")
                if isinstance(stmt.duration, ast.VariableExpr) and stmt.duration.read_only:
                    errors.append(f"未知的标识符 '{stmt.duration.name}' (line {stmt.duration.loc.line})")
            elif isinstance(stmt, ast.IfStmt):
                _check_stmts(stmt.body)
                for _, elif_body in stmt.elifs:
                    _check_stmts(elif_body)
                if stmt.else_body:
                    _check_stmts(stmt.else_body)
            elif isinstance(stmt, ast.ForStmt):
                _check_stmts(stmt.body)
            elif isinstance(stmt, ast.WhileStmt):
                _check_stmts(stmt.body)
            elif isinstance(stmt, ast.FuncDeclStmt):
                _check_stmts(stmt.body)

    _check_stmts(program.statements)
    return errors


# ---------------------------------------------------------------------------
# Friendly error formatting
# ---------------------------------------------------------------------------

_ASSIGN_TOKENS = {"EQUAL", "ASSIGN_OP"}
_EXPR_TOKENS = {
    "VAR", "IDENT", "INT", "NUMBER", "STRING",
    "TRUE_KW", "FALSE_KW", "MINUS", "NOT",
    "TILDE", "LPAR", "LSQB", "CONST", "EX_VAR",
}


def _format_lark_error(e: Exception) -> str:
    """Convert Lark exception to a user-friendly Chinese message."""
    from lark.exceptions import UnexpectedCharacters, UnexpectedToken

    if isinstance(e, UnexpectedCharacters):
        char = e.char
        line = e.line
        allowed = e.allowed

        if char == "\n":
            if allowed & _ASSIGN_TOKENS and not (allowed & _EXPR_TOKENS):
                return f"语句不完整，期望赋值操作符 (line {line})"
            if allowed & _EXPR_TOKENS:
                return f"语句不完整，期望表达式 (line {line})"
            if allowed <= {"_NL", "COMMENT"}:
                return f"此处不应有内容 (line {line})"
            return f"语句不完整 (line {line})"

        if char.strip():
            return f"无效的字符 '{char}' (line {line})"
        return f"意外的空白字符 (line {line})"

    if isinstance(e, UnexpectedToken):
        token = e.token
        line = getattr(token, "line", 0)
        expected = set(e.expected) if hasattr(e, "expected") else set()
        if expected & _EXPR_TOKENS:
            return f"意外的符号，期望表达式 (line {line})"
        return f"意外的符号 (line {line})"

    return str(e)


_FUNC_CALL_LINE = re.compile(
    r'^(\s*)(PRINT|ALERT)\s+(.*)'
)


_EXPR_PATTERNS = [
    re.compile(r'^-?\d+(\.\d+)?$'),          # number
    re.compile(r'^".*"$'),                     # double-quoted string
    re.compile(r"^'.*'$"),                     # single-quoted string
    re.compile(r'^\$\$?[a-zA-Z0-9_一-龥]+$'), # variable
    re.compile(r'^_[a-zA-Z0-9_一-龥]+$'),      # constant
    re.compile(r'^@[a-zA-Z0-9_一-龥]+$'),      # external var
    re.compile(r'^(TRUE|FALSE)$', re.I),       # boolean
    re.compile(r'^\(.*\)$'),                   # parenthesized
]


def _is_valid_expr(text: str) -> bool:
    """Check if text is a valid expression token (not bare template text)."""
    for pat in _EXPR_PATTERNS:
        if pat.match(text):
            return True
    return False


def _preprocess_func_calls(source: str) -> str:
    """Quote bare text in PRINT/ALERT template arguments so the Lark parser
    can handle them as expressions.

    PRINT text: & $var & text!  →  PRINT "text:" & $var & "text!"
    """
    lines = source.splitlines(keepends=True)
    result: list[str] = []
    for line in lines:
        content = line.rstrip("\r\n")
        newline = line[len(content):]  # preserve original line ending

        # Separate comment from content
        comment = ""
        hash_pos = content.find("#")
        if hash_pos >= 0:
            comment = content[hash_pos:]
            content = content[:hash_pos].rstrip()

        m = _FUNC_CALL_LINE.match(content)
        if m and not content.rstrip().endswith(":"):
            indent = m.group(1)
            func_name = m.group(2)
            args_text = m.group(3) or ""

            parts = args_text.split("&")
            quoted: list[str] = []
            for part in parts:
                stripped = part.strip()
                if not stripped:
                    continue
                if _is_valid_expr(stripped):
                    quoted.append(stripped)
                else:
                    escaped = stripped.replace("\\", "\\\\").replace('"', '\\"')
                    quoted.append(f'"{escaped}"')

            new_args = " & ".join(quoted)
            content = f"{indent}{func_name} {new_args}"

        result.append(f"{content}{comment}{newline}")

    return "".join(result)


_LARK = None


def _get_lark() -> Lark:
    """Return a cached Lark parser instance (module-level singleton)."""
    global _LARK
    if _LARK is None:
        grammar = _GRAMMAR_PATH.read_text(encoding="utf-8")
        _LARK = Lark(grammar, parser="earley", propagate_positions=True)
    return _LARK


class Parser:
    def __init__(self):
        self._lark = _get_lark()
        self._transformer = _StmtTransformer()
        self.errors: List[str] = []

    def tokenize(self, source: str) -> List[dict]:
        """Lex source into tokens for syntax highlighting.

        Returns a list of dicts with keys: type, value, line, column.
        """
        return _tokenize(source)

    def parse(self, source: str) -> ast.Program:
        self.errors.clear()
        preprocessed = _preprocess_func_calls(source)
        # Ensure the source ends with a newline (grammar requires _NL after every line)
        if not preprocessed.endswith("\n"):
            preprocessed += "\n"
        try:
            tree = self._lark.parse(preprocessed)
            return self._transformer.transform(tree)
        except Exception as e:
            friendly = _format_lark_error(e)
            self.errors.append(friendly)
            raise ParseError(friendly) from e
