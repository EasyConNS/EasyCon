using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    class Shadow
    {
        const string ShadowPath = ImageRes.ImagePath + @"Shadow.bin";
        public const int X = 220;
        public const int Y = 232;
        public const int PW = 8;
        public const int PH = 8;
        public const int PX = 64;
        public const int PY = 64;
        public const int W = PW * PX;
        public const int H = PH * PY;
        public const int BytesLength = PX * PY / 8;
        public const int MD5Length = 16;

        static readonly Dictionary<string, Shadow> _dict = new Dictionary<string, Shadow>();
        static bool _prepared = false;

        public readonly int ID;
        public readonly char Variance;
        public readonly string Hash;
        public readonly byte[] HashBytes;
        public readonly Bitmap Image;

        public string Name { get { return $"#{ID}{(Variance != '\0' ? $"-{Variance}" : string.Empty)}"; } }

        public Shadow(Bitmap image)
        {
            // temporary
            ID = -1;
            Variance = '\0';
            HashBytes = GetHashBytes(image);
            Hash = BytesToString(HashBytes);
            Image = image;
        }

        public Shadow(int id, char v, byte[] bytes)
        {
            // loaded
            ID = id;
            Variance = v;
            HashBytes = bytes;
            Hash = BytesToString(HashBytes);
            Image = null;
        }

        public Shadow(int id, char v, Bitmap image)
        {
            // new
            ID = id;
            Variance = v;
            HashBytes = GetHashBytes(image);
            Hash = BytesToString(HashBytes);
            Image = image;
        }

        byte[] GetHashBytes(Bitmap image)
        {
            int[,] data = new int[PX, PY];
            for (int x = 0; x < Shadow.PX; x++)
                for (int y = 0; y < Shadow.PY; y++)
                    data[x, y] = image.GetPixel(x * Shadow.PW, y * Shadow.PH).Compare(Color.FromArgb(0, 0, 0)) > 0.95 ? 1 : 0;
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(DataToBytes(data));
            }
        }

        string BytesToString(byte[] bytes)
        {
            return string.Join(string.Empty, bytes.Select(b => b.ToString("X2")));
        }

        public static byte[] DataToBytes(int[,] data)
        {
            List<byte> bytes = new List<byte>(BytesLength);
            byte b = 0;
            int c = 0;
            for (int i = 0; i < data.GetLength(0); i++)
                for (int j = 0; j < data.GetLength(1); j++)
                {
                    b = (byte)(b | (data[i, j] << c));
                    c++;
                    if (c >= 8)
                    {
                        bytes.Add(b);
                        b = 0;
                        c = 0;
                    }
                }
            return bytes.ToArray();
        }

        public static int[,] BytesToData(byte[] bytes)
        {
            int[,] data = new int[PX, PY];
            int c = 0;
            int index = 0;
            for (int i = 0; i < data.GetLength(0); i++)
                for (int j = 0; j < data.GetLength(1); j++)
                {
                    data[i, j] = bytes[index] & 1;
                    bytes[index] = (byte)(bytes[index] >> 1);
                    c++;
                    if (c >= 8)
                    {
                        index++;
                        c = 0;
                    }
                }
            return data;
        }

        public static void Load()
        {
            if (_prepared)
                return;
            _prepared = true;
            if (!File.Exists(ShadowPath))
                return;
            using (var fs = new FileStream(ShadowPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    try
                    {
                        while (true)
                        {
                            int id = reader.ReadInt32();
                            char v = reader.ReadChar();
                            byte[] bytes = reader.ReadBytes(MD5Length);
                            Shadow shadow = new Shadow(id, v, bytes);
                            _dict[shadow.Hash] = shadow;
                        }
                    }
                    catch
                    { }
                }
            }
        }

        public static void Save()
        {
            Load();
            using (var fs = new FileStream(ShadowPath, FileMode.Create))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    foreach (var shadow in _dict.Values)
                    {
                        writer.Write(shadow.ID);
                        writer.Write(shadow.Variance);
                        writer.Write(shadow.HashBytes);
                    }
                }
            }
        }

        public static Shadow Save(int id, char v, Bitmap image)
        {
            Load();
            Shadow shadow = new Shadow(id, v, image);
            _dict[shadow.Hash] = shadow;
            Save();
            return shadow;
        }

        public static Shadow Find(string hash)
        {
            Load();
            if (_dict.ContainsKey(hash))
                return _dict[hash];
            return null;
        }

        public static Shadow Find(Shadow shadow)
        {
            return Find(shadow.Hash);
        }
    }
}
