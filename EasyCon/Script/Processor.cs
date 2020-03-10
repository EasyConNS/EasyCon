using EasyCon.Script.Parsing.Statements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyCon.Script.Parsing;

namespace EasyCon.Script
{
    class Processor
    {
        public const uint RegisterCount = 8;

        public IOutputAdapter Output;
        public int PC = 0;
        public bool CancelLineBreak = false;
        public Stack<short> Stack = new Stack<short>();
        public Stack<For> LoopStack = new Stack<For>();
        public Dictionary<For, int> LoopTime = new Dictionary<For, int>();
        public Dictionary<For, int> LoopCount = new Dictionary<For, int>();
        public RegisterFile Register = new RegisterFile(RegisterCount);

        public class RegisterFile
        {
            short[] _register;

            public RegisterFile(uint length)
            {
                _register = new short[length];
            }

            public short this[uint index]
            {
                get => _register[index];
                set => _register[index] = value;
            }

            public int this[ValRegEx val]
            {
                get
                {
                    if (val is ValReg)
                        return this[(val as ValReg).Index];
                    else if (val is ValReg32)
                        return GetReg32((val as ValReg32).Index);
                    throw new ArgumentException();
                }

                set
                {
                    if (val is ValReg)
                        _register[(val as ValReg).Index] = (short)value;
                    else if (val is ValReg32)
                        SetReg32((val as ValReg32).Index, value);
                }
            }

            public int GetReg32(uint index)
            {
                return ((ushort)_register[index] | (_register[index + 1] << 16));
            }

            public void SetReg32(uint index, int value)
            {
                _register[index] = (short)value;
                _register[index + 1] = (short)(value >> 16);
            }
        }
    }
}
