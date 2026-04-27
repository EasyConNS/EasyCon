"""Document formatting support for ECS LSP."""

from __future__ import annotations

from lsprotocol.types import TextEdit

from easycon_grammar import ast


def format_document(program: ast.Program) -> str:
    """Format entire document, returning the formatted source."""
    lines: list[str] = []
    for stmt in program.statements:
        _format_stmt(stmt, 0, lines)
    # Strip trailing blank lines, ensure single trailing newline
    while lines and lines[-1] == "":
        lines.pop()
    return "\n".join(lines) + "\n" if lines else ""


def _format_stmt(stmt: ast.Stmt, indent: int, lines: list[str]) -> None:
    prefix = " " * indent

    if isinstance(stmt, ast.EmptyStmt):
        if stmt.comment:
            lines.append(f"{prefix}{stmt.comment}")
        else:
            lines.append("")
    elif isinstance(stmt, ast.IfStmt):
        cond = _expr_to_string(stmt.condition)
        header = _append_comment(f"{prefix}IF {cond}", stmt.comment)
        lines.append(header)
        for s in stmt.body:
            _format_stmt(s, indent + 4, lines)
        for elif_cond, elif_body in stmt.elifs:
            lines.append(f"{prefix}ELIF {_expr_to_string(elif_cond)}")
            for s in elif_body:
                _format_stmt(s, indent + 4, lines)
        if stmt.else_body:
            lines.append(f"{prefix}ELSE")
            for s in stmt.else_body:
                _format_stmt(s, indent + 4, lines)
        lines.append(f"{prefix}ENDIF")
    elif isinstance(stmt, ast.ForStmt):
        if stmt.infinite:
            header = f"{prefix}FOR"
        elif stmt.iter_name is not None:
            lower = _expr_to_string(stmt.lower) if stmt.lower else ""
            upper = _expr_to_string(stmt.upper)
            header = f"{prefix}FOR {stmt.iter_name} = {lower} TO {upper}"
            if stmt.step is not None:
                step = _expr_to_string(stmt.step)
                header += f" STEP {step}"
        else:
            upper = _expr_to_string(stmt.upper)
            header = f"{prefix}FOR {upper}"
            if stmt.step is not None:
                step = _expr_to_string(stmt.step)
                header += f" STEP {step}"
        header = _append_comment(header, stmt.comment)
        lines.append(header)
        for s in stmt.body:
            _format_stmt(s, indent + 4, lines)
        lines.append(f"{prefix}NEXT")
    elif isinstance(stmt, ast.WhileStmt):
        cond = _expr_to_string(stmt.condition)
        header = _append_comment(f"{prefix}WHILE {cond}", stmt.comment)
        lines.append(header)
        for s in stmt.body:
            _format_stmt(s, indent + 4, lines)
        lines.append(f"{prefix}END")
    elif isinstance(stmt, ast.FuncDeclStmt):
        if stmt.params:
            params = ", ".join(stmt.params)
            header = f"{prefix}FUNC {stmt.name}({params})"
        else:
            header = f"{prefix}FUNC {stmt.name}"
        header = _append_comment(header, stmt.comment)
        lines.append(header)
        for s in stmt.body:
            _format_stmt(s, indent + 4, lines)
        lines.append(f"{prefix}ENDFUNC")
    elif isinstance(stmt, ast.ExternFuncStmt):
        params_str = ", ".join(
            f"{p.name}:{p.type}" if p.type else p.name
            for p in stmt.params
        )
        line = f'{prefix}EXTERN FUNC {stmt.name}({params_str}):{stmt.return_type} FROM "{stmt.library}"'
        lines.append(_append_comment(line, stmt.comment))
    elif isinstance(stmt, ast.ImportStmt):
        lines.append(_append_comment(f'{prefix}IMPORT "{stmt.module}"', stmt.comment))
    elif isinstance(stmt, ast.ConstantDeclStmt):
        val = _expr_to_string(stmt.value)
        lines.append(_append_comment(f"{prefix}{stmt.name} = {val}", stmt.comment))
    elif isinstance(stmt, ast.AssignmentStmt):
        target = _expr_to_string(stmt.target)
        val = _expr_to_string(stmt.value)
        lines.append(_append_comment(f"{prefix}{target} {stmt.op} {val}", stmt.comment))
    elif isinstance(stmt, ast.ReturnStmt):
        if stmt.value is not None:
            lines.append(_append_comment(f"{prefix}RETURN {_expr_to_string(stmt.value)}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}RETURN", stmt.comment))
    elif isinstance(stmt, ast.CallStmt):
        if stmt.args:
            args = " ".join(_expr_to_string(arg) for arg in stmt.args)
            lines.append(_append_comment(f"{prefix}{stmt.name} {args}", stmt.comment))
        elif stmt.is_call:
            lines.append(_append_comment(f"{prefix}CALL {stmt.name}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}{stmt.name}", stmt.comment))
    elif isinstance(stmt, ast.WaitStmt):
        dur = _expr_to_string(stmt.duration)
        if stmt.omitted:
            lines.append(_append_comment(f"{prefix}{dur}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}WAIT {dur}", stmt.comment))
    elif isinstance(stmt, ast.KeyPressStmt):
        if stmt.duration is not None:
            lines.append(_append_comment(f"{prefix}{stmt.key} {_expr_to_string(stmt.duration)}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}{stmt.key}", stmt.comment))
    elif isinstance(stmt, ast.KeyActStmt):
        action = "UP" if stmt.up else "DOWN"
        lines.append(_append_comment(f"{prefix}{stmt.key} {action}", stmt.comment))
    elif isinstance(stmt, ast.StickPressStmt):
        if stmt.duration is not None:
            dur = _expr_to_string(stmt.duration)
            lines.append(_append_comment(f"{prefix}{stmt.key} {stmt.direction},{dur}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}{stmt.key} {stmt.direction}", stmt.comment))
    elif isinstance(stmt, ast.StickActStmt):
        lines.append(_append_comment(f"{prefix}{stmt.key} {stmt.direction}", stmt.comment))
    elif isinstance(stmt, ast.BreakStmt):
        if stmt.level > 1:
            lines.append(_append_comment(f"{prefix}BREAK {stmt.level}", stmt.comment))
        else:
            lines.append(_append_comment(f"{prefix}BREAK", stmt.comment))
    elif isinstance(stmt, ast.ContinueStmt):
        lines.append(_append_comment(f"{prefix}CONTINUE", stmt.comment))
    elif isinstance(stmt, ast.BeepStmt):
        freq = _expr_to_string(stmt.frequency)
        dur = _expr_to_string(stmt.duration)
        lines.append(_append_comment(f"{prefix}BEEP {freq}, {dur}", stmt.comment))
    elif isinstance(stmt, ast.AmiiboStmt):
        idx = _expr_to_string(stmt.index)
        lines.append(_append_comment(f"{prefix}AMIIBO {idx}", stmt.comment))


