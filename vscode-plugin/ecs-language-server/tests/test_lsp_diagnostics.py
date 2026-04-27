"""LSP diagnostics tests."""

from __future__ import annotations
from lsprotocol.types import DiagnosticSeverity
from easycon_script_lsp.features.diagnostics import parse_error_to_diagnostic


def test_extract_line_from_blocks_format():
    diag = parse_error_to_diagnostic("语句块没有正确结束 (line 5)")
    assert diag.range.start.line == 4  # 0-based
    assert diag.severity == DiagnosticSeverity.Error
    assert diag.source == "ecs-lsp"


def test_extract_line_from_lark_format():
    diag = parse_error_to_diagnostic("Unexpected token at line 10 col 3")
    assert diag.range.start.line == 9  # 0-based


def test_diagnostic_message_preserved():
    msg = "导入只能在脚本开头 (line 3)"
    diag = parse_error_to_diagnostic(msg)
    assert diag.message == msg


def test_diagnostic_with_source_lines():
    diag = parse_error_to_diagnostic("error (line 1)", source_lines=["A 100", "B 200"])
    assert diag.range.start.line == 0
    assert diag.range.start.character == 0
    assert diag.range.end.character > 0


def test_diagnostic_no_line_info():
    diag = parse_error_to_diagnostic("Some generic error")
    assert diag.range.start.line == 0
