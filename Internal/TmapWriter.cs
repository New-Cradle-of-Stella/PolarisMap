using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using m2d;
using UnityEngine;
using XX;

namespace Polaris.Map.Internal
{
    /// <summary>把已验证的公开草稿编译为游戏可读的 TMAP v4。</summary>
    internal static class TmapWriter
    {
        internal static byte[] Build(MapDraft draft, M2DBase m2d)
        {
            if (m2d == null)
            {
                throw new InvalidOperationException("The game map system is not available yet.");
            }

            int keyLayerIndex = MapDraftValidator.Validate(draft);
            Dictionary<MapElementDraft, MapImageResolver.ResolvedImage> images = ResolveImages(draft, m2d.IMGS);

            using var output = new MemoryStream();
            var writer = new BigEndianWriter(output);
            WriteHeader(writer, draft, images.Values);
            WriteLayers(writer, draft, keyLayerIndex, images);
            WriteJoints(writer, draft);
            return output.ToArray();
        }

        static Dictionary<MapElementDraft, MapImageResolver.ResolvedImage> ResolveImages(
            MapDraft draft,
            M2ImageContainer imageContainer)
        {
            var result = new Dictionary<MapElementDraft, MapImageResolver.ResolvedImage>();
            foreach (MapLayerDraft layer in draft.Layers)
            {
                foreach (MapElementDraft element in layer.Elements)
                {
                    result.Add(element, MapImageResolver.Resolve(
                        imageContainer, element.ImageSource, requireStableId: true, nameof(draft)));
                }
            }
            return result;
        }

        static void WriteHeader(
            BigEndianWriter writer,
            MapDraft draft,
            IEnumerable<MapImageResolver.ResolvedImage> images)
        {
            writer.Byte(MapFormat.Version);
            writer.Tag(Map2d.BIN_CTG.NAME);
            writer.Pascal(draft.Key);
            writer.Tag(Map2d.BIN_CTG.SIZE);
            writer.UShort(checked((ushort)(draft.Width * MapFormat.CellPixels)));
            writer.UShort(checked((ushort)(draft.Height * MapFormat.CellPixels)));
            writer.Tag(Map2d.BIN_CTG.BGCOL);
            writer.Byte(draft.Background.Red);
            writer.Byte(draft.Background.Green);
            writer.Byte(draft.Background.Blue);
            writer.Tag(Map2d.BIN_CTG.COMMENT);
            writer.String(draft.Comment ?? "");

            if (draft.CspKeys.Count != 0 || draft.CspExpectedCount != 0)
            {
                writer.Tag(Map2d.BIN_CTG.CSP);
                writer.Byte(draft.CspExpectedCount == 0
                    ? checked((byte)draft.CspKeys.Count)
                    : draft.CspExpectedCount);
                writer.Byte(checked((byte)draft.CspKeys.Count));
                foreach (string key in draft.CspKeys) writer.Pascal(key);
            }

            if (draft.EditorAdditional.Count != 0)
            {
                writer.Tag(Map2d.BIN_CTG.EDITOR_ADDITIONAL);
                writer.Byte(checked((byte)draft.EditorAdditional.Count));
                foreach (string value in draft.EditorAdditional) writer.Pascal(value);
            }

            foreach (MapMeshRectDraft rect in draft.MeshRects)
            {
                writer.Tag(Map2d.BIN_CTG.MESH_RECT);
                writer.Byte(rect.Index);
                writer.Float(rect.X);
                writer.Float(rect.Y);
                writer.Float(rect.Width);
                writer.Float(rect.Height);
            }

            string[] directories = images
                .Select(static image => image.Directory)
                .Where(static directory => directory != null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static directory => directory, StringComparer.Ordinal)
                .ToArray();
            if (directories.Length == 0)
            {
                return;
            }

            writer.Tag(Map2d.BIN_CTG.IMGDIR);
            writer.Byte(checked((byte)directories.Length));
            foreach (string directory in directories)
            {
                writer.Pascal(directory);
            }
        }

        static void WriteLayers(
            BigEndianWriter writer,
            MapDraft draft,
            int keyLayerIndex,
            IReadOnlyDictionary<MapElementDraft, MapImageResolver.ResolvedImage> images)
        {
            writer.Tag(Map2d.BIN_CTG.LAYER_REVERSE);
            writer.Int(draft.Layers.Count);

            // LAYER_REVERSE 会把每个新层插到数组头部，所以磁盘顺序与公开的底→顶顺序相反。
            for (int i = draft.Layers.Count - 1; i >= 0; i--)
            {
                MapLayerDraft layer = draft.Layers[i];
                byte[] content = BuildLayerContent(layer, images);

                writer.Tag(Map2d.BIN_CTG.LAYER_HEADER_2);
                writer.Pascal(layer.Name);
                writer.String(layer.Comment ?? "");
                writer.Byte((byte)(i == keyLayerIndex ? 1 : 0));
                writer.UInt(PackLayerColor(layer.Color));
                writer.UInt((uint)Math.Max(MapFormat.DefaultCollectionCapacity, layer.Elements.Count));
                writer.Byte(checked((byte)Math.Max(MapFormat.DefaultCollectionCapacity, layer.LabelPoints.Count)));
                writer.Byte(checked((byte)Math.Max(MapFormat.DefaultCollectionCapacity, layer.Gradations.Count)));
                writer.Tag(Map2d.BIN_CTG.LAY_CHIPS_CONTENT);
                writer.UInt(checked((uint)content.Length));
                writer.Bytes(content);
            }
        }

