using Polaris.Map.Authoring;

namespace AicTmapToPmap;

internal sealed class TmapConverter
{
    private const float Cell = 28f;
    private readonly BinaryReaderBE _reader;
    private readonly ChipCatalog _catalog;
    private readonly DiagnosticSink _diagnostics;
    private readonly PmapDocument _document = new();
    private readonly List<ImportedLayer> _diskLayers = new();
    private List<ImportedLayer>? _layers;
    private ImportedLayer? _currentLayer;
    private bool _reverseLayers;
    private int? _declaredLayerCount;
    private uint _pattern;

    internal TmapConverter(string path, ChipCatalog catalog, DiagnosticSink diagnostics)
    {
        _reader = new BinaryReaderBE(File.ReadAllBytes(path), path);
        _catalog = catalog;
        _diagnostics = diagnostics;
        _document.Key = Path.GetFileNameWithoutExtension(path);
    }

    internal PmapDocument Convert()
    {
        byte version = _reader.Byte();
        if (version != 4)
            throw _reader.Error($"unsupported TMAP version {version}; expected version 4");

        while (_reader.Remaining > 0)
        {
            int tagOffset = _reader.Position;
            byte tag = _reader.Byte();
            switch (tag)
            {
                case 1: _document.Key = _reader.Pascal(); break;
                case 2: ReadSize(); break;
                case 3: _document.Background = Rgb(_reader.Byte(), _reader.Byte(), _reader.Byte()); break;
                case 4: _document.Comment = _reader.String(); break;
                case 5: ReadCsp(); break;
                case 6: ReadEditorAdditional(); break;
                case 7: ReadMeshRect(); break;
                case 8: ReadLayerReverse(); break;
                case 9: SkipImageDirectories(); break;
                case 80: ReadLayer(hasCapacities: false); break;
                case 90: ReadLayer(hasCapacities: true); break;
                case 87: ReadLayerContent(); break;
                case 89: ReadJointCount(); break;
                case 88: ReadJoint(); break;
                case 81: RequireLayer(tagOffset); _pattern = _reader.UInt32(); break;
                case 82: RequireLayer(tagOffset); ReadPuts(PmapElementKind.Chip, tagOffset); break;
                case 83: RequireLayer(tagOffset); ReadPuts(PmapElementKind.Picture, tagOffset); break;
                case 84: RequireLayer(tagOffset); ReadLabelPoint(); break;
                case 85: RequireLayer(tagOffset); ReadGradation(); break;
                case 86: RequireLayer(tagOffset); ReadSubMap(); break;
                default: throw _reader.Error($"unknown TMAP tag {tag} (tag starts @0x{tagOffset:X})");
            }
        }

        FinalizeLayers();
        if (_document.Width <= 0 || _document.Height <= 0)
            throw _reader.Error("TMAP does not contain a valid SIZE block");
        if (_document.Layers.Count == 0)
            throw _reader.Error("TMAP does not contain any layers");

        int keyLayers = _document.Layers.Count(x => x.IsKeyLayer);
        if (keyLayers == 0)
        {
            _document.Layers[0].IsKeyLayer = true;
            _diagnostics.Warning("TMAP2001", _reader.Source, -1,
                "the original map has no key layer; the bottom layer was selected so the PMAP remains editable");
        }
        else if (keyLayers > 1)
        {
            bool keep = true;
            foreach (PmapLayer layer in _document.Layers.Where(x => x.IsKeyLayer))
            {
                if (keep) { keep = false; continue; }
                layer.IsKeyLayer = false;
            }
            _diagnostics.Warning("TMAP2002", _reader.Source, -1,
                "the original map has multiple key layers; only the bottom-most key layer was retained");
        }
        return _document;
    }

    private void ReadSize()
    {
        int widthPixels = _reader.UInt16();
        int heightPixels = _reader.UInt16();
        if (widthPixels % 28 != 0 || heightPixels % 28 != 0)
            throw _reader.Error($"SIZE {widthPixels}x{heightPixels} is not aligned to the 28-pixel map grid");
        _document.Width = widthPixels / 28;
        _document.Height = heightPixels / 28;
    }

    private void ReadCsp()
    {
        _document.CspExpectedCount = _reader.Byte();
        int count = _reader.Byte();
        for (int i = 0; i < count; i++) _document.CspKeys.Add(_reader.Pascal());
    }

