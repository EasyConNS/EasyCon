"""Tokenization tests — verify lexer produces correct token types."""

from __future__ import annotations
import pytest


# ---------------------------------------------------------------------------
# Keywords
# ---------------------------------------------------------------------------

KEYWORD_TESTS = [
    ("IMPORT", "IMPORT_KW"),
    ("IF", "IF_KW"),
    ("ELIF", "ELIF_KW"),
    ("ELSE", "ELSE_KW"),
    ("ENDIF", "ENDIF_KW"),
    ("WHILE", "WHILE_KW"),
    ("FOR", "FOR_KW"),
    ("TO", "TO_KW"),
    ("IN", "IN_KW"),
    ("STEP", "STEP_KW"),
    ("BREAK", "BREAK_KW"),
    ("CONTINUE", "CONTINUE_KW"),
    ("NEXT", "NEXT_KW"),
    ("FUNC", "FUNC_KW"),
    ("RETURN", "RETURN_KW"),
    ("ENDFUNC", "ENDFUNC_KW"),
    ("END", "END_KW"),
    ("TRUE", "TRUE_KW"),
    ("FALSE", "FALSE_KW"),
    ("RESET", "RESET_KW"),
    ("WAIT", "WAIT_KW"),
    ("CALL", "CALL_KW"),
    ("EXTERN", "EXTERN_KW"),
    ("FROM", "FROM_KW"),
    ("BEEP", "BEEP_KW"),
    ("AMIIBO", "AMIIBO_KW"),
]


@pytest.mark.parametrize("source,expected_type", KEYWORD_TESTS)
def test_keyword_token(tokenize, source, expected_type):
    tokens = tokenize(source)
    kw_tokens = [t for t in tokens if t["type"] == expected_type]
    assert len(kw_tokens) >= 1, f"Expected token type {expected_type} for {source}, got {[t['type'] for t in tokens]}"
    assert kw_tokens[0]["value"] == source


# ---------------------------------------------------------------------------
# Buttons
# ---------------------------------------------------------------------------

BUTTON_TESTS = [
    "A", "B", "X", "Y",
    "L", "R", "ZL", "ZR",
    "HOME", "CAPTURE", "PLUS", "MINUS",
    "LCLICK", "RCLICK",
]


@pytest.mark.parametrize("button", BUTTON_TESTS)
def test_button_key_token(tokenize, button):
    tokens = tokenize(button)
    btn_tokens = [t for t in tokens if t["type"] == "BUTTON_KEY"]
    assert len(btn_tokens) >= 1, f"Expected BUTTON_KEY for {button}"
    assert btn_tokens[0]["value"].upper() == button.upper()


# ---------------------------------------------------------------------------
# Stick keys
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("stick", ["LS", "RS"])
def test_stick_key_token(tokenize, stick):
    tokens = tokenize(stick)
    stick_tokens = [t for t in tokens if t["type"] == "STICK_KEY"]
    assert len(stick_tokens) >= 1


# ---------------------------------------------------------------------------
# Direction keys
# ---------------------------------------------------------------------------

DIRECTION_TESTS = [
    "UP", "DOWN", "LEFT", "RIGHT",
    "UPLEFT", "UPRIGHT", "DOWNLEFT", "DOWNRIGHT",
]


@pytest.mark.parametrize("direction", DIRECTION_TESTS)
def test_direction_key_token(tokenize, direction):
    tokens = tokenize(direction)
    dir_tokens = [t for t in tokens if t["type"] == "DIRECTION_KEY"]
    assert len(dir_tokens) >= 1, f"Expected DIRECTION_KEY for {direction}"


# ---------------------------------------------------------------------------
# Variables, constants, external vars
# ---------------------------------------------------------------------------

def test_variable_token(tokenize):
    tokens = tokenize("$a")
    var_tokens = [t for t in tokens if t["type"] == "VAR"]
    assert len(var_tokens) == 1
    assert var_tokens[0]["value"] == "$a"


def test_global_variable_token(tokenize):
    tokens = tokenize("$$global")
    var_tokens = [t for t in tokens if t["type"] == "VAR"]
    assert len(var_tokens) == 1
    assert var_tokens[0]["value"] == "$$global"


def test_constant_token(tokenize):
    tokens = tokenize("_MAX")
    const_tokens = [t for t in tokens if t["type"] == "CONST"]
    assert len(const_tokens) == 1
    assert const_tokens[0]["value"] == "_MAX"


def test_ex_var_token(tokenize):
    tokens = tokenize("@image")
    ex_tokens = [t for t in tokens if t["type"] == "EX_VAR"]
    assert len(ex_tokens) == 1
    assert ex_tokens[0]["value"] == "@image"


# ---------------------------------------------------------------------------
# Strings and numbers
# ---------------------------------------------------------------------------

def test_double_quoted_string(tokenize):
    tokens = tokenize('"hello"')
    str_tokens = [t for t in tokens if t["type"] == "STRING"]
    assert len(str_tokens) == 1


def test_single_quoted_string(tokenize):
    tokens = tokenize("'hello'")
    str_tokens = [t for t in tokens if t["type"] == "STRING"]
    assert len(str_tokens) == 1


def test_integer_token(tokenize):
    tokens = tokenize("42")
    int_tokens = [t for t in tokens if t["type"] == "INT"]
    assert len(int_tokens) == 1
    assert int_tokens[0]["value"] == "42"


