"""EasyCon Script Language Server."""

from __future__ import annotations
import argparse
import asyncio
import logging
from dataclasses import dataclass, field
from typing import Optional

from lsprotocol import types
from pygls.lsp.server import LanguageServer

logger = logging.getLogger("ecs-lsp")
# Suppress harmless pygls warnings (e.g. cancelRequest for already-finished requests)
logging.getLogger("pygls").setLevel(logging.ERROR)

from easycon_grammar import ast
from easycon_grammar.compilation import Compilation
from easycon_script_lsp.features.completion import get_completions
from easycon_script_lsp.features.diagnostics import parse_error_to_diagnostic
from easycon_script_lsp.features.document_symbols import get_document_symbols
from easycon_script_lsp.features.hover import get_hover
from easycon_script_lsp.features.definition import get_definition
from easycon_script_lsp.features.formatting import format_document


@dataclass
class DocumentState:
    uri: str
    source: str
    program: Optional[ast.Program] = None
    errors: list[str] = field(default_factory=list)


class ECSServer(LanguageServer):
    def __init__(self):
        super().__init__("ecs-lsp", "0.1.0")
        self._documents: dict[str, DocumentState] = {}
        self._pending_updates: dict[str, asyncio.TimerHandle] = {}

    def get_document_state(self, uri: str) -> Optional[DocumentState]:
        return self._documents.get(uri)

    def set_document_state(self, uri: str, state: DocumentState) -> None:
        self._documents[uri] = state

    def remove_document_state(self, uri: str) -> None:
        self._documents.pop(uri, None)

    def _cancel_pending_update(self, uri: str) -> None:
        handle = self._pending_updates.pop(uri, None)
        if handle:
            handle.cancel()

    def _update_and_publish(self, uri: str, source: str) -> None:
        comp = Compilation(source)
        result = comp.compile()
        logger.info(f"Compiled {uri}, errors={len(result.errors)}")
        state = DocumentState(
            uri=uri,
            source=source,
            program=result.program,
            errors=result.errors,
        )
        self.set_document_state(uri, state)
        lines = source.splitlines()
        diagnostics = [parse_error_to_diagnostic(e, lines) for e in result.errors]
        logger.info(f"Publishing {len(diagnostics)} diagnostics")
        self.text_document_publish_diagnostics(
            types.PublishDiagnosticsParams(uri=uri, diagnostics=diagnostics)
        )

    def _debounced_update_and_publish(self, uri: str, source: str, delay: float = 0.3) -> None:
        self._cancel_pending_update(uri)
        loop = asyncio.get_event_loop()

        def callback():
            self._pending_updates.pop(uri, None)
            self._update_and_publish(uri, source)

        self._pending_updates[uri] = loop.call_later(delay, callback)


ecs_server = ECSServer()


# --------------------------------------------------------------------------- #
#  Lifecycle
# --------------------------------------------------------------------------- #


@ecs_server.feature(types.INITIALIZE)
def on_initialize(ls: ECSServer, params: types.InitializeParams) -> types.InitializeResult:
    return types.InitializeResult(
        capabilities=types.ServerCapabilities(
            text_document_sync=types.TextDocumentSyncOptions(
                open_close=True,
                change=types.TextDocumentSyncKind.Full,
            ),
            document_symbol_provider=True,
            completion_provider=types.CompletionOptions(
                trigger_characters=["$", "_", "@", "("],
            ),
            hover_provider=True,
            definition_provider=True,
            document_formatting_provider=True,
        )
    )


# --------------------------------------------------------------------------- #
#  Document sync
# --------------------------------------------------------------------------- #


@ecs_server.feature(types.TEXT_DOCUMENT_DID_OPEN)
def on_did_open(ls: ECSServer, params: types.DidOpenTextDocumentParams) -> None:
    doc = params.text_document
    ls._update_and_publish(doc.uri, doc.text)


@ecs_server.feature(types.TEXT_DOCUMENT_DID_CHANGE)
def on_did_change(ls: ECSServer, params: types.DidChangeTextDocumentParams) -> None:
    doc = ls.workspace.get_text_document(params.text_document.uri)
    ls._debounced_update_and_publish(doc.uri, doc.source)


@ecs_server.feature(types.TEXT_DOCUMENT_DID_CLOSE)
def on_did_close(ls: ECSServer, params: types.DidCloseTextDocumentParams) -> None:
    ls._cancel_pending_update(params.text_document.uri)
    ls.remove_document_state(params.text_document.uri)
    ls.text_document_publish_diagnostics(
        types.PublishDiagnosticsParams(uri=params.text_document.uri, diagnostics=[])
    )


# --------------------------------------------------------------------------- #
#  Features
# --------------------------------------------------------------------------- #


@ecs_server.feature(types.TEXT_DOCUMENT_DOCUMENT_SYMBOL)
def on_document_symbol(
    ls: ECSServer, params: types.DocumentSymbolParams
) -> list[types.DocumentSymbol] | None:
    state = ls.get_document_state(params.text_document.uri)
    if not state or not state.program:
        return []
    return get_document_symbols(state.program)


@ecs_server.feature(
    types.TEXT_DOCUMENT_COMPLETION,
    types.CompletionOptions(trigger_characters=["$", "_", "@", "("]),
)
def on_completion(
    ls: ECSServer, params: types.CompletionParams
) -> types.CompletionList | None:
    state = ls.get_document_state(params.text_document.uri)
    program = state.program if state else None
    return get_completions(program)


@ecs_server.feature(types.TEXT_DOCUMENT_HOVER)
def on_hover(ls: ECSServer, params: types.HoverParams) -> types.Hover | None:
    doc = ls.workspace.get_text_document(params.text_document.uri)
    word = doc.word_at_position(params.position)
    if not word:
        return None
    state = ls.get_document_state(params.text_document.uri)
    return get_hover(state.program if state else None, word, params.position)


@ecs_server.feature(types.TEXT_DOCUMENT_DEFINITION)
def on_definition(
    ls: ECSServer, params: types.DefinitionParams
) -> types.Location | None:
    state = ls.get_document_state(params.text_document.uri)
    if not state or not state.program:
        return None
    return get_definition(state.program, state.source, params.position)


@ecs_server.feature(types.TEXT_DOCUMENT_FORMATTING)
def on_formatting(
    ls: ECSServer, params: types.DocumentFormattingParams
) -> list[types.TextEdit] | None:
    state = ls.get_document_state(params.text_document.uri)
    if not state or not state.program:
        return None
    doc = ls.workspace.get_text_document(params.text_document.uri)
    source_lines = doc.source.splitlines()
    last_line = max(0, len(source_lines) - 1)
    last_char = len(source_lines[last_line]) if source_lines else 0
    formatted = format_document(state.program)
    return [
        types.TextEdit(
            range=types.Range(
                start=types.Position(line=0, character=0),
                end=types.Position(line=last_line, character=last_char),
            ),
            new_text=formatted,
        )
    ]


# --------------------------------------------------------------------------- #
#  CLI entry point
# --------------------------------------------------------------------------- #


def main() -> None:
    parser = argparse.ArgumentParser(description="EasyCon Script Language Server")
    parser.add_argument("--stdio", action="store_true", help="Use stdio (default)")
    parser.add_argument("--tcp", action="store_true", help="Use TCP instead of stdio")
    parser.add_argument("--host", default="127.0.0.1", help="TCP host")
    parser.add_argument("--port", type=int, default=2087, help="TCP port")
    args = parser.parse_args()

    if args.tcp:
        ecs_server.start_tcp(args.host, args.port)
    else:
        ecs_server.start_io()
