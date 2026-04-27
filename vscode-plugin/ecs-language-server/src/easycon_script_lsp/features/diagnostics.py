"""Diagnostics support for ECS LSP."""

from __future__ import annotations
import re
from typing import Optional

from lsprotocol.types import Diagnostic, DiagnosticSeverity, Position, PublishDiagnosticsParams, Range
from pygls.lsp.server import LanguageServer
from pygls.workspace import TextDocument

from easycon_grammar.compilation import Compilation

# Match both:
#   "(line 5)"          -- _build_blocks ParseError
#   "at line 5 col 3"   -- Lark lexer/parser errors
_ERROR_LINE_RE = re.compile(r"(?:\(line\s+(\d+)\)|at\s+line\s+(\d+))", re.IGNORECASE)


def _extract_line(error_msg: str) -> int:
    match = _ERROR_LINE_RE.search(error_msg)
    if match:
        line_str = match.group(1) or match.group(2)
        return int(line_str) - 1  # 0-based
    return 0


def parse_error_to_diagnostic(error_msg: str, source_lines: Optional[list[str]] = None) -> Diagnostic:
    """Convert a Compilation error string to an LSP Diagnostic."""
    line = _extract_line(error_msg)
    line_length = len(source_lines[line]) if source_lines and line < len(source_lines) else 0
    end_char = max(line_length, 1)
    return Diagnostic(
        range=Range(
            start=Position(line=line, character=0),
            end=Position(line=line, character=end_char),
        ),
        message=error_msg,
        severity=DiagnosticSeverity.Error,
        source="ecs-lsp",
    )


def publish_diagnostics(ls: LanguageServer, text_document: TextDocument) -> list[str]:
    """Parse document and publish diagnostics.  Returns any error strings."""
    source = text_document.source
    comp = Compilation(source)
    result = comp.compile()

    lines = source.splitlines()
    diagnostics = [parse_error_to_diagnostic(e, lines) for e in result.errors]
    ls.text_document_publish_diagnostics(
        PublishDiagnosticsParams(uri=text_document.uri, diagnostics=diagnostics)
    )
    return result.errors
