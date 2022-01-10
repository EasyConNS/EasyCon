namespace EasyCon2.Script
{
    class Processor
    {
        // only for online
        public const uint RegisterCount = 64;
        public const uint OfflineMaxRegisterCount = 8;

        public IOutputAdapter Output;
        public ICGamePad GamePad;
        public int PC = 0;
        public bool CancelLineBreak = false;
        public Stack<short> Stack = new();
        public Stack<int> CallStack = new();
        public Stack<Parsing.Statements.For> LoopStack = new();
        public Dictionary<Parsing.Statements.For, int> LoopTime = new();
        public Dictionary<Parsing.Statements.For, int> LoopCount = new();
        public RegisterFile Register = new(RegisterCount);
    }

    class RegisterFile
    {
        private short[] _register;

        public RegisterFile(uint length)
        {
            _register = new short[length];
        }

        public short this[uint index]
        {
            get => _register[index];
            set => _register[index] = value;
        }

        public int this[Parsing.ValRegEx val]
        {
            get
            {
                if (val is Parsing.ValReg)
                    return this[(val as Parsing.ValReg).Index];
                else if (val is Parsing.ValReg32)
                    return GetReg32((val as Parsing.ValReg32).Index);
                throw new ArgumentException();
            }

            set
            {
                if (val is Parsing.ValReg)
                    _register[(val as Parsing.ValReg).Index] = (short)value;
                else if (val is Parsing.ValReg32)
                    SetReg32((val as Parsing.ValReg32).Index, value);
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
