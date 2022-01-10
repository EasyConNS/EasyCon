using EasyCon2.Global;
using System.IO;
using System.Text;

namespace EasyCon2.Script.Assembly
{
    class Assembler
    {
        public static readonly byte[] EmptyAsm = new byte[] { 0xFF, 0xFF };
        public const uint IReg = 7;

        readonly List<Instruction> _instructions = new();
        public Dictionary<Parsing.Statements.For, Instructions.AsmFor> ForMapping = new();
        public Dictionary<Parsing.Statements.If, Instructions.AsmBranchFalse> IfMapping = new();
        public Dictionary<Parsing.Statements.Else, Instructions.AsmBranch> ElseMapping = new();
        public Dictionary<int, Instructions.AsmKey_Hold> KeyMapping = new();
        public Dictionary<int, Instructions.AsmStick_Hold> StickMapping = new();
        public Dictionary<string, Instructions.AsmBranch> FunctionMapping = new();
        public Dictionary<string, Instructions.AsmEmpty> CallMapping = new();

        public void Add(Instruction ins)
        {
            _instructions.Add(ScripterUtil.Assert(ins));
        }

        public Instruction Last()
        {
            return _instructions.Last();
        }

        public byte[] Assemble(List<Parsing.Statement> statements, bool autoRun)
        {
            // initialize
            _instructions.Clear();
            ForMapping.Clear();
            IfMapping.Clear();
            ElseMapping.Clear();
            KeyMapping.Clear();
            StickMapping.Clear();
            FunctionMapping.Clear();
            CallMapping.Clear();

            // compile into instructions
            for (int i = 0; i < statements.Count; i++)
            {
                try
                {
                    statements[i].Assemble(this);
                }
                catch (AssembleException ex)
                {
                    ex.Index = i;
                    throw;
                }
            }

            // optimize
            var discarded = new HashSet<Instruction>();
            var list = new List<Instruction>();
            foreach (var item in _instructions)
            {
                list.Add(item);

                // 1 Instruction
                var ins1 = list[list.Count - 1];

                // 2 Instructions
                if (list.Count < 2)
                    continue;
                var ins2 = ins1;
                ins1 = list[list.Count - 2];
                // keypress-wait => compressed keypress
                if (ins1 is Instructions.AsmKey_Standard && ins2 is Instructions.AsmWait)
                {
                    var press = ins1 as Instructions.AsmKey_Standard;
                    var wait = ins2 as Instructions.AsmWait;
                    var ins = Instructions.AsmKey_Compressed.Create(press.KeyCode, press.RealDuration, wait.RealDuration);
                    if (ins.Success)
                    {
                        list.Add(ins);
                        discarded.Add(ins1);
                        discarded.Add(ins2);
                        continue;
                    }
                }

                if (ins1 is Instructions.AsmCall && ins2 is Instructions.AsmLabel)
                {
                    var funclbl = (ins2 as Instructions.AsmLabel);
                    var callfunc = CallMapping.GetValueOrDefault(funclbl.Label, null);
                    var ins = Instructions.AsmCall.Create(callfunc);
                    list.Add(ins);
                    discarded.Add(ins1);
                    discarded.Add(ins2);
                    continue;
                }

                // 3 Instructions
                if (list.Count < 3)
                    continue;
                var ins3 = ins2;
                ins2 = ins1;
                ins1 = list[list.Count - 3];
                // if-loopcontrol-endif => loopcontrol_cf
                if (ins1 is Instructions.AsmBranchFalse && ins2 is Instructions.AsmLoopControl && ins3 is Instructions.AsmEmpty && (ins1 as Instructions.AsmBranchFalse).Target == ins3)
                {
                    (ins2 as Instructions.AsmLoopControl).CheckFlag = 1;
                    discarded.Add(ins1);
                    continue;
                }

                // 4 Instructions
                if (list.Count < 4)
                    continue;
                var ins4 = ins3;
                ins3 = ins2;
                ins2 = ins1;
                ins1 = list[list.Count - 4];
                // if-loopcontrol-else-endif => loopcontrol_cf-ifnot-endif
                if (ins1 is Instructions.AsmBranchFalse && ins2 is Instructions.AsmLoopControl && ins3 is Instructions.AsmBranch && ins4 is Instructions.AsmEmpty && (ins1 as Instructions.AsmBranchFalse).Target == ins4)
                {
                    (ins2 as Instructions.AsmLoopControl).CheckFlag = 1;
                    list.Add(Instructions.AsmBranchTrue.Create((ins3 as Instructions.AsmBranch).Target));
                    discarded.Add(ins1);
                    discarded.Add(ins3);
                    continue;
                }
            }
            _instructions.Clear();
            _instructions.AddRange(list);

            // update address and index
            int addr = 2;
            int index = 0;
            for (int i = 0; i < _instructions.Count; i++)
            {
                var ins = _instructions[i];
                ins.Address = addr;
                ins.Index = index;
                if (!discarded.Contains(ins))
                {
                    addr += ins.ByteCount;
                    index += ins.InsCount;
                }
            }

            // generate bytecode
            using var stream = new MemoryStream();
            // preserved for length
            stream.WriteByte(0);
            stream.WriteByte(0);
            // write instructions
            foreach (var ins in _instructions)
                if (!discarded.Contains(ins))
                    ins.WriteBytes(stream);
            // get array
            var bytes = stream.ToArray();
            // write length
            bytes[0] = (byte)(bytes.Length & 0xFF);
            bytes[1] = (byte)(bytes.Length >> 8);

            if (!autoRun)
            {
                bytes[1] |= 0x80;
            }
            return bytes;
        }