    private void ReadEditorAdditional()
    {
        int count = _reader.Byte();
        for (int i = 0; i < count; i++) _document.EditorAdditional.Add(_reader.Pascal());
    }

    private void ReadMeshRect()
    {
        _document.MeshRects.Add(new PmapMeshRect
        {
            Index = _reader.Byte(),
            X = _reader.Float(),
            Y = _reader.Float(),
            Width = _reader.Float(),
            Height = _reader.Float(),
        });
    }

    private void ReadLayerReverse()
    {
        if (_diskLayers.Count != 0)
            throw _reader.Error("LAYER_REVERSE must appear before layer headers");
        _reverseLayers = true;
        int count = _reader.Int32();
        if (count < 0 || count > byte.MaxValue)
            throw _reader.Error($"invalid layer count {count}");
        _declaredLayerCount = count;
    }

    private void SkipImageDirectories()
    {
        int count = _reader.Byte();
        for (int i = 0; i < count; i++) _reader.Pascal();
    }

    private void ReadLayer(bool hasCapacities)
    {
        if (_layers != null)
            throw _reader.Error("a layer header appeared after JOINT data");
        var layer = new PmapLayer
        {
            Name = _reader.Pascal(),
            Comment = _reader.String(),
            IsKeyLayer = _reader.Byte() != 0,
            Color = Argb(_reader.UInt32()),
        };
        if (hasCapacities)
        {
            _reader.UInt32();
            _reader.Byte();
            _reader.Byte();
        }
        _currentLayer = new ImportedLayer(layer, _diskLayers.Count);
        _diskLayers.Add(_currentLayer);
        _pattern = 0;
    }

    private void ReadLayerContent()
    {
        RequireLayer(_reader.Position - 1);
        uint byteLength = _reader.UInt32();
        long endLong = (long)_reader.Position + byteLength;
        if (endLong > _reader.Length)
            throw _reader.Error($"LAY_CHIPS_CONTENT length {byteLength} exceeds the file");
        int end = (int)endLong;
        while (_reader.Position < end)
        {
            int tagOffset = _reader.Position;
            byte tag = _reader.Byte();
            switch (tag)
            {
                case 81: _pattern = _reader.UInt32(); break;
                case 82: ReadPuts(PmapElementKind.Chip, tagOffset); break;
                case 83: ReadPuts(PmapElementKind.Picture, tagOffset); break;
                case 84: ReadLabelPoint(); break;
                case 85: ReadGradation(); break;
                case 86: ReadSubMap(); break;
                default: throw _reader.Error($"unexpected layer-content tag {tag} (tag starts @0x{tagOffset:X})");
            }
            if (_reader.Position > end)
                throw _reader.Error($"layer content overruns its declared endpoint 0x{end:X}");
        }
        if (_reader.Position != end)
            throw _reader.Error($"layer content ended at 0x{_reader.Position:X}, expected 0x{end:X}");
    }

    private void ReadPuts(PmapElementKind kind, int tagOffset)
    {
        ImportedLayer layer = _currentLayer!;
        uint imageId = _reader.UInt32();
        short drawX = _reader.Int16();
        short drawY = _reader.Int16();
        int opacityFlip = _reader.Byte();
        int rotation = _reader.Int16();
        bool flip = opacityFlip > 100;
        int opacity = flip ? opacityFlip - 100 : opacityFlip;
        if (opacity is < 0 or > 100)
            throw _reader.Error($"invalid opacity/flip byte {opacityFlip}");

        ChipImage image;
        try { image = _catalog.Get(imageId); }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidDataException($"{_reader.Source} @0x{tagOffset:X}: {ex.Message}", ex);
        }