        static byte[] BuildLayerContent(
            MapLayerDraft layer,
            IReadOnlyDictionary<MapElementDraft, MapImageResolver.ResolvedImage> images)
        {
            using var output = new MemoryStream();
            var writer = new BigEndianWriter(output);
            uint patternId = 0;
            foreach (MapElementDraft element in layer.Elements)
            {
                if (element.PatternId != patternId)
                {
                    writer.Tag(Map2d.BIN_CTG.PAT_CHANGE);
                    writer.UInt(element.PatternId);
                    patternId = element.PatternId;
                }
                WriteElement(writer, element, images[element]);
            }
            foreach (MapLabelPointDraft point in layer.LabelPoints) WriteLabelPoint(writer, point);
            foreach (MapGradationDraft gradation in layer.Gradations) WriteGradation(writer, gradation);
            foreach (MapSubMapDraft subMap in layer.SubMaps) WriteSubMap(writer, subMap);
            return output.ToArray();
        }

        static void WriteLabelPoint(BigEndianWriter writer, MapLabelPointDraft value)
        {
            writer.Tag(Map2d.BIN_CTG.LP);
            writer.Pascal(value.Key);
            writer.Short(ToPixelShort(value.X));
            writer.Short(ToPixelShort(value.Y));
            writer.Short(ToPixelShort(value.Width));
            writer.Short(ToPixelShort(value.Height));
            writer.Float(value.FocusX);
            writer.Float(value.FocusY);
            writer.String(value.Command ?? "");
            writer.String(value.Comment ?? "");
        }

        static void WriteGradation(BigEndianWriter writer, MapGradationDraft value)
        {
            writer.Tag(Map2d.BIN_CTG.GRD);
            writer.Pascal(value.Key);
            writer.Short(ToPixelShort(value.X));
            writer.Short(ToPixelShort(value.Y));
            writer.Short(ToPixelShort(value.Width));
            writer.Short(ToPixelShort(value.Height));
            writer.Byte((byte)value.Order);
            writer.Byte((byte)value.Direction);
            WriteColor(writer, value.StartColor);
            WriteColor(writer, value.EndColor);
            if (value.Direction != MapGradationDirection.Slicer) return;
            writer.Byte(value.SlicerColumns);
            if (value.SlicerColumns == 0) return;
            writer.Byte(value.SlicerRows);
            foreach (float sample in value.InternalX) writer.Float(sample);
            foreach (float sample in value.InternalY) writer.Float(sample);
            foreach (float sample in value.Levels) writer.Float(sample);
        }

        static void WriteSubMap(BigEndianWriter writer, MapSubMapDraft value)
        {
            writer.Tag(Map2d.BIN_CTG.SM);
            writer.Pascal(value.TargetMapKey);
            writer.Float(value.X);
            writer.Float(value.Y);
            writer.Float(value.BaseX);
            writer.Float(value.BaseY);
            writer.Float(value.ScaleX);
            writer.Float(value.ScaleY);
            writer.Float(value.ScrollX);
            writer.Float(value.ScrollY);
            writer.Byte((byte)value.Order);
            writer.Byte(value.RepeatX);
            writer.Byte(value.RepeatY);
            writer.Float(value.IntervalX);
            writer.Float(value.IntervalY);
            writer.Float(value.CameraLength);
        }

