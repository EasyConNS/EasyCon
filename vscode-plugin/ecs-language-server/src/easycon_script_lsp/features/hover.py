"""Hover support for ECS LSP."""

from __future__ import annotations

from lsprotocol.types import Hover, MarkupContent, MarkupKind, Position

from easycon_grammar import ast
from easycon_script_lsp.constants import (
    ECS_KEYWORDS,
    ECS_BUILTIN_FUNCTIONS,
    ECS_BUTTON_KEYS,
    ECS_STICK_KEYS,
    ECS_DIRECTION_KEYS,
    ECS_KEY_MODS,
    BUILTIN_DOCS,
    KEYWORD_DOCS,
)
from easycon_script_lsp.utils.ast_walker import ASTWalker

_KEY_MOD_LABEL = {"DOWN": "按下", "UP": "松开"}


def get_hover(
    program: ast.Program | None,
    word: str,
    position: Position | None = None,
) -> Hover | None:
    """Return hover information for the given word.

    When position is provided and program is available, uses AST for
    precise identification (e.g. button key vs key_mod vs direction).
    """

    # AST position-based lookup
    if program is not None and position is not None:
        pos_1based = (position.line + 1, position.character + 1)
        result = _hover_from_ast(program, pos_1based)
        if result is not None:
            return result

    # Fallback: word-based matching
    return _hover_from_word(program, word)


def _hover_from_ast(
    program: ast.Program, pos: tuple[int, int]
) -> Hover | None:
    """Return hover by finding the AST node at the given 1-based position."""
    node = _find_node_at(program, pos)
    if node is None:
        return None

    stmt = node["stmt"]
    role = node["role"]
    return _hover_for_role(stmt, role)


def _find_node_at(
    program: ast.Program, pos: tuple[int, int]
) -> dict | None:
    """Find the AST statement and role at the given 1-based (line, col)."""
    line, col = pos

    def search(stmts: list[ast.Stmt]) -> dict | None:
        for stmt in stmts:
            result = _match_stmt(stmt, line, col)
            if result is not None:
                return result
            # Recurse into blocks
            for child_stmts in _child_stmts(stmt):
                result = search(child_stmts)
                if result is not None:
                    return result
        return None

    return search(program.statements)


def _match_stmt(stmt: ast.Stmt, line: int, col: int) -> dict | None:
    """Check if position falls within a statement, returning role info."""
    loc = stmt.loc
    if loc.line != line:
        return None

    if isinstance(stmt, ast.WaitStmt):
        if stmt.omitted:
            return {"stmt": stmt, "role": "wait_duration"}
        # WAIT keyword occupies columns [loc.col, loc.col + 4)
        if col < loc.column + 4:
            return {"stmt": stmt, "role": "wait_keyword"}
        return {"stmt": stmt, "role": "wait_duration"}

    if isinstance(stmt, ast.KeyPressStmt):
        key_end = loc.column + len(stmt.key)
        if loc.column <= col < key_end:
            return {"stmt": stmt, "role": "key"}
        if stmt.duration is not None:
            return {"stmt": stmt, "role": "duration"}
        return None

    if isinstance(stmt, ast.KeyActStmt):
        key_end = loc.column + len(stmt.key)
        if loc.column <= col < key_end:
            return {"stmt": stmt, "role": "key"}
        mod_start = loc.column + len(stmt.key) + 1
        mod_end = mod_start + (3 if stmt.up else 4)
        if mod_start <= col < mod_end:
            return {"stmt": stmt, "role": "key_mod"}
        return None

    if isinstance(stmt, (ast.StickActStmt, ast.StickPressStmt)):
        key_end = loc.column + len(stmt.key)
        if loc.column <= col < key_end:
            return {"stmt": stmt, "role": "stick"}
        dir_start = loc.column + len(stmt.key) + 1
        dir_end = dir_start + len(stmt.direction)
        if dir_start <= col < dir_end:
            return {"stmt": stmt, "role": "direction"}
        if isinstance(stmt, ast.StickPressStmt) and stmt.duration is not None:
            return {"stmt": stmt, "role": "duration"}
        return None

    if isinstance(stmt, ast.ExternFuncStmt):
        # EXTERN keyword at column for 6 chars
        if col < loc.column + 6:
            return {"stmt": stmt, "role": "extern_keyword"}
        # FUNC keyword after EXTERN (7 chars from start)
        func_col = loc.column + 7
        if func_col <= col < func_col + 4:
            return {"stmt": stmt, "role": "func_keyword"}
        # Function name after "EXTERN FUNC " (12 chars from start)
        name_col = loc.column + 12
        if name_col <= col < name_col + len(stmt.name):
            return {"stmt": stmt, "role": "extern_func_name"}
        return {"stmt": stmt, "role": "extern_func_decl"}

    return None


