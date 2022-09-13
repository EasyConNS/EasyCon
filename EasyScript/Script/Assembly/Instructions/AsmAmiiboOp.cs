using System.IO;

namespace EasyScript.Assembly.Instructions;

class AsmAmiibo : Instruction
{
    public uint AmiiboIndex;

    public static Instruction Create(Parsing.ValBase value)
    {
        if (value is Parsing.ValInstant iVal)
        {
            var val = (uint)iVal.Val;
            if (val > 9)
                return Failed.OutOfRange;
            var ins = new AsmAmiibo
            {
                AmiiboIndex = val
            };
            return ins;
        }
        /*
        if (value is Parsing.ValReg rVal)
        {
            var ins = new AsmAmiibo
            {
                AmiiboIndex = rVal.Index
            };
            return ins;
        }
        */
        else
            return Failed.NotImplemented;
    }

    public override void WriteBytes(Stream stream)
    {
        WriteBits(stream, 0, 1);
        WriteBits(stream, 0b0111, 4);
        WriteBits(stream, 0, 3);
        WriteBits(stream, AmiiboIndex, 8);
    }
}