using System;
using System.Collections.Generic;

namespace Polaris.Map.Internal
{
    /// <summary>验证公开草稿是否能被 TMAP v4 无损表达，并返回关键图层索引。</summary>
    internal static class MapDraftValidator
    {
        internal static int Validate(MapDraft draft)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }
            if (!MapFormat.IsSafeMapKey(draft.Key))
            {
                throw new ArgumentException(
                    "Map key must contain only ASCII letters, digits, underscores, dots, or hyphens, and cannot begin with a dot.",
                    nameof(draft));
            }
            RequirePascal(draft.Key, "Map key");
            RequireString(draft.Comment ?? "", "Map comment");
            RequireDimension(draft.Width, "width");
            RequireDimension(draft.Height, "height");
            if (draft.CspKeys.Count > byte.MaxValue
                || draft.EditorAdditional.Count > byte.MaxValue
                || draft.MeshRects.Count > byte.MaxValue)
            {
                throw new ArgumentException("TMAP header collections may contain at most 255 entries.", nameof(draft));
            }
            foreach (string key in draft.CspKeys) RequirePascal(key, "CSP key");
            foreach (string value in draft.EditorAdditional) RequirePascal(value, "Editor additional value");
            foreach (MapMeshRectDraft rect in draft.MeshRects)
            {
                RequireFinite(rect.X, rect.Y, rect.Width, rect.Height, "Mesh rectangle");
            }

            if (draft.Layers.Count == 0)
            {
                throw new ArgumentException("A map must contain at least one layer.", nameof(draft));
            }

            var elementIds = new Dictionary<string, MapElementKind>(StringComparer.Ordinal);
            int keyLayerIndex = -1;
            for (int i = 0; i < draft.Layers.Count; i++)
            {
                MapLayerDraft layer = draft.Layers[i];
                RequirePascal(layer.Name, "Layer name");
                RequireString(layer.Comment ?? "", "Layer comment");
                if (layer.IsKeyLayer)
                {
                    if (keyLayerIndex >= 0)
                    {
                        throw new ArgumentException("A map can have at most one key layer.", nameof(draft));
                    }
                    keyLayerIndex = i;
                }

                foreach (MapElementDraft element in layer.Elements)
                {
                    ValidateElement(element, draft);
                    if (!string.IsNullOrEmpty(element.Id))
                    {
                        if (elementIds.ContainsKey(element.Id))
                            throw new ArgumentException($"Duplicate map element id: {element.Id}.", nameof(draft));
                        elementIds.Add(element.Id, element.Kind);
                    }
                }
                if (layer.LabelPoints.Count > byte.MaxValue || layer.Gradations.Count > byte.MaxValue)
                    throw new ArgumentException("A layer may contain at most 255 label points or gradations.", nameof(draft));
                foreach (MapLabelPointDraft point in layer.LabelPoints) ValidateLabelPoint(point, draft);
                foreach (MapGradationDraft gradation in layer.Gradations) ValidateGradation(gradation, draft);
                foreach (MapSubMapDraft subMap in layer.SubMaps) ValidateSubMap(subMap, draft);
                if (layer.Joints.Count > ushort.MaxValue)
                    throw new ArgumentException("A layer may contain at most 65535 joints.", nameof(draft));
            }

            foreach (MapLayerDraft layer in draft.Layers)
                foreach (MapJointDraft joint in layer.Joints)
                    ValidateJoint(joint, elementIds, draft);