def test_negative_integer_token(tokenize):
    tokens = tokenize("-42")
    int_tokens = [t for t in tokens if t["type"] == "INT"]
    assert len(int_tokens) == 1
    assert int_tokens[0]["value"] == "-42"


def test_float_token(tokenize):
    tokens = tokenize("3.14")
    num_tokens = [t for t in tokens if t["type"] == "NUMBER"]
    assert len(num_tokens) == 1


# ---------------------------------------------------------------------------
# Identifiers
# ---------------------------------------------------------------------------

def test_identifier_token(tokenize):
    tokens = tokenize("myFunc")
    id_tokens = [t for t in tokens if t["type"] == "IDENT"]
    assert len(id_tokens) == 1
    assert id_tokens[0]["value"] == "myFunc"


def test_chinese_identifier(tokenize):
    tokens = tokenize("测试")
    id_tokens = [t for t in tokens if t["type"] == "IDENT"]
    assert len(id_tokens) >= 1


# ---------------------------------------------------------------------------
# Comments
# ---------------------------------------------------------------------------

def test_comment_token(tokenize):
    tokens = tokenize("# this is a comment")
    comment_tokens = [t for t in tokens if t["type"] == "COMMENT"]
    assert len(comment_tokens) == 1


# ---------------------------------------------------------------------------
# Operators
# ---------------------------------------------------------------------------

OPERATOR_TESTS = [
    ("+", "OP"),
    ("*", "OP"),
    ("/", "OP"),
    ("\\", "OP"),
    ("%", "OP"),
    ("^", "OP"),
    ("&", "OP"),
    ("|", "OP"),
    ("<", "OP"),
    (">", "OP"),
    ("==", "OP"),
    ("!=", "OP"),
    ("<=", "OP"),
    (">=", "OP"),
    ("<<", "OP"),
    (">>", "OP"),
    ("=", "ASSIGN_OP"),
    ("+=", "ASSIGN_OP"),
    ("-=", "ASSIGN_OP"),
    ("*=", "ASSIGN_OP"),
    ("/=", "ASSIGN_OP"),
    ("%=", "ASSIGN_OP"),
]


@pytest.mark.parametrize("op_str,expected_type", OPERATOR_TESTS)
def test_operator_token(tokenize, op_str, expected_type):
    source = f"$a{op_str}1"
    tokens = tokenize(source)
    op_tokens = [t for t in tokens if t["type"] == expected_type and t["value"] == op_str]
    assert len(op_tokens) >= 1, f"Expected {expected_type} for {op_str} in {source}, tokens: {[(t['type'], t['value']) for t in tokens]}"


# ---------------------------------------------------------------------------
# Logical operators
# ---------------------------------------------------------------------------

@pytest.mark.parametrize("logic_op", ["and", "or", "not"])
def test_logical_operator_token(tokenize, logic_op):
    tokens = tokenize(f"$a {logic_op} $b")
    logic_tokens = [t for t in tokens if t["type"] == "LOGIC_OP" and t["value"] == logic_op]
    assert len(logic_tokens) == 1


# ---------------------------------------------------------------------------
# Punctuation
# ---------------------------------------------------------------------------

def test_paren_tokens(tokenize):
    tokens = tokenize("( )")
    paren_tokens = [t for t in tokens if t["type"] == "PAREN"]
    assert len(paren_tokens) == 2


def test_bracket_tokens(tokenize):
    tokens = tokenize("[ ]")
    bracket_tokens = [t for t in tokens if t["type"] == "BRACKET"]
    assert len(bracket_tokens) == 2


def test_comma_token(tokenize):
    tokens = tokenize("1, 2")
    comma_tokens = [t for t in tokens if t["type"] == "COMMA"]
    assert len(comma_tokens) == 1


def test_colon_token(tokenize):
    tokens = tokenize(":INT")
    colon_tokens = [t for t in tokens if t["type"] == "COLON"]
    assert len(colon_tokens) == 1


# ---------------------------------------------------------------------------
# Newlines
# ---------------------------------------------------------------------------

def test_newline_token(tokenize):
    tokens = tokenize("A\nB")
    nl_tokens = [t for t in tokens if t["type"] == "_NL"]
    assert len(nl_tokens) == 1


def test_windows_newline(tokenize):
    tokens = tokenize("A\r\nB")
    nl_tokens = [t for t in tokens if t["type"] == "_NL"]
    assert len(nl_tokens) == 1
    assert nl_tokens[0]["value"] == "\r\n"


# ---------------------------------------------------------------------------
# Edge cases
# ---------------------------------------------------------------------------

def test_empty_source(tokenize):
    tokens = tokenize("")
    assert tokens == []


def test_only_whitespace(tokenize):
    tokens = tokenize("   \t  ")
    assert tokens == []


def test_only_newlines(tokenize):
    tokens = tokenize("\n\n\n")
    nl_tokens = [t for t in tokens if t["type"] == "_NL"]
    assert len(nl_tokens) == 3


def test_mixed_case_button(tokenize):
    """Button keys are case-insensitive in the tokenizer."""
    tokens = tokenize("a")
    btn = [t for t in tokens if t["type"] == "BUTTON_KEY"]
    assert len(btn) == 1