        public static string WriteHex(string hexStr, byte[] asmBytes, Board board)
        {
            if (asmBytes.Length > board.DataSize)
                throw new AssembleException("烧录失败！字节超出限制");
            var list = new List<byte>(asmBytes);
            // load hex file
            var lines = hexStr.Split('\r', '\n');
            var hexFile = new List<IntelHex>();
            foreach (var line in lines)
            {
                try
                {
                    var hex = IntelHex.Parse(line);
                    if (hex != null)
                        hexFile.Add(hex);
                }
                catch (AssembleException ex)
                {
                    throw new AssembleException("固件读取失败！" + ex.Message);
                }
            }
            // find data position
            int index = hexFile.FindIndex(hex =>
            {
                if (hex.Data.Length < 0x10)
                    return false;
                for (int i = 0; i < hex.Data.Length; i++)
                    if (i < 2 && hex.Data[i] != 0xFF || i > 2 && hex.Data[i] != 0)
                        return false;
                return true;
            });
            if (index == -1)
                throw new AssembleException("固件读取失败！未找到数据结构");
            int ver = hexFile[index].Data[2];
            if (ver < board.Version)
                throw new AssembleException("固件版本不符，请使用最新版的固件");
            // write data from asmBytes
            for (int i = 0; i < list.Count;)
            {
                int len = Math.Min(hexFile[index].DataSize, list.Count - i);
                byte[] data = hexFile[index].Data.ToArray();
                list.CopyTo(i, data, 0, len);
                hexFile[index].WriteData(data);
                i += len;
                index++;
            }
            // get string
            var str = new StringBuilder();
            foreach (var hex in hexFile)
                str.AppendLine(hex.ToString());
            return str.ToString();
        }
    }

    class IntelHex
    {
        public byte DataSize { get; private set; }
        public ushort StartAddress { get; private set; }
        public byte RecordType { get; private set; }
        public byte[] Data { get; private set; }
        public byte Checksum { get; private set; }

        public static IntelHex Parse(string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line))
                    return null;
                line = line.Trim();
                if (line[0] != ':')
                    throw new AssembleException("需要以“:”作为起始");
                List<byte> bytes = new List<byte>();
                for (int i = 1; i < line.Length; i += 2)
                    bytes.Add(Convert.ToByte(line.Substring(i, 2), 16));
                IntelHex hex = new IntelHex();
                int index = 0;
                hex.DataSize = bytes[index++];
                hex.StartAddress = (ushort)(bytes[index++] << 8);
                hex.StartAddress = (ushort)(hex.StartAddress | bytes[index++]);
                hex.RecordType = bytes[index++];
                hex.Data = bytes.GetRange(index, hex.DataSize).ToArray();
                index += hex.DataSize;
                hex.Checksum = bytes[index++];
                if (index != bytes.Count)
                    throw new AssembleException("数据过多");
                return hex;
            }
            catch (IndexOutOfRangeException)
            {
                throw new AssembleException("数据过少");
            }
        }

        void FixChecksum()
        {
            Checksum = 0;
            Checksum -= DataSize;
            Checksum -= (byte)(StartAddress >> 8);
            Checksum -= (byte)(StartAddress & 0xFF);
            Checksum -= RecordType;
            foreach (var b in Data)
                Checksum -= b;
        }

        public void WriteData(byte[] data)
        {
            DataSize = (byte)data.Length;
            Data = data;
            FixChecksum();
        }

        public byte[] GetBytes()
        {
            List<byte> bytes = new List<byte>();
            bytes.Add(DataSize);
            bytes.Add((byte)(StartAddress >> 8));
            bytes.Add((byte)(StartAddress & 0xFF));
            bytes.Add(RecordType);
            bytes.AddRange(Data);
            bytes.Add(Checksum);
            return bytes.ToArray();
        }

        public string GetString()
        {
            return ":" + string.Join("", GetBytes().Select(b => b.ToString("X2")));
        }

        public override string ToString()
        {
            return GetString();
        }
    }

    public class AssembleException : Exception
    {
        public int Index;

        public AssembleException(string message, int index = -1)
            : base(message)
        {
            Index = index;
        }
    }
}
