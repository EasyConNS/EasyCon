"""Compilation entry point for EasyCon script engine."""
from __future__ import annotations
from typing import Optional, List

from .parser import Parser, ParseError, _validate_program
from . import ast


class CompilationResult:
    def __init__(self, program: Optional[ast.Program] = None, errors: Optional[List[str]] = None):
        self.program = program
        self.errors = errors or []

    @property
    def has_errors(self) -> bool:
        return len(self.errors) > 0


class Compilation:
    def __init__(self, source: str):
        self.source = source
        self._parser = Parser()
        self._program: Optional[ast.Program] = None
        self._errors: List[str] = []

    def compile(self) -> CompilationResult:
        try:
            self._program = self._parser.parse(self.source)
            semantic_errors = _validate_program(self._program)
            if semantic_errors:
                return CompilationResult(program=self._program, errors=semantic_errors)
            return CompilationResult(program=self._program)
        except ParseError as e:
            self._errors.append(str(e))
            return CompilationResult(errors=self._errors)
