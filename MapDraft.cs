using System.Collections.Generic;

namespace Polaris.Map
{
    /// <summary>尚未写盘的完整 TMAP v4 地图，宽高以地图格计。</summary>
    public sealed class MapDraft
    {
        readonly List<MapLayerDraft> layers = new();
        readonly List<string> cspKeys = new();
        readonly List<string> editorAdditional = new();
        readonly List<MapMeshRectDraft> meshRects = new();

        public MapDraft(string key, int width, int height)
        {
            Key = key;
            Width = width;
            Height = height;
        }

        public string Key { get; }
        public int Width { get; }
        public int Height { get; }
        public MapColor Background { get; set; } = MapColor.DefaultBackground;
        public string Comment { get; set; } = "";
        public byte CspExpectedCount { get; set; }
        public IList<string> CspKeys => cspKeys;
        public IList<string> EditorAdditional => editorAdditional;
        public IList<MapMeshRectDraft> MeshRects => meshRects;
        public IReadOnlyList<MapLayerDraft> Layers => layers;

        public MapLayerDraft AddLayer(string name, bool isKeyLayer = false)
        {
            var layer = new MapLayerDraft(name) { IsKeyLayer = isKeyLayer };
            layers.Add(layer);
            return layer;
        }

        public MapDraft AddMeshRect(byte index, float x, float y, float width, float height)
        {
            meshRects.Add(new MapMeshRectDraft(index, x, y, width, height));
            return this;
        }
    }

    public sealed class MapMeshRectDraft
    {
        public MapMeshRectDraft(byte index, float x, float y, float width, float height)
        {
            Index = index; X = x; Y = y; Width = width; Height = height;
        }

        public byte Index { get; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
    }

    /// <summary>新地图中的一个图层；列表顺序为底层到顶层。</summary>
    public sealed class MapLayerDraft
    {
        readonly List<MapElementDraft> elements = new();
        readonly List<MapLabelPointDraft> labelPoints = new();
        readonly List<MapGradationDraft> gradations = new();
        readonly List<MapSubMapDraft> subMaps = new();
        readonly List<MapJointDraft> joints = new();

        internal MapLayerDraft(string name) => Name = name;

        public string Name { get; }
        public string Comment { get; set; } = "";
        public bool IsKeyLayer { get; set; }
        public MapColor Color { get; set; } = MapColor.DefaultLayer;
        public IReadOnlyList<MapElementDraft> Elements => elements;
        public IReadOnlyList<MapLabelPointDraft> LabelPoints => labelPoints;
        public IReadOnlyList<MapGradationDraft> Gradations => gradations;
        public IReadOnlyList<MapSubMapDraft> SubMaps => subMaps;
        public IReadOnlyList<MapJointDraft> Joints => joints;

        public MapLayerDraft AddChip(string imageSource, float x, float y, int quarterTurns = 0,
            bool flip = false, int opacityPercent = 100, uint patternId = 0, string id = null)
        {
            AddChipElement(imageSource, x, y, quarterTurns, flip, opacityPercent, patternId, id);
            return this;
        }

        public MapElementDraft AddChipElement(string imageSource, float x, float y, int quarterTurns = 0,
            bool flip = false, int opacityPercent = 100, uint patternId = 0, string id = null)
        {
            var value = new MapElementDraft(MapElementKind.Chip, imageSource, x, y,
                quarterTurns, flip, opacityPercent, patternId, id);
            elements.Add(value);
            return value;
        }

        public MapLayerDraft AddPicture(string imageSource, float x, float y, int rotationDegrees = 0,
            bool flip = false, int opacityPercent = 100, uint patternId = 0, string id = null)
        {
            AddPictureElement(imageSource, x, y, rotationDegrees, flip, opacityPercent, patternId, id);
            return this;
        }

        public MapElementDraft AddPictureElement(string imageSource, float x, float y, int rotationDegrees = 0,
            bool flip = false, int opacityPercent = 100, uint patternId = 0, string id = null)
        {
            var value = new MapElementDraft(MapElementKind.Picture, imageSource, x, y,
                rotationDegrees, flip, opacityPercent, patternId, id);
            elements.Add(value);
            return value;
        }

        public MapLayerDraft AddLabelPoint(string key, float x, float y, float width, float height,
            float focusX = 0, float focusY = 0, string command = "", string comment = "")
        {
            labelPoints.Add(new MapLabelPointDraft(key, x, y, width, height)
            {
                FocusX = focusX, FocusY = focusY,
                Command = command ?? "", Comment = comment ?? "",
            });
            return this;
        }

