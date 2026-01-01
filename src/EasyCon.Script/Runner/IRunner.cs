using EasyScript;
using EasyScript.Statements;
using System.Collections.Immutable;

namespace EasyCon.Script.Runner;

interface IRunner
{
    void Assemble(ImmutableArray<Statement> statements);
    void Run(IOutputAdapter output, ICGamePad pad);
}