def _hover_for_role(stmt: ast.Stmt, role: str) -> Hover | None:
    if isinstance(stmt, ast.WaitStmt) and role == "wait_keyword":
        doc = KEYWORD_DOCS.get("WAIT", "延时等待")
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`WAIT`** — {doc}",
            )
        )

    if isinstance(stmt, ast.WaitStmt) and role == "wait_duration":
        ms = _extract_number(stmt.duration)
        return _build_wait_hover(ms, stmt.omitted)

    if isinstance(stmt, ast.KeyPressStmt) and role == "key":
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{stmt.key}`** — Nintendo Switch 按键",
            )
        )
    if isinstance(stmt, ast.KeyPressStmt) and role == "duration":
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"按键 `{stmt.key}` 的持续时间（毫秒）",
            )
        )
    if isinstance(stmt, ast.KeyActStmt) and role == "key":
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{stmt.key}`** — Nintendo Switch 按键",
            )
        )
    if isinstance(stmt, ast.KeyActStmt) and role == "key_mod":
        action = "UP" if stmt.up else "DOWN"
        label = _KEY_MOD_LABEL.get(action, action)
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{action}`** — 按键动作（{label}）",
            )
        )
    if isinstance(stmt, (ast.StickActStmt, ast.StickPressStmt)) and role == "stick":
        label = "左摇杆" if stmt.key.upper() == "LS" else "右摇杆"
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{stmt.key}`** — {label}（Left Stick / Right Stick）",
            )
        )
    if isinstance(stmt, (ast.StickActStmt, ast.StickPressStmt)) and role == "direction":
        if stmt.direction == "RESET":
            return Hover(
                contents=MarkupContent(
                    kind=MarkupKind.Markdown,
                    value=f"**`RESET`** — 摇杆复位：释放摇杆，恢复到自然居中状态",
                )
            )
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{stmt.direction}`** — 摇杆方向",
            )
        )
    if isinstance(stmt, ast.StickPressStmt) and role == "duration":
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"摇杆 `{stmt.key}` 向 `{stmt.direction}` 的持续时间（毫秒）",
            )
        )
    if isinstance(stmt, ast.ExternFuncStmt):
        return _build_extern_func_hover(stmt)
    return None


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


def _hover_from_word(program: ast.Program | None, word: str) -> Hover | None:
    """Fallback: word-based hover matching."""
    word_upper = word.upper()

    # Keyword — use detailed doc when available
    if word_upper in ECS_KEYWORDS:
        doc = KEYWORD_DOCS.get(word_upper, "")
        if doc:
            return Hover(
                contents=MarkupContent(
                    kind=MarkupKind.Markdown,
                    value=f"**`{word}`** — ECS 关键字\n\n{doc}",
                )
            )
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — ECS 脚本关键字",
            )
        )

    # Built-in function
    if word_upper in ECS_BUILTIN_FUNCTIONS:
        doc = BUILTIN_DOCS.get(word_upper, "")
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — 内置功能\n\n{doc}",
            )
        )

    # Button key
    if word_upper in ECS_BUTTON_KEYS:
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — Nintendo Switch 按键",
            )
        )

    # Stick key
    if word_upper in ECS_STICK_KEYS:
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — 摇杆（Left Stick / Right Stick）",
            )
        )

    # Direction key
    if word_upper in ECS_DIRECTION_KEYS:
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — 摇杆方向",
            )
        )

    # Key mod
    if word_upper in ECS_KEY_MODS:
        return Hover(
            contents=MarkupContent(
                kind=MarkupKind.Markdown,
                value=f"**`{word}`** — 按键动作（按下/松开）",
            )
        )

    # Declared symbols
    if program is not None:
        walker = ASTWalker(program)
        for sym in walker.iter_symbols():
            if sym.name == word:
                kind_label = _kind_label(sym.kind)
                return Hover(
                    contents=MarkupContent(
                        kind=MarkupKind.Markdown,
                        value=f"**`{word}`** — {kind_label}",
                    )
                )

    return None


def _kind_label(kind: str) -> str:
    return {
        "constant": "常量",
        "variable": "变量",
        "function": "函数",
        "parameter": "参数",
    }.get(kind, kind)


def _extract_number(expr: ast.Expr) -> int | None:
    """Extract integer value from a simple LiteralExpr, or None."""
    if isinstance(expr, ast.LiteralExpr) and isinstance(expr.value, (int, float)):
        return int(expr.value)
    return None


def _build_extern_func_hover(stmt: ast.ExternFuncStmt) -> Hover:
    """Build hover content for an EXTERN FUNC declaration."""
    params_str = ", ".join(
        f"{p.name}:{p.type}" if p.type else p.name
        for p in stmt.params
    )
    signature = f'EXTERN FUNC {stmt.name}({params_str}):{stmt.return_type} FROM "{stmt.library}"'
    return Hover(
        contents=MarkupContent(
            kind=MarkupKind.Markdown,
            value=(
                f"**外部函数声明 (FFI)**\n\n"
                f"```ecs\n{signature}\n```\n\n"
                f"- **返回类型:** `{stmt.return_type}`\n"
                f"- **来源库:** `{stmt.library}`\n\n"
                f"> 调用原生函数存在崩溃风险，请确保参数正确。"
            ),
        )
    )


def _build_wait_hover(ms: int | None, omitted: bool) -> Hover:
    """Build hover for a wait duration."""
    if ms is not None:
        s = ms / 1000.0
        value = f"**延时等待** — 脚本在此停顿 **{ms} 毫秒**（{s:.1f} 秒）后继续执行下一步"
        if omitted:
            value += (
                f"\n\n> 💡 提示：单独写数字是 `WAIT {ms}` 的简写形式"
            )
    else:
        value = "**延时等待** — 脚本在此停顿一段时间后继续执行"
        if omitted:
            value += "\n\n> 💡 提示：单独写数字是 `WAIT` 的简写形式"
    return Hover(
        contents=MarkupContent(
            kind=MarkupKind.Markdown,
            value=value,
        )
    )
