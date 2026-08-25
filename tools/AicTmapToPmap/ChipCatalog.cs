namespace AicTmapToPmap;

internal sealed record ChipImage(
    uint Id,
    string Source,
    int PixelWidth,
    int PixelHeight,
    int ShiftX,
    int ShiftY,
    int Columns,
    int Rows)
{
    internal (int X, int Y) GetShift(int rotation, bool flip)
    {
        int sign = flip ? -1 : 1;
        int sx;
        int sy;
        switch (Mod(rotation, 4))
        {
            case 1: sx = -1; sy = sign; break;
            case 2: sx = -sign; sy = -1; break;
            case 3: sx = 1; sy = -sign; break;
            default: sx = sign; sy = 1; break;
        }

        bool sideways = Mod(rotation, 4) is 1 or 3;
        int gridW = (sideways ? Rows : Columns) * 28;
        int gridH = (sideways ? Columns : Rows) * 28;
        int imageW = sideways ? PixelHeight : PixelWidth;
        int imageH = sideways ? PixelWidth : PixelHeight;
        int shiftW = sideways ? ShiftY : ShiftX;
        int shiftH = sideways ? ShiftX : ShiftY;
        return (sx < 0 ? gridW - imageW - shiftW : shiftW,
            sy < 0 ? gridH - imageH - shiftH : shiftH);
    }

    internal (int X, int Y) GetRotatedGridSize(int rotation)
        => Mod(rotation, 4) is 1 or 3 ? (Rows * 28, Columns * 28) : (Columns * 28, Rows * 28);

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

internal sealed class ChipCatalog
{
    private readonly Dictionary<uint, ChipImage> _images = new();

    internal int Count => _images.Count;

    internal ChipImage Get(uint id)
        => _images.TryGetValue(id, out ChipImage? image)
            ? image
            : throw new KeyNotFoundException($"imageId {id} is not a concrete M2ChipImage in __m2d_chips.dat");

    internal static ChipCatalog Load(string path)
    {
        var reader = new BinaryReaderBE(File.ReadAllBytes(path), path);
        byte version = reader.Byte();
        if (version < 11 || version > 12)
            throw reader.Error($"unsupported __m2d_chips.dat version {version}; this converter supports versions 11 and 12");

        reader.UInt32(); // image id array capacity
        reader.UInt32(); // serialized entry count
        int familyCount = reader.UInt16();
        for (int i = 0; i < familyCount; i++) reader.Pascal();

        int directoryCount = reader.UInt16();
        var directories = new List<DirectoryBlock>(directoryCount);
        for (int i = 0; i < directoryCount; i++)
        {
            string name = reader.Pascal();
            reader.UInt32(); // dictionary capacity
            uint position = reader.UInt32();
            uint length = reader.UInt32();
            directories.Add(new DirectoryBlock(name, position, length));
        }

        var catalog = new ChipCatalog();
        foreach (DirectoryBlock directory in directories)
            catalog.ReadDirectory(reader, version, directory);
        return catalog;
    }

    private void ReadDirectory(BinaryReaderBE reader, byte version, DirectoryBlock directory)
    {
        if (directory.Length == 0) return;
        long endLong = (long)directory.Position + directory.Length;
        if (directory.Position > int.MaxValue || endLong > reader.Length)
            throw reader.Error($"chip directory '{directory.Name}' has an invalid block range");
        int end = (int)endLong;
        reader.Seek((int)directory.Position);
        while (reader.Position < end)
        {
            int recordStart = reader.Position;
            byte type = reader.Byte();
            switch (type)
            {
                case 0: break;
                case 2: ReadConcreteImage(reader, version, directory.Name); break;
                case 3: SkipPattern(reader, version); break;
                case 4: SkipSmartImage(reader, version); break;
                case 5: SkipStamp(reader, builtIn: false); break;
                case 6: ReadNestedImage(reader, version, directory.Name); break;
                case 7: SkipStamp(reader, builtIn: true); break;
                default: throw reader.Error($"unknown chip record type {type} in directory '{directory.Name}' (record starts @0x{recordStart:X})");
            }
            if (reader.Position > end)
                throw reader.Error($"chip record in directory '{directory.Name}' overruns its block");
        }
        if (reader.Position != end)
            throw reader.Error($"chip directory '{directory.Name}' ended at 0x{reader.Position:X}, expected 0x{end:X}");
    }

    private void ReadConcreteImage(BinaryReaderBE reader, byte version, string directory)
    {
        string basename = reader.Pascal();
        int width = reader.UInt16();
        int height = reader.UInt16();
        int shiftX = reader.Byte();
        int shiftY = reader.Byte();
        reader.Int16(); // horizon
        uint id = reader.UInt32();
        int configLength = reader.UInt16();
        reader.Skip(configLength);
        reader.UInt16(); // family
        if (version <= 8) reader.UInt16(); else reader.UInt32();
        reader.UInt16(); // meta index
        reader.Byte(); // additional mesh count
        Add(new ChipImage(id, directory + basename + ".png", width, height, shiftX, shiftY,
            CeilDiv(width + shiftX, 28), CeilDiv(height + shiftY, 28)));
    }

    private void ReadNestedImage(BinaryReaderBE reader, byte version, string directory)
    {
        string basename = reader.Pascal();
        int sourceLeft = unchecked((sbyte)reader.Byte());
        int sourceTop = unchecked((sbyte)reader.Byte());
        int packed = reader.Byte();
        int itemColumns = (packed >> 4) & 15;
        int itemRows = packed & 15;
        int sourceWidth = reader.UInt16();
        int sourceHeight = reader.UInt16();
        int spliceX = reader.Byte();
        int spliceY = reader.Byte();
        reader.Byte(); // config base x
        reader.Byte(); // config base y
        reader.Byte(); // left config + flags
        reader.Byte(); // right config

        int nestedColumns = CeilDiv(CeilDiv(sourceLeft + sourceWidth, 28) - FloorDiv(sourceLeft, 28), itemColumns);
        int nestedRows = CeilDiv(CeilDiv(sourceTop + sourceHeight, 28) - FloorDiv(sourceTop, 28), itemRows);
        int childCount = checked(nestedColumns * nestedRows);
        uint parentId = reader.UInt32();
        var preloadIds = new uint[childCount + 1];
        for (int i = 0; i < preloadIds.Length; i++) preloadIds[i] = reader.UInt32();
        reader.UInt16(); // family
        if (version <= 8) reader.UInt16(); else reader.UInt32();
        reader.UInt16(); // meta index

        bool assignedParent = false;
        for (int index = 0; index < childCount; index++)
        {
            if (preloadIds[index] == 0) continue;
            GetNestedPosition(index, nestedColumns, nestedRows, spliceX, spliceY, out int x, out int y);
            int shiftX = x == 0 ? sourceLeft : 0;
            int sourceX = Math.Max(-sourceLeft + x * 28 * itemColumns, 0);
            int width = Math.Min(28 * itemColumns - shiftX, sourceWidth - sourceX);
            int shiftY = y == 0 ? sourceTop : 0;
            int sourceY = Math.Max(-sourceTop + y * 28 * itemRows, 0);
            int height = Math.Min(28 * itemRows - shiftY, sourceHeight - sourceY);
            uint id = assignedParent ? preloadIds[index] : parentId;
            string source = assignedParent ? directory + basename + "." + index + ".png" : directory + basename + ".png";
            assignedParent = true;
            Add(new ChipImage(id, source, width, height, shiftX, shiftY,
                CeilDiv(width + shiftX, 28), CeilDiv(height + shiftY, 28)));
        }
    }

    private static void GetNestedPosition(int index, int columns, int rows, int spliceX, int spliceY, out int x, out int y)
    {
        int rightColumns = columns - spliceX;
        int bottomRows = rows - spliceY;
        if (index < rightColumns * bottomRows)
        {
            x = spliceX + index % rightColumns;
            y = spliceY + index / rightColumns;
            return;
        }
        index -= rightColumns * bottomRows;
        if (index < rightColumns * spliceY)
        {
            x = spliceX + index % rightColumns;
            y = spliceY - 1 - index / rightColumns;
            return;
        }
        index -= rightColumns * spliceY;
        if (index < spliceX * bottomRows)
        {
            x = spliceX - 1 - index % spliceX;
            y = spliceY + index / spliceX;
            return;
        }
        index -= spliceX * bottomRows;
        x = spliceX - 1 - index % spliceX;
        y = spliceY - 1 - index / spliceX;
    }

    private static void SkipPattern(BinaryReaderBE reader, byte version)
    {
        reader.Pascal(); reader.Byte(); reader.UInt32();
        reader.Byte(); reader.Byte();
        if (version >= 5) reader.Byte();
        reader.UInt16();
        int count = reader.UInt16();
        reader.Skip(checked(count * 5));
        if (version < 3) reader.Pascal();
        else { reader.UInt32(); if (version >= 8) reader.Byte(); }
    }

    private static void SkipSmartImage(BinaryReaderBE reader, byte version)
    {
        reader.Pascal(); reader.Byte(); reader.UInt32(); reader.Byte(); reader.Byte();
        int pieceCount = reader.Byte();
        for (int i = 0; i < pieceCount; i++)
        {
            int itemCount = reader.Byte();
            reader.Skip(checked(itemCount * 5));
        }
        reader.Byte(); reader.UInt16();
        if (version < 3) reader.Pascal();
        else
        {
            reader.UInt32();
            if (version >= 12)
            {
                int names = reader.Byte();
                for (int i = 0; i < names; i++) reader.Pascal();
            }
        }
    }

    private static void SkipStamp(BinaryReaderBE reader, bool builtIn)
    {
        reader.Pascal(); reader.Byte(); reader.UInt32(); reader.Byte(); reader.Byte();
        int itemCount = reader.Byte();
        reader.Skip(checked(itemCount * 21)); // u32 + 3*f32 + bool + u32
        reader.UInt16(); reader.Byte(); reader.Byte();
        if (builtIn) reader.Skip(8);
    }

    private void Add(ChipImage image)
    {
        if (image.Id == 0) return;
        _images[image.Id] = image;
    }

    private static int CeilDiv(int value, int divisor)
        => (int)Math.Ceiling(value / (double)divisor);

    private static int FloorDiv(int value, int divisor)
        => (int)Math.Floor(value / (double)divisor);

    private sealed record DirectoryBlock(string Name, uint Position, uint Length);
}
