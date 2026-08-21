using System;
using System.Collections.Generic;

namespace Polaris.Map
{
    /// <summary>尚未写盘的 TMAP v4 地图，宽高以地图格计。</summary>
    public sealed class MapDraft
    {
        readonly List<MapLayerDraft> layers = new();

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

        /// <summary>地图 META 原文。</summary>
        public string Comment { get; set; } = "";

        public IReadOnlyList<MapLayerDraft> Layers => layers;

        /// <summary>添加图层；列表顺序为底层到顶层。</summary>
        public MapLayerDraft AddLayer(string name, bool isKeyLayer = false)
        {
            var layer = new MapLayerDraft(name) { IsKeyLayer = isKeyLayer };
            layers.Add(layer);
            return layer;
        }
    }

    /// <summary>新地图中的一个图层。</summary>
    public sealed class MapLayerDraft
    {
        readonly List<MapElementDraft> elements = new();

        internal MapLayerDraft(string name) => Name = name;

        public string Name { get; }
        public string Comment { get; set; } = "";
        public bool IsKeyLayer { get; set; }
        public MapColor Color { get; set; } = MapColor.DefaultLayer;
        public IReadOnlyList<MapElementDraft> Elements => elements;

        /// <summary>添加 Chip；坐标以地图格计，旋转以顺时针四分之一圈计。</summary>
        public MapLayerDraft AddChip(
            string imageSource, int x, int y, int quarterTurns = 0, bool flip = false, int opacityPercent = 100)
        {
            elements.Add(new MapElementDraft(
                MapElementKind.Chip, imageSource, x, y, quarterTurns, flip, opacityPercent));
            return this;
        }

        /// <summary>添加 Picture；坐标为图片中心，旋转单位为度。</summary>
        public MapLayerDraft AddPicture(
            string imageSource, float x, float y, int rotationDegrees = 0, bool flip = false, int opacityPercent = 100)
        {
            elements.Add(new MapElementDraft(
                MapElementKind.Picture, imageSource, x, y, rotationDegrees, flip, opacityPercent));
            return this;
        }
    }

    /// <summary>第一版支持的地图图元类型。</summary>
    public enum MapElementKind
    {
        Chip,
        Picture,
    }

    /// <summary>新地图里的一个初始 CP/PIC 图元。</summary>
    public sealed class MapElementDraft
    {
        internal MapElementDraft(
            MapElementKind kind, string imageSource, float x, float y, int rotation, bool flip, int opacityPercent)
        {
            Kind = kind;
            ImageSource = imageSource;
            X = x;
            Y = y;
            Rotation = rotation;
            Flip = flip;
            OpacityPercent = opacityPercent;
        }

        public MapElementKind Kind { get; }
        public string ImageSource { get; }
        public float X { get; }
        public float Y { get; }
        public int Rotation { get; }
        public bool Flip { get; }
        public int OpacityPercent { get; }
    }
}
