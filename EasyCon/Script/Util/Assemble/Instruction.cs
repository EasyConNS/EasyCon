using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    abstract class Instruction
    {
        public const uint REG_COUNT = 8;

        public uint Index { get; set; }
        public uint Address { get; set; }
        public virtual uint InsCount => 1;
        public virtual uint ByteCount => 2;
        public bool Success => !(this is Failed);

        public abstract void WriteBytes(Stream stream);

        public byte[] GetBytes()
        {
            using (var stream = new MemoryStream())
            {
                WriteBytes(stream);
                return stream.ToArray();
            }
        }

        protected static bool Scale(ref uint val, uint unit)
        {
            // Ceiling(val / unit)
            var b = val % unit == 0;
            val = (val + unit - 1) / unit;
            return b;
        }

        ulong _bitbuffer = 0;
        int _bitcount = 0;

        protected void WriteBits(Stream stream, ulong bits, int bitcount)
        {
            _bitbuffer = (_bitbuffer << bitcount) | bits;
            _bitcount += bitcount;
            while (_bitcount >= 8)
            {
                stream.WriteByte((byte)(_bitbuffer >> (_bitcount - 8)));
                //_bitbuffer = _bitbuffer & ((1ul << (_bitcount - 8)) - 1);
                _bitcount -= 8;
            }
        }

        // simulated exception to avoid try-catch hell
        public class Failed : Instruction
        {
            public string Message;

            Failed(string message)
            {
                Message = message;
            }

            public override void WriteBytes(Stream stream)
            {
                throw new InvalidOperationException();
            }

            public static readonly Failed OutOfRange = new Failed("数值超出范围");
            public static readonly Failed InvalidArgument = new Failed("错误的参数格式");
        }
    }

    interface IAsmKey
    {
        uint KeyCode { get; set; }
    }

    interface IAsmHold
    {
        uint HoldUntil { get; set; }
    }
}