        public MapLayerDraft AddGradation(MapGradationDraft gradation)
        {
            gradations.Add(gradation);
            return this;
        }

        public MapLayerDraft AddSubMap(MapSubMapDraft subMap)
        {
            subMaps.Add(subMap);
            return this;
        }

        public MapLayerDraft AddJoint(MapJointDraft joint)
        {
            joints.Add(joint);
            return this;
        }
    }

    public enum MapElementKind { Chip, Picture }

    public sealed class MapElementDraft
    {
        internal MapElementDraft(MapElementKind kind, string imageSource, float x, float y,
            int rotation, bool flip, int opacityPercent, uint patternId, string id)
        {
            Kind = kind; ImageSource = imageSource; X = x; Y = y; Rotation = rotation;
            Flip = flip; OpacityPercent = opacityPercent; PatternId = patternId; Id = id ?? "";
        }

        public MapElementKind Kind { get; }
        public string Id { get; }
        public string ImageSource { get; }
        public float X { get; }
        public float Y { get; }
        public int Rotation { get; }
        public bool Flip { get; }
        public int OpacityPercent { get; }
        public uint PatternId { get; }
    }

    public sealed class MapLabelPointDraft
    {
        public MapLabelPointDraft(string key, float x, float y, float width, float height)
        {
            Key = key; X = x; Y = y; Width = width; Height = height;
        }

        public string Key { get; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float FocusX { get; set; }
        public float FocusY { get; set; }
        public string Command { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    public enum MapGradationDirection : byte
    {
        Left = 0, Top = 1, Right = 2, Bottom = 3,
        LeftTop = 4, TopRight = 5, BottomLeft = 6, RightBottom = 7,
        Circle = 8, SliceLeftTop = 9, SliceTopRight = 10,
        SliceBottomLeft = 11, SliceRightBottom = 12, Slicer = 13,
    }

    public enum MapGradationOrder : byte
    {
        ChipBottom = 0, ChipGround = 1, ChipTop = 2,
        Sky = 3, Back = 4, Ground = 5, Top = 6,
    }

    public sealed class MapGradationDraft
    {
        readonly List<float> internalX = new();
        readonly List<float> internalY = new();
        readonly List<float> levels = new();

        public MapGradationDraft(string key, float x, float y, float width, float height)
        {
            Key = key; X = x; Y = y; Width = width; Height = height;
        }

        public string Key { get; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public MapGradationOrder Order { get; set; } = MapGradationOrder.Ground;
        public MapGradationDirection Direction { get; set; } = MapGradationDirection.Left;
        public MapColor StartColor { get; set; } = new(255, 255, 255, 255);
        public MapColor EndColor { get; set; } = new(255, 255, 255, 0);
        public byte SlicerColumns { get; set; }
        public byte SlicerRows { get; set; }
        public IList<float> InternalX => internalX;
        public IList<float> InternalY => internalY;
        public IList<float> Levels => levels;
    }

    public enum MapSubMapOrder : byte { Sky = 0, Back = 1, Ground = 2, Top = 3 }

    public sealed class MapSubMapDraft
    {
        public MapSubMapDraft(string targetMapKey) => TargetMapKey = targetMapKey;

        public string TargetMapKey { get; }
        public float X { get; set; }
        public float Y { get; set; }
        public float BaseX { get; set; }
        public float BaseY { get; set; }
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public MapSubMapOrder Order { get; set; } = MapSubMapOrder.Ground;
        public byte RepeatX { get; set; }
        public byte RepeatY { get; set; }
        public float IntervalX { get; set; }
        public float IntervalY { get; set; }
        public float CameraLength { get; set; }
    }

    public sealed class MapJointDraft
    {
        readonly List<MapJointPointDraft> points = new();

        public MapJointDraft(float centerX, float centerY)
        {
            CenterX = centerX; CenterY = centerY;
        }

        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public MapColor Color { get; set; } = new(255, 255, 255, 255);
        public byte Thickness { get; set; } = 1;
        public IList<MapJointPointDraft> Points => points;

        public MapJointDraft AddPoint(float x, float y, string chipId = null)
        {
            points.Add(new MapJointPointDraft(x, y, chipId));
            return this;
        }
    }

    public sealed class MapJointPointDraft
    {
        public MapJointPointDraft(float x, float y, string chipId = null)
        {
            X = x; Y = y; ChipId = chipId ?? "";
        }

        public float X { get; }
        public float Y { get; }
        public string ChipId { get; }
    }
}