        var element = new PmapElement
        {
            Kind = kind,
            Id = $"put_d{layer.DiskIndex}_{layer.Puts.Count}",
            // `#imageId` 让 PolarisMap 可精确取回嵌套 M2ChipImage；前半段保留可读 PixelLiner 源路径。
            Image = image.Source + "#" + imageId,
            Rotation = rotation,
            Flip = flip,
            Opacity = opacity,
            PatternId = _pattern,
            VisualWidth = Math.Max(image.PixelWidth / Cell, 1f / Cell),
            VisualHeight = Math.Max(image.PixelHeight / Cell, 1f / Cell),
            Color = kind == PmapElementKind.Chip ? "#5B6477" : "#B68A5A",
            Label = Path.GetFileNameWithoutExtension(image.Source),
        };
        if (kind == PmapElementKind.Chip)
        {
            int normalizedRotation = Mod(rotation, 4);
            bool sideways = normalizedRotation is 1 or 3;
            int width = sideways ? image.PixelHeight : image.PixelWidth;
            int height = sideways ? image.PixelWidth : image.PixelHeight;
            (int shiftX, int shiftY) = image.GetShift(normalizedRotation, flip);
            (int rotatedX, int rotatedY) = image.GetRotatedGridSize(normalizedRotation);
            int offsetX = shiftX < 0 ? -(rotatedX - width + shiftX) : shiftX;
            int offsetY = shiftY < 0 ? -(rotatedY - height + shiftY) : shiftY;
            int logicalX = drawX - offsetX;
            int logicalY = drawY - offsetY;
            element.X = logicalX / Cell;
            element.Y = logicalY / Cell;
            element.Rotation = normalizedRotation;
        }
        else
        {
            double radians = rotation / 180.0 * Math.PI;
            double cosine = Math.Abs(Math.Cos(radians));
            double sine = Math.Abs(Math.Sin(radians));
            double width = image.PixelWidth * cosine + image.PixelHeight * sine;
            double height = image.PixelHeight * cosine + image.PixelWidth * sine;
            element.X = (float)((drawX + width * 0.5) / Cell);
            element.Y = (float)((drawY + height * 0.5) / Cell);
        }
        layer.Puts.Add(element);
        layer.Layer.Elements.Add(element);
    }

    private void ReadLabelPoint()
    {
        _currentLayer!.Layer.Elements.Add(new PmapElement
        {
            Kind = PmapElementKind.LabelPoint,
            Key = _reader.Pascal(),
            X = _reader.Int16() / Cell,
            Y = _reader.Int16() / Cell,
            Width = _reader.Int16() / Cell,
            Height = _reader.Int16() / Cell,
            FocusX = _reader.Float(),
            FocusY = _reader.Float(),
            Command = _reader.String(),
            Comment = _reader.String(),
            VisualWidth = 1,
            VisualHeight = 1,
            Color = "#4B9B77",
        });
        PmapElement value = _currentLayer.Layer.Elements[^1];
        value.VisualWidth = Math.Max(value.Width, 1f / Cell);
        value.VisualHeight = Math.Max(value.Height, 1f / Cell);
        value.Label = value.Key;
    }

    private void ReadGradation()
    {
        var value = new PmapElement
        {
            Kind = PmapElementKind.Gradation,
            Key = _reader.Pascal(),
            X = _reader.Int16() / Cell,
            Y = _reader.Int16() / Cell,
            Width = _reader.Int16() / Cell,
            Height = _reader.Int16() / Cell,
            Order = _reader.Byte(),
            Direction = _reader.Byte(),
            StartColor = Rgba(_reader.UInt32()),
            EndColor = Rgba(_reader.UInt32()),
            Color = "#8874B8AA",
        };
        if (value.Direction == 13)
        {
            value.SlicerColumns = _reader.Byte();
            if (value.SlicerColumns > 0)
            {
                value.SlicerRows = _reader.Byte();
                int xCount = Math.Max(value.SlicerColumns, 2) - 2;
                int yCount = Math.Max(value.SlicerRows, 2) - 2;
                int levelCount = Math.Max(value.SlicerColumns, 2) * Math.Max(value.SlicerRows, 2);
                for (int i = 0; i < xCount; i++) value.InternalX.Add(_reader.Float());
                for (int i = 0; i < yCount; i++) value.InternalY.Add(_reader.Float());
                for (int i = 0; i < levelCount; i++) value.Levels.Add(_reader.Float());
            }
        }
        value.VisualWidth = Math.Max(value.Width, 1f / Cell);
        value.VisualHeight = Math.Max(value.Height, 1f / Cell);
        value.Label = value.Key;
        _currentLayer!.Layer.Elements.Add(value);
    }

    private void ReadSubMap()
    {
        var value = new PmapElement
        {
            Kind = PmapElementKind.SubMap,
            TargetMap = _reader.Pascal(),
            X = _reader.Float(),
            Y = _reader.Float(),
            BaseX = _reader.Float(),
            BaseY = _reader.Float(),
            ScaleX = _reader.Float(),
            ScaleY = _reader.Float(),
            ScrollX = _reader.Float(),
            ScrollY = _reader.Float(),
            Order = _reader.Byte(),
            RepeatX = _reader.Byte(),
            RepeatY = _reader.Byte(),
            IntervalX = _reader.Float(),
            IntervalY = _reader.Float(),
            CameraLength = _reader.Float(),
            VisualWidth = 4,
            VisualHeight = 3,
            Color = "#4A83A8",
        };
        value.Label = value.TargetMap;
        _currentLayer!.Layer.Elements.Add(value);
    }

    private void ReadJointCount()
    {
        FinalizeLayers();
        int layerIndex = _reader.Byte();
        _reader.UInt16();
        RequireRuntimeLayer(layerIndex);
    }

    private void ReadJoint()
    {
        FinalizeLayers();
        int ownerIndex = _reader.Byte();
        ImportedLayer owner = RequireRuntimeLayer(ownerIndex);
        int pointCount = _reader.Byte();
        var value = new PmapElement
        {
            Kind = PmapElementKind.Joint,
            X = _reader.Float(),
            Y = _reader.Float(),
            Color = Rgba(_reader.UInt32()),
            Thickness = _reader.Byte(),
            VisualWidth = 1,
            VisualHeight = 1,
            Label = "Joint",
        };
        float minX = value.X, maxX = value.X, minY = value.Y, maxY = value.Y;
        for (int i = 0; i < pointCount; i++)
        {
            var point = new PmapJointPoint { X = _reader.Float(), Y = _reader.Float() };
            int putIndex = _reader.Int32();
            if (putIndex >= 0)
            {
                int layerIndex = _reader.Byte();
                ImportedLayer target = RequireRuntimeLayer(layerIndex);
                if (putIndex >= target.Puts.Count)
                    throw _reader.Error($"JOINT references put {putIndex} in layer {layerIndex}, which has only {target.Puts.Count} puts");
                point.ChipId = target.Puts[putIndex].Id;
            }
            value.Points.Add(point);
            minX = Math.Min(minX, value.X + point.X);
            maxX = Math.Max(maxX, value.X + point.X);
            minY = Math.Min(minY, value.Y + point.Y);
            maxY = Math.Max(maxY, value.Y + point.Y);
        }
        value.VisualWidth = Math.Max(maxX - minX, 1f / Cell);
        value.VisualHeight = Math.Max(maxY - minY, 1f / Cell);
        owner.Layer.Elements.Add(value);
    }

    private void FinalizeLayers()
    {
        if (_layers != null) return;
        if (_declaredLayerCount.HasValue && _declaredLayerCount.Value != _diskLayers.Count)
            throw _reader.Error($"LAYER_REVERSE declares {_declaredLayerCount.Value} layers, but {_diskLayers.Count} headers were read");
        _layers = _reverseLayers ? _diskLayers.AsEnumerable().Reverse().ToList() : _diskLayers.ToList();
        foreach (ImportedLayer layer in _layers) _document.Layers.Add(layer.Layer);
    }

    private void RequireLayer(int tagOffset)
    {
        if (_currentLayer == null)
            throw new InvalidDataException($"{_reader.Source} @0x{tagOffset:X}: layer content appears before a layer header");
    }

    private ImportedLayer RequireRuntimeLayer(int index)
    {
        if (_layers == null || index < 0 || index >= _layers.Count)
            throw _reader.Error($"invalid runtime layer index {index}");
        return _layers[index];
    }

    private static string Rgb(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
    private static string Rgba(uint value)
        => $"#{(byte)(value >> 24):X2}{(byte)(value >> 16):X2}{(byte)(value >> 8):X2}{(byte)value:X2}";
    private static string Argb(uint value)
        => $"#{(byte)(value >> 16):X2}{(byte)(value >> 8):X2}{(byte)value:X2}{(byte)(value >> 24):X2}";
    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private sealed class ImportedLayer
    {
        internal ImportedLayer(PmapLayer layer, int diskIndex) { Layer = layer; DiskIndex = diskIndex; }
        internal PmapLayer Layer { get; }
        internal int DiskIndex { get; }
        internal List<PmapElement> Puts { get; } = new();
    }
}
