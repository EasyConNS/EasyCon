// See https://aka.ms/new-console-template for more information

using Injection;
using Scripter;

var text = "";
if(args.Length > 1)
{
    text = File.ReadAllText(args[1]);
}

//PointerUtil.GetPointerAddress("[[main+427C470]+1F0]+68");

text = File.ReadAllText("testscript.txt");
ScriptUtil.Build(text);
