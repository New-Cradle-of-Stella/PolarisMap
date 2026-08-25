using System.Buffers.Binary;
using System.Text;

namespace AicTmapToPmap;

internal sealed class BinaryReaderBE
{
    private readonly byte[] _data;

    internal BinaryReaderBE(byte[] data, string source)
    {
        _data = data;
        Source = source;
    }

    internal string Source { get; }
    internal int Position { get; set; }
    internal int Length => _data.Length;
    internal int Remaining => Length - Position;

    internal byte Byte()
    {
        Require(1);
        return _data[Position++];
    }

    internal bool Bool() => Byte() != 0;

    internal short Int16()
    {
        Require(2);
        short value = BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(Position, 2));
        Position += 2;
        return value;
    }

    internal ushort UInt16()
    {
        Require(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(Position, 2));
        Position += 2;
        return value;
    }

    internal int Int32()
    {
        Require(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    internal uint UInt32()
    {
        Require(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(Position, 4));
        Position += 4;
        return value;
    }

    internal float Float()
        => BitConverter.Int32BitsToSingle(Int32());

    internal string Pascal()
        => Utf8(Byte());

    internal string String()
        => Utf8(UInt16());

    internal void Skip(int count)
    {
        Require(count);
        Position += count;
    }

    internal void Seek(int position)
    {
        if (position < 0 || position > Length)
            throw Error($"seek target {position} is outside the file");
        Position = position;
    }

    internal InvalidDataException Error(string message)
        => new($"{Source} @0x{Position:X}: {message}");

    private string Utf8(int count)
    {
        Require(count);
        string value;
        try
        {
            value = new UTF8Encoding(false, true).GetString(_data, Position, count);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"{Source} @0x{Position:X}: invalid UTF-8 string", ex);
        }
        Position += count;
        return value;
    }

    private void Require(int count)
    {
        if (count < 0 || Position > Length - count)
            throw Error($"unexpected end of file (need {count} byte(s), have {Remaining})");
    }
}
