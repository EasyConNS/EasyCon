"""Map ECS tokenizer output to LSP semantic token types."""

from lsprotocol.types import SemanticTokenTypes

# Ordered list of token types for the legend
TOKEN_TYPES = [
    SemanticTokenTypes.Keyword,
    SemanticTokenTypes.Function,
    SemanticTokenTypes.Variable,
    SemanticTokenTypes.Number,
    SemanticTokenTypes.String,
    SemanticTokenTypes.Comment,
    SemanticTokenTypes.EnumMember,
    SemanticTokenTypes.Operator,
    SemanticTokenTypes.Macro,
    SemanticTokenTypes.Type,
]

TOKEN_TYPE_MAP = {
    "IMPORT_KW": SemanticTokenTypes.Keyword,
    "IF_KW": SemanticTokenTypes.Keyword,
    "ELIF_KW": SemanticTokenTypes.Keyword,
    "ELSE_KW": SemanticTokenTypes.Keyword,
    "ENDIF_KW": SemanticTokenTypes.Keyword,
    "WHILE_KW": SemanticTokenTypes.Keyword,
    "FOR_KW": SemanticTokenTypes.Keyword,
    "TO_KW": SemanticTokenTypes.Keyword,
    "IN_KW": SemanticTokenTypes.Keyword,
    "STEP_KW": SemanticTokenTypes.Keyword,
    "BREAK_KW": SemanticTokenTypes.Keyword,
    "CONTINUE_KW": SemanticTokenTypes.Keyword,
    "NEXT_KW": SemanticTokenTypes.Keyword,
    "FUNC_KW": SemanticTokenTypes.Keyword,
    "RETURN_KW": SemanticTokenTypes.Keyword,
    "ENDFUNC_KW": SemanticTokenTypes.Keyword,
    "END_KW": SemanticTokenTypes.Keyword,
    "TRUE_KW": SemanticTokenTypes.Keyword,
    "FALSE_KW": SemanticTokenTypes.Keyword,
    "RESET_KW": SemanticTokenTypes.Keyword,
    "WAIT_KW": SemanticTokenTypes.Keyword,
    "CALL_KW": SemanticTokenTypes.Keyword,
    "EXTERN_KW": SemanticTokenTypes.Keyword,
    "FROM_KW": SemanticTokenTypes.Keyword,
    "KEY_MOD": SemanticTokenTypes.Keyword,
    "IDENT": SemanticTokenTypes.Macro,
    "VAR": SemanticTokenTypes.Variable,
    "CONST": SemanticTokenTypes.Variable,
    "EX_VAR": SemanticTokenTypes.Variable,
    "INT": SemanticTokenTypes.Number,
    "NUMBER": SemanticTokenTypes.Number,
    "STRING": SemanticTokenTypes.String,
    "COMMENT": SemanticTokenTypes.Comment,
    "BUTTON_KEY": SemanticTokenTypes.Function,
    "STICK_KEY": SemanticTokenTypes.Type,
    "DIRECTION_KEY": SemanticTokenTypes.EnumMember,
    "LOGIC_OP": SemanticTokenTypes.Operator,
    "OP": SemanticTokenTypes.Operator,
    "ASSIGN_OP": SemanticTokenTypes.Operator,
    "PAREN": SemanticTokenTypes.Operator,
    "BRACKET": SemanticTokenTypes.Operator,
    "COMMA": SemanticTokenTypes.Operator,
    "COLON": SemanticTokenTypes.Operator,
}

# Map token type string to index in legend


def _build_type_index() -> dict[str, int]:
    return {t: i for i, t in enumerate(TOKEN_TYPES)}


_TYPE_INDEX = _build_type_index()


def get_semantic_token_type(token_name: str) -> int | None:
    """Return LSP semantic token type index, or None to skip."""
    sem_type = TOKEN_TYPE_MAP.get(token_name)
    if sem_type is None:
        return None
    return _TYPE_INDEX.get(sem_type)


def build_token_legend() -> tuple[list[str], list[str]]:
    """Return (token_types, token_modifiers) for server capabilities."""
    return [t.value for t in TOKEN_TYPES], []
