Script模块用来实现ecp脚本的解析和执行

- 词法分析
- 语法解析
- ast生成

```ebnf
main::= {decl | stmt}

decl ::= importdecl | constdecl | arrdecl | funcdecl

stmt ::= assignment | augassign | ifstmt | forstmt | callstmt | keyaction | stickaction

constdecl ::= const '=' expr

assignment ::= suffexpr '=' expr

suffexpr ::= var | indexvar

expr ::=

condition ::=
```