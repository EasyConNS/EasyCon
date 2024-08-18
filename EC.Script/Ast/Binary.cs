// Copyright 2012 Fan Shi
// 
// This file is part of the VBF project.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using VBF.Compilers.Scanners;

namespace ECP.Ast;

public enum BinaryOperator
{
    Add,
    Substract,
    Multiply,
    Divide,
    RangeDivide,
    Mod,
    Less,
    LessEq,
    Greater,
    GreaterEq,
    Equal,
    NotEqual,
    LogicalAnd,
    LogicalOr
}

public class Binary : Expression
{
    private static Dictionary<string, BinaryOperator> s_OperatorMap;

    static Binary()
    {
        s_OperatorMap = new Dictionary<string, BinaryOperator>
        {
            ["+"] = BinaryOperator.Add,
            ["-"] = BinaryOperator.Substract,
            ["*"] = BinaryOperator.Multiply,
            ["/"] = BinaryOperator.Divide,
            ["\\"] = BinaryOperator.RangeDivide,
            ["%"] = BinaryOperator.Mod,
            ["<"] = BinaryOperator.Less,
            ["<="] = BinaryOperator.LessEq,
            [">"] = BinaryOperator.Greater,
            [">="] = BinaryOperator.GreaterEq,
            ["=="] = BinaryOperator.Equal,
            ["!="] = BinaryOperator.NotEqual,
            ["and"] = BinaryOperator.LogicalAnd,
            ["or"] = BinaryOperator.LogicalOr
        };
    }

    public Binary(LexemeValue op, Expression left, Expression right)
    {
        Operator = s_OperatorMap[op.Content];
        Left = left;
        Right = right;
        OpLexeme = op;
    }

    public Expression Left { get; private set; }
    public Expression Right { get; private set; }
    public BinaryOperator Operator { get; private set; }
    public LexemeValue OpLexeme { get; private set; }

    public override T Accept<T>(IAstVisitor<T> visitor)
    {
        return visitor.VisitBinary(this);
    }

    public override string ToString()
    {
        return $"{Left}{OpLexeme}{Right}";
    }
}
