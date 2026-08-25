using System.IO;
using System.Text;
using m2d;

namespace Polaris.Map.Internal
{
    /// <summary>TMAP 使用的大端基础类型和两种 UTF-8 长度前缀字符串。</summary>
    internal sealed class BigEndianWriter
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        readonly Stream stream;

        internal BigEndianWriter(Stream stream) => this.stream = stream;

        internal void Tag(Map2d.BIN_CTG tag) => Byte((byte)tag);
        internal void Byte(byte value) => stream.WriteByte(value);
        internal void Bytes(byte[] value) => stream.Write(value, 0, value.Length);
        internal void Short(short value) => UShort(unchecked((ushort)value));

        internal void UShort(ushort value)
        {
            Byte((byte)(value >> 8));
            Byte((byte)value);
        }

        internal void Int(int value) => UInt(unchecked((uint)value));

        internal void Float(float value)
        {
            byte[] bytes = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian)
            {
                System.Array.Reverse(bytes);
            }
            Bytes(bytes);
        }

        internal void UInt(uint value)
        {
            Byte((byte)(value >> 24));
            Byte((byte)(value >> 16));
            Byte((byte)(value >> 8));
            Byte((byte)value);
        }

        internal void Pascal(string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            Byte(checked((byte)bytes.Length));
            Bytes(bytes);
        }

        internal void String(string value)
        {
            byte[] bytes = Utf8.GetBytes(value);
            UShort(checked((ushort)bytes.Length));
            Bytes(bytes);
        }
    }
}
