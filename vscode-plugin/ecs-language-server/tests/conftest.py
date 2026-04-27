"""Shared fixtures and helpers for ECS language server tests."""

from __future__ import annotations
import pytest
from easycon_grammar.parser import Parser
from easycon_grammar.compilation import Compilation


@pytest.fixture(scope="function")
def parser() -> Parser:
    return Parser()


@pytest.fixture(scope="function")
def parse(parser: Parser):
    def _parse(source: str):
        return parser.parse(source)
    return _parse


@pytest.fixture(scope="function")
def tokenize(parser: Parser):
    def _tokenize(source: str):
        return parser.tokenize(source)
    return _tokenize


@pytest.fixture(scope="function")
def compile_source():
    def _compile(source: str):
        return Compilation(source).compile()
    return _compile