def _expr_to_string(expr: ast.Expr) -> str:
    if isinstance(expr, ast.LiteralExpr):
        v = expr.value
        if isinstance(v, bool):
            return "TRUE" if v else "FALSE"
        if isinstance(v, str):
            escaped = v.replace("\\", "\\\\").replace('"', '\\"')
            return f'"{escaped}"'
        return str(v)
    elif isinstance(expr, ast.VariableExpr):
        return expr.name
    elif isinstance(expr, ast.ConstVarExpr):
        return expr.name
    elif isinstance(expr, ast.ExtVarExpr):
        return f"@{expr.name}"
    elif isinstance(expr, ast.BinaryExpr):
        left = _expr_to_string(expr.left)
        right = _expr_to_string(expr.right)
        return f"{left} {expr.op} {right}"
    elif isinstance(expr, ast.UnaryExpr):
        operand = _expr_to_string(expr.operand)
        if expr.op in ("-", "~"):
            return f"{expr.op}{operand}"
        return f"{expr.op} {operand}"
    elif isinstance(expr, ast.ParenExpr):
        return f"({_expr_to_string(expr.inner)})"
    elif isinstance(expr, ast.IndexDefExpr):
        items = ", ".join(_expr_to_string(item) for item in expr.items)
        return f"[{items}]"
    elif isinstance(expr, ast.IndexExpr):
        return f"{expr.name}[{_expr_to_string(expr.index)}]"
    elif isinstance(expr, ast.SliceExpr):
        start = _expr_to_string(expr.start) if expr.start else ""
        end = _expr_to_string(expr.end)
        return f"{expr.name}[{start}:{end}]"
    elif isinstance(expr, ast.CallExpr):
        args = ", ".join(_expr_to_string(arg) for arg in expr.args)
        return f"{expr.name}({args})"
    else:
        return ""


def _append_comment(text: str, comment: str) -> str:
    if comment:
        return f"{text} {comment}"
    return text
