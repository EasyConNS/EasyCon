namespace EasyCon.Script.Syntax;

class SerialPrint(uint value, bool mem) : Statement(null!)
{
    public readonly uint Value = value;
    public readonly bool Mem = mem;

    protected override string _GetString()
    {
        return Mem ? $"SMEM {Value}" : $"SPRINT {Value}";
    }

}