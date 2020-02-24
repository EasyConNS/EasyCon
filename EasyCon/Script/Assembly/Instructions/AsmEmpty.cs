using EasyCon.Script.Assembly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assembly.Instructions
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
