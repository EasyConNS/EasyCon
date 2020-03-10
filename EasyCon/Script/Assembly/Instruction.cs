using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assembly
{
    abstract class Instruction
    {
        public const uint REG_COUNT = 8;

        public int Index { get; set; }
        public int Address { get; set; }
        public virtual int InsCount => 1;
        public virtual int ByteCount => 2;
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

        protected static bool Scale(ref int val, int unit)
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
            _bitbuffer = _bitbuffer << bitcount | bits;
            _bitcount += bitcount;
            while (_bitcount >= 8)
            {
                stream.WriteByte((byte)(_bitbuffer >> _bitcount - 8));
                //_bitbuffer = _bitbuffer & ((1ul << (_bitcount - 8)) - 1);
                _bitcount -= 8;
            }
        }

        protected void WriteBits(Stream stream, long bits, int bitcount)
        {
            WriteBits(stream, (ulong)bits, bitcount);
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
            public static readonly Failed NotSupported = new Failed("这个语句暂时只能联机模式使用");
            public static readonly Failed NotImplemented = new Failed("类型未定义，这可能是一个bug，请汇报给作者");
        }

        public static Instruction CreateInstance(Type type, params object[] args)
        {
            return type.GetMethod("Create", System.Reflection.BindingFlags.FlattenHierarchy | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, args) as Instruction;
        }
    }
}
