using System;
using System.IO;
using EasyCon.Script2.Syntax;
using EasyCon.Script2.IO;

namespace EasyCon.Script2.Symbols;

internal static class SymbolPrinter
{
    public static void WriteTo(Symbol symbol, TextWriter writer)
    {
        switch (symbol.Kind)
        {
            case SymbolKind.Function:
                WriteFunctionTo((FunctionSymbol)symbol, writer);
                break;
            case SymbolKind.GlobalVariable:
                WriteGlobalVariableTo((GlobalVariableSymbol)symbol, writer);
                break;
            case SymbolKind.LocalVariable:
                WriteLocalVariableTo((LocalVariableSymbol)symbol, writer);
                break;
            case SymbolKind.Parameter:
                WriteParameterTo((ParameterSymbol)symbol, writer);
                break;
            case SymbolKind.Type:
                WriteTypeTo((TypeSymbol)symbol, writer);
                break;
            default:
                throw new Exception($"Unexpected symbol: {symbol.Kind}");
        }
    }

    private static void WriteFunctionTo(FunctionSymbol symbol, TextWriter writer)
    {
        writer.WriteKeyword(TokenType.FUNC);
        writer.WriteSpace();
        writer.WriteIdentifier(symbol.Name);
        writer.WritePunctuation(TokenType.LeftParen);

        for (int i = 0; i < symbol.Parameters.Length; i++)
        {
            if (i > 0)
            {
                writer.WritePunctuation(TokenType.COMMA);
                writer.WriteSpace();
            }

            symbol.Parameters[i].WriteTo(writer);
        }

        writer.WritePunctuation(TokenType.RightParen);

        if (symbol.Type != TypeSymbol.Void)
        {
            writer.WritePunctuation(TokenType.COLON);
            writer.WriteSpace();
            symbol.Type.WriteTo(writer);
        }
    }

    private static void WriteGlobalVariableTo(GlobalVariableSymbol symbol, TextWriter writer)
    {
        //writer.WriteKeyword(symbol.IsReadOnly ? SyntaxKind.LetKeyword : SyntaxKind.VarKeyword);
        writer.WriteSpace();
        writer.WriteIdentifier(symbol.Name);
        writer.WritePunctuation(TokenType.COLON);
        writer.WriteSpace();
        symbol.Type.WriteTo(writer);
    }

    private static void WriteLocalVariableTo(LocalVariableSymbol symbol, TextWriter writer)
    {
        //writer.WriteKeyword(symbol.IsReadOnly ? SyntaxKind.LetKeyword : SyntaxKind.VarKeyword);
        writer.WriteSpace();
        writer.WriteIdentifier(symbol.Name);
        writer.WritePunctuation(TokenType.COLON);
        writer.WriteSpace();
        symbol.Type.WriteTo(writer);
    }

    private static void WriteParameterTo(ParameterSymbol symbol, TextWriter writer)
    {
        writer.WriteIdentifier(symbol.Name);
        writer.WritePunctuation(TokenType.COLON);
        writer.WriteSpace();
        symbol.Type.WriteTo(writer);
    }

    private static void WriteTypeTo(TypeSymbol symbol, TextWriter writer)
    {
        writer.WriteIdentifier(symbol.Name);
    }
}