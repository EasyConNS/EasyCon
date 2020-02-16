using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PTDevice;
using EasyCon.Script.Assemble;
using System.IO;

namespace EasyCon.Script
{
    class Assembler
    {
        public const int MaxBytes = 400;
        public const int FirmwareVersion = 0x42;
        public static readonly byte[] EmptyAsm = new byte[] { 0xFF, 0xFF };

        public static int GetDirectionIndex(NintendoSwitch.Key key)
        {
            int x = key.StickX;
            int y = key.StickY;
            if (x == NintendoSwitch.STICK_CENTER && y == NintendoSwitch.STICK_CENTER)
                return -1;
            x = (int)Math.Round(x / 32d);
            y = (int)Math.Round(y / 32d);
            return x >= y ? x + y : 32 - x - y;
        }

        static Instruction Assert(Instruction ins)
        {
            if (ins is Instruction.Failed)
                throw new AssembleException((ins as Instruction.Failed).Message);
            return ins;
        }

        public static byte[] Assemble(List<Statement> statements)
        {
            // compile into instructions
            Dictionary<ForLoop, AsmFor> formapping = new Dictionary<ForLoop, AsmFor>();
            List<Instruction> instructions = new List<Instruction>();
            for (int i = 0; i < statements.Count; i++)
            {
                var st = statements[i];
                Instruction ins = null;
                if (st is Empty || st is Print)
                    continue;
                else if (st is SerialPrint)
                {
                    uint mem = (st as SerialPrint).Mem ? 1u : 0;
                    uint value = (st as SerialPrint).Value;
                    instructions.Add(Assert(AsmSerialPrint.Create(mem, value)));
                    continue;
                }
                else if (st is KeyAction)
                {
                    var key = (st as KeyAction).Key;
                    uint keycode = (uint)key.KeyCode;
                    if (st is KeyPress)
                    {
                        uint duration = (uint)(st as KeyPress).Duration;
                        uint waittime = 0;
                        if (i + 1 < statements.Count && statements[i + 1] is Wait)
                            waittime = (uint)(statements[i + 1] as Wait).Duration;
                        ins = AsmKey_Compressed.Create(keycode, duration, waittime);
                        if (ins.Success)
                        {
                            i++;
                            instructions.Add(ins);
                            continue;
                        }
                        ins = AsmKey_Standard.Create(keycode, duration);
                        if (ins.Success)
                        {
                            instructions.Add(ins);
                            continue;
                        }
                        else if (ins == Instruction.Failed.OutOfRange)
                        {
                            instructions.Add(Assert(AsmKey_Hold.Create(keycode)));
                            instructions.Add(Assert(AsmWait.Create(duration)));
                            instructions.Add(Assert(AsmKey_Release.Create(keycode)));
                            continue;
                        }
                    }
                    else if (st is KeyDown)
                    {
                        instructions.Add(Assert(AsmKey_Hold.Create(keycode)));
                        continue;
                    }
                    else if (st is KeyUp)
                    {
                        instructions.Add(Assert(AsmKey_Release.Create(keycode)));
                        continue;
                    }
                }
                else if (st is StickAction)
                {
                    var key = (st as StickAction).Key;
                    uint keycode = (uint)key.KeyCode;
                    uint dindex = (uint)GetDirectionIndex((st as StickAction).Key);
                    if (st is StickPress)
                    {
                        uint duration = (uint)(st as StickPress).Duration;
                        ins = AsmStick_Standard.Create(keycode, dindex, duration);
                        if (ins.Success)
                        {
                            instructions.Add(ins);
                            continue;
                        }
                        else if (ins == Instruction.Failed.OutOfRange)
                        {
                            instructions.Add(Assert(AsmStick_Hold.Create(keycode, dindex)));
                            instructions.Add(Assert(AsmWait.Create(duration)));
                            instructions.Add(Assert(AsmStick_Reset.Create(keycode)));
                            continue;
                        }
                    }
                    else if (st is StickDown)
                    {
                        instructions.Add(Assert(AsmStick_Hold.Create(keycode, dindex)));
                        continue;
                    }
                    else if (st is StickUp)
                    {
                        instructions.Add(Assert(AsmStick_Reset.Create(keycode)));
                        continue;
                    }
                }
                else if (st is Wait)
                {
                    uint waittime = (uint)(st as Wait).Duration;
                    instructions.Add(Assert(AsmWait.Create(waittime)));
                    continue;
                }
                else if (st is ForLoop)
                {
                    uint loopnumber = 0;
                    if (st is ForLoopInfinite)
                        loopnumber = 0;
                    else if (st is ForLoopStatic)
                        loopnumber = (uint)(st as ForLoopStatic).Times;
                    ins = Assert(AsmFor.Create(loopnumber));
                    formapping[st as ForLoop] = ins as AsmFor;
                    instructions.Add(ins);
                    continue;
                }
                else if (st is Next)
                {
                    var @for = formapping[(st as Next).ForLoop];
                    ins = Assert(AsmNext.Create(@for));
                    @for.Next = ins as AsmNext;
                    instructions.Add(ins);
                    continue;
                }
                throw new NotImplementedException();
            }

            // calculate address and index
            uint addr = 2;
            uint index = 0;
            for (int i = 0; i < instructions.Count; i++)
            {
                var ins = instructions[i];
                ins.Address = addr;
                addr += ins.ByteCount;
                ins.Index = index;
                index += ins.InsCount;
            }

            // calculate key/stick release
            Dictionary<uint, List<Instruction>> keychain = new Dictionary<uint, List<Instruction>>();
            foreach (var ins in instructions)
            {
                if (ins is IAsmKey)
                {
                    var keycode = (ins as IAsmKey).KeyCode;
                    if (!keychain.ContainsKey(keycode))
                        keychain[keycode] = new List<Instruction>();
                    keychain[keycode].Add(ins);
                }
            }
            foreach (var list in keychain.Values)
                for (int i = 0; i < list.Count; i++)
                {
                    var ins = list[i] as IAsmHold;
                    if (ins != null)
                    {
                        uint offset = ((i + 1 < list.Count) ? list[i + 1].Index : index) - list[i].Index;
                        if (offset >= 1 << 7)
                            throw new AssembleException("松开按键间隔过远", i);
                        ins.HoldUntil = offset;
                    }
                }

            // generate bytecode
            using (var stream = new MemoryStream())
            {
                // preserved for length
                stream.WriteByte(0);
                stream.WriteByte(0);
                // write instructions
                foreach (var ins in instructions)
                    ins.WriteBytes(stream);
                // get array
                var bytes = stream.ToArray();
                // write length
                bytes[0] = (byte)(bytes.Length & 0xFF);
                bytes[1] = (byte)(bytes.Length >> 8);
                return bytes;
            }
        }

        public static string WriteHex(string hexStr, byte[] asmBytes)
        {
            if (asmBytes.Length > MaxBytes)
                throw new AssembleException("烧录失败！字节超出限制");
            const int DataLen = 16;
            List<byte> list = new List<byte>(asmBytes);
            // fill zeros
            while (list.Count < MaxBytes)
                list.Add(0);
            // load hex file
            var lines = hexStr.Split('\r', '\n');
            List<IntelHex> hexFile = new List<IntelHex>();
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
                    if ((i < 2 && hex.Data[i] != 0xFF) || (i > 2 && hex.Data[i] != 0))
                        return false;
                return true;
            });
            if (index == -1)
                throw new AssembleException("固件读取失败！未找到数据结构");
            int ver = hexFile[index].Data[2];
            if (ver < FirmwareVersion)
                throw new AssembleException("固件版本不符，请使用最新版的固件");
            ushort address = hexFile[index].StartAddress;
            // remove original data
            hexFile.RemoveRange(index, hexFile.Count - index);
            // write data from asmBytes
            for (int i = 0; i < list.Count; i += DataLen)
            {
                int len = Math.Min(DataLen, list.Count - i);
                byte[] data = list.GetRange(i, len).ToArray();
                hexFile.Add(new IntelHex(address, 0, data));
                address += (ushort)len;
            }
            // write eof
            hexFile.Add(new IntelHex(0, 1, new byte[0]));
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

        IntelHex()
        { }

        public IntelHex(ushort startAddress, byte recordType, byte[] data)
        {
            DataSize = (byte)data.Length;
            StartAddress = startAddress;
            RecordType = recordType;
            Data = data;
            FixChecksum();
        }

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