        static void WriteJoints(BigEndianWriter writer, MapDraft draft)
        {
            var references = new Dictionary<string, ChipReference>(StringComparer.Ordinal);
            for (int layerIndex = 0; layerIndex < draft.Layers.Count; layerIndex++)
            {
                MapLayerDraft layer = draft.Layers[layerIndex];
                for (int elementIndex = 0; elementIndex < layer.Elements.Count; elementIndex++)
                {
                    MapElementDraft element = layer.Elements[elementIndex];
                    if (!string.IsNullOrEmpty(element.Id))
                        references.Add(element.Id, new ChipReference(layerIndex, elementIndex, element.Kind));
                }
            }

            for (int layerIndex = 0; layerIndex < draft.Layers.Count; layerIndex++)
            {
                MapLayerDraft layer = draft.Layers[layerIndex];
                if (layer.Joints.Count == 0) continue;
                byte encodedLayer = checked((byte)layerIndex);
                writer.Tag(Map2d.BIN_CTG.JOINT_COUNT);
                writer.Byte(encodedLayer);
                writer.UShort(checked((ushort)layer.Joints.Count));
                foreach (MapJointDraft joint in layer.Joints)
                {
                    writer.Tag(Map2d.BIN_CTG.JOINT);
                    writer.Byte(encodedLayer);
                    writer.Byte(checked((byte)joint.Points.Count));
                    writer.Float(joint.CenterX);
                    writer.Float(joint.CenterY);
                    WriteColor(writer, joint.Color);
                    writer.Byte(joint.Thickness);
                    foreach (MapJointPointDraft point in joint.Points)
                    {
                        writer.Float(point.X);
                        writer.Float(point.Y);
                        if (string.IsNullOrEmpty(point.ChipId))
                        {
                            writer.Int(-1);
                            continue;
                        }
                        ChipReference reference = references[point.ChipId];
                        writer.Int(reference.ElementIndex);
                        writer.Byte(checked((byte)reference.LayerIndex));
                    }
                }
            }
        }

        static short ToPixelShort(float cells)
            => checked((short)Math.Round(cells * MapFormat.CellPixels, MidpointRounding.AwayFromZero));

        static void WriteColor(BigEndianWriter writer, MapColor color)
        {
            writer.Byte(color.Red);
            writer.Byte(color.Green);
            writer.Byte(color.Blue);
            writer.Byte(color.Alpha);
        }

        readonly struct ChipReference
        {
            internal ChipReference(int layerIndex, int elementIndex, MapElementKind kind)
            {
                LayerIndex = layerIndex;
                ElementIndex = elementIndex;
                Kind = kind;
            }

            internal int LayerIndex { get; }
            internal int ElementIndex { get; }
            internal MapElementKind Kind { get; }
        }

        static void WriteElement(
            BigEndianWriter writer,
            MapElementDraft element,
            MapImageResolver.ResolvedImage resolved)
        {
            int drawX;
            int drawY;
            int rotation;
            if (element.Kind == MapElementKind.Chip)
            {
                rotation = MapFormat.NormalizeQuarterTurns(element.Rotation);
                CalculateChipDrawPosition(
                    resolved.Image, element.X, element.Y, rotation, element.Flip,
                    out drawX, out drawY);
                writer.Tag(Map2d.BIN_CTG.CP);
            }
            else
            {
                rotation = element.Rotation;
                CalculatePictureDrawPosition(resolved.Image, element.X, element.Y, rotation, out drawX, out drawY);
                writer.Tag(Map2d.BIN_CTG.PIC);
            }

            writer.UInt(resolved.Id);
            writer.Short(checked((short)drawX));
            writer.Short(checked((short)drawY));
            writer.Byte(checked((byte)(element.OpacityPercent + (element.Flip ? 100 : 0))));
            writer.Short(checked((short)rotation));
        }

        static void CalculateChipDrawPosition(
            M2ChipImage image,
            float mapX,
            float mapY,
            int rotation,
            bool flip,
            out int drawX,
            out int drawY)
        {
            bool sideways = rotation % 2 == 1;
            int width = sideways ? image.iheight : image.iwidth;
            int height = sideways ? image.iwidth : image.iheight;
            Vector2Int shift = image.getShift(rotation, flip);
            Vector2Int rotated = image.getRWH(rotation);

            drawX = X.IntR(mapX * MapFormat.CellPixels);
            drawX += shift.x < 0 ? -(rotated.x - width + shift.x) : shift.x;
            drawY = X.IntR(mapY * MapFormat.CellPixels);
            drawY += shift.y < 0 ? -(rotated.y - height + shift.y) : shift.y;
        }

        static void CalculatePictureDrawPosition(
            M2ChipImage image,
            float mapX,
            float mapY,
            int rotationDegrees,
            out int drawX,
            out int drawY)
        {
            float radians = rotationDegrees / 180f * Mathf.PI;
            float cosine = Mathf.Abs(Mathf.Cos(radians));
            float sine = Mathf.Abs(Mathf.Sin(radians));
            float width = image.iwidth * cosine + image.iheight * sine;
            float height = image.iheight * cosine + image.iwidth * sine;
            drawX = X.IntR(mapX * MapFormat.CellPixels - width * 0.5f);
            drawY = X.IntR(mapY * MapFormat.CellPixels - height * 0.5f);
        }

        static uint PackLayerColor(MapColor color)
        {
            // M2MapLayer 把 uint 直接交给 C32；原版默认值是 0xFF7F7F7F（A,R,G,B）。
            return ((uint)color.Alpha << 24)
                | ((uint)color.Red << 16)
                | ((uint)color.Green << 8)
                | color.Blue;
        }
    }
}