            return keyLayerIndex < 0 ? 0 : keyLayerIndex;
        }

        static void ValidateLabelPoint(MapLabelPointDraft point, MapDraft draft)
        {
            if (point == null) throw new ArgumentException("Label point cannot be null.", nameof(draft));
            RequirePascal(point.Key, "Label point key");
            RequireString(point.Command ?? "", "Label point command");
            RequireString(point.Comment ?? "", "Label point comment");
            RequireFinite(point.X, point.Y, point.Width, point.Height, "Label point rectangle");
            RequireFinite(point.FocusX, point.FocusY, 0, 0, "Label point focus");
            RequirePixelShort(point.X, "Label point x");
            RequirePixelShort(point.Y, "Label point y");
            RequirePixelShort(point.Width, "Label point width");
            RequirePixelShort(point.Height, "Label point height");
            if (point.Width < 0 || point.Height < 0)
                throw new ArgumentOutOfRangeException(nameof(draft), "Label point width/height cannot be negative.");
        }

        static void ValidateGradation(MapGradationDraft value, MapDraft draft)
        {
            if (value == null) throw new ArgumentException("Gradation cannot be null.", nameof(draft));
            RequirePascal(value.Key, "Gradation key");
            RequireFinite(value.X, value.Y, value.Width, value.Height, "Gradation rectangle");
            RequirePixelShort(value.X, "Gradation x");
            RequirePixelShort(value.Y, "Gradation y");
            RequirePixelShort(value.Width, "Gradation width");
            RequirePixelShort(value.Height, "Gradation height");
            if (value.Width < 0 || value.Height < 0)
                throw new ArgumentOutOfRangeException(nameof(draft), "Gradation width/height cannot be negative.");
            if ((byte)value.Direction > (byte)MapGradationDirection.Slicer
                || (byte)value.Order > (byte)MapGradationOrder.Top)
                throw new ArgumentOutOfRangeException(nameof(draft), "Gradation direction/order is outside TMAP v4.");

            if (value.Direction != MapGradationDirection.Slicer)
            {
                if (value.SlicerColumns != 0 || value.SlicerRows != 0
                    || value.InternalX.Count != 0 || value.InternalY.Count != 0 || value.Levels.Count != 0)
                    throw new ArgumentException("Only SLICER gradations may contain slicer grid data.", nameof(draft));
                return;
            }
            if (value.SlicerColumns == 0)
            {
                if (value.SlicerRows != 0 || value.InternalX.Count != 0
                    || value.InternalY.Count != 0 || value.Levels.Count != 0)
                    throw new ArgumentException("An empty slicer grid cannot contain rows or samples.", nameof(draft));
                return;
            }
            if (value.SlicerColumns < 2 || value.SlicerRows < 2)
                throw new ArgumentException("A non-empty slicer grid requires at least 2 columns and 2 rows.", nameof(draft));
            if (value.InternalX.Count != value.SlicerColumns - 2
                || value.InternalY.Count != value.SlicerRows - 2
                || value.Levels.Count != value.SlicerColumns * value.SlicerRows)
                throw new ArgumentException("Slicer coordinate/level counts do not match its rows and columns.", nameof(draft));
            foreach (float sample in value.InternalX) RequireFinite(sample, 0, 0, 0, "Slicer X");
            foreach (float sample in value.InternalY) RequireFinite(sample, 0, 0, 0, "Slicer Y");
            foreach (float sample in value.Levels) RequireFinite(sample, 0, 0, 0, "Slicer level");
        }

        static void ValidateSubMap(MapSubMapDraft value, MapDraft draft)
        {
            if (value == null) throw new ArgumentException("Sub-map cannot be null.", nameof(draft));
            RequirePascal(value.TargetMapKey, "Sub-map target key");
            RequireFinite(value.X, value.Y, value.BaseX, value.BaseY, "Sub-map position");
            RequireFinite(value.ScaleX, value.ScaleY, value.ScrollX, value.ScrollY, "Sub-map transform");
            RequireFinite(value.IntervalX, value.IntervalY, value.CameraLength, 0, "Sub-map interval");
            if ((byte)value.Order > (byte)MapSubMapOrder.Top)
                throw new ArgumentOutOfRangeException(nameof(draft), "Sub-map order is outside TMAP v4.");
        }

        static void ValidateJoint(MapJointDraft value, Dictionary<string, MapElementKind> knownIds, MapDraft draft)
        {
            if (value == null) throw new ArgumentException("Joint cannot be null.", nameof(draft));
            if (value.Points.Count > byte.MaxValue)
                throw new ArgumentException("A joint may contain at most 255 points.", nameof(draft));
            RequireFinite(value.CenterX, value.CenterY, 0, 0, "Joint center");
            foreach (MapJointPointDraft point in value.Points)
            {
                if (point == null) throw new ArgumentException("Joint point cannot be null.", nameof(draft));
                RequireFinite(point.X, point.Y, 0, 0, "Joint point");
                if (!string.IsNullOrEmpty(point.ChipId))
                {
                    if (!knownIds.TryGetValue(point.ChipId, out MapElementKind kind))
                        throw new ArgumentException($"Joint references an unknown put id: {point.ChipId}.", nameof(draft));
                    if (kind != MapElementKind.Chip && kind != MapElementKind.Picture)
                        throw new ArgumentException($"Joint reference must target a chip or picture: {point.ChipId}.", nameof(draft));
                }
            }
        }

        static void ValidateElement(MapElementDraft element, MapDraft draft)
        {
            if (string.IsNullOrEmpty(element.ImageSource))
            {
                throw new ArgumentException("Image source cannot be empty.", nameof(draft));
            }

            int opacity = MapFormat.RequireOpacity(element.OpacityPercent, nameof(draft));
            if (element.Flip && opacity == 0)
            {
                throw new ArgumentException(
                    "TMAP cannot represent a flipped element at exactly 0% opacity; use 1% or disable flip.",
                    nameof(draft));
            }
            if (!MapFormat.IsValidRotation(element.Rotation))
            {
                throw new ArgumentOutOfRangeException(nameof(draft), "Element rotation exceeds the TMAP i16 range.");
            }

            MapFormat.RequireFinite(element.X, element.Y, nameof(draft));
            if (element.Kind != MapElementKind.Chip)
            {
                return;
            }
            // 原版编辑器允许 CP 以像素为粒度偏移，也允许装饰芯片跨出地图边界。
            // 最终 i16 draw 坐标由 TmapWriter 在解析图像 shift 后做精确溢出检查。
        }

        static void RequireDimension(int value, string name)
        {
            if (!MapFormat.IsValidDimensionPixels(value))
            {
                throw new ArgumentOutOfRangeException(
                    "draft", $"Map {name} is outside the TMAP u16 pixel range.");
            }
        }

        static void RequirePixelShort(float cells, string what)
        {
            double pixels = cells * MapFormat.CellPixels;
            if (pixels < short.MinValue || pixels > short.MaxValue)
                throw new ArgumentOutOfRangeException("draft", $"{what} is outside the TMAP i16 pixel range.");
        }

        static void RequireFinite(float a, float b, float c, float d, string what)
        {
            if (float.IsNaN(a) || float.IsInfinity(a) || float.IsNaN(b) || float.IsInfinity(b)
                || float.IsNaN(c) || float.IsInfinity(c) || float.IsNaN(d) || float.IsInfinity(d))
                throw new ArgumentException($"{what} values must be finite.");
        }

        static void RequirePascal(string value, string what)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"{what} cannot be empty.");
            }
            if (MapFormat.ExceedsUtf8ByteLimit(value, MapFormat.MaxPascalStringBytes))
            {
                throw new ArgumentException($"{what} exceeds 255 UTF-8 bytes.");
            }
        }

        static void RequireString(string value, string what)
        {
            if (MapFormat.ExceedsUtf8ByteLimit(value, MapFormat.MaxLongStringBytes))
            {
                throw new ArgumentException($"{what} exceeds 65535 UTF-8 bytes.");
            }
        }
    }
}
