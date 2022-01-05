using System.IO;

namespace EasyCon2.Script.Assembly.Instructions
{
    class AsmEmpty : Instruction
    {
        public override int ByteCount => 0;

        public override int InsCount => 0;

        public static Instruction Create()
        {
            return new AsmEmpty();
        }

        public override void WriteBytes(Stream stream)
        { }
    }
}
