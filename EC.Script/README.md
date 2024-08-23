Script库用来实现ecp脚本的解析和执行

- 语法规则定义
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