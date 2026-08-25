using System;
using System.Globalization;
using Polaris.Map.Authoring;

namespace Polaris.Map.Internal
{
    internal static class PmapCompiler
    {
        internal static MapDraft Compile(PmapDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Validate();

            var draft = new MapDraft(document.Key, document.Width, document.Height)
            {
                Background = ParseColor(document.Background),
                Comment = document.Comment ?? "",
                CspExpectedCount = checked((byte)document.CspExpectedCount),
            };
            foreach (string key in document.CspKeys) draft.CspKeys.Add(key);
            foreach (string value in document.EditorAdditional) draft.EditorAdditional.Add(value);
            foreach (PmapMeshRect rect in document.MeshRects)
                draft.AddMeshRect(checked((byte)rect.Index), rect.X, rect.Y, rect.Width, rect.Height);
            foreach (PmapLayer sourceLayer in document.Layers)
            {
                MapLayerDraft layer = draft.AddLayer(sourceLayer.Name, sourceLayer.IsKeyLayer);
                layer.Color = ParseColor(sourceLayer.Color);
                layer.Comment = sourceLayer.Comment ?? "";
                foreach (PmapElement element in sourceLayer.Elements)
                {
                    if (element.Kind == PmapElementKind.Chip)
                    {
                        layer.AddChip(element.Image, element.X, element.Y,
                            element.Rotation, element.Flip, element.Opacity, element.PatternId, element.Id);
                    }
                    else if (element.Kind == PmapElementKind.Picture)
                    {
                        layer.AddPicture(element.Image, element.X, element.Y,
                            element.Rotation, element.Flip, element.Opacity, element.PatternId, element.Id);
                    }
                    else if (element.Kind == PmapElementKind.LabelPoint)
                    {
                        layer.AddLabelPoint(element.Key, element.X, element.Y, element.Width, element.Height,
                            element.FocusX, element.FocusY, element.Command, element.Comment);
                    }
                    else if (element.Kind == PmapElementKind.Gradation)
                    {
                        var gradation = new MapGradationDraft(
                            element.Key, element.X, element.Y, element.Width, element.Height)
                        {
                            Order = (MapGradationOrder)element.Order,
                            Direction = (MapGradationDirection)element.Direction,
                            StartColor = ParseColor(element.StartColor),
                            EndColor = ParseColor(element.EndColor),
                            SlicerColumns = checked((byte)element.SlicerColumns),
                            SlicerRows = checked((byte)element.SlicerRows),
                        };
                        foreach (float value in element.InternalX) gradation.InternalX.Add(value);
                        foreach (float value in element.InternalY) gradation.InternalY.Add(value);
                        foreach (float value in element.Levels) gradation.Levels.Add(value);
                        layer.AddGradation(gradation);
                    }
                    else if (element.Kind == PmapElementKind.SubMap)
                    {
                        layer.AddSubMap(new MapSubMapDraft(element.TargetMap)
                        {
                            X = element.X, Y = element.Y, BaseX = element.BaseX, BaseY = element.BaseY,
                            ScaleX = element.ScaleX, ScaleY = element.ScaleY,
                            ScrollX = element.ScrollX, ScrollY = element.ScrollY,
                            Order = (MapSubMapOrder)element.Order,
                            RepeatX = checked((byte)element.RepeatX), RepeatY = checked((byte)element.RepeatY),
                            IntervalX = element.IntervalX, IntervalY = element.IntervalY,
                            CameraLength = element.CameraLength,
                        });
                    }
                    else if (element.Kind == PmapElementKind.Joint)
                    {
                        var joint = new MapJointDraft(element.X, element.Y)
                        {
                            Color = ParseColor(element.Color),
                            Thickness = checked((byte)element.Thickness),
                        };
                        foreach (PmapJointPoint point in element.Points)
                            joint.AddPoint(point.X, point.Y, point.ChipId);
                        layer.AddJoint(joint);
                    }
                }
            }
            return draft;
        }

        internal static MapColor ParseColor(string value)
        {
            string normalized = PmapDocument.NormalizeColor(value).Substring(1);
            byte r = byte.Parse(normalized.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte a = normalized.Length == 8
                ? byte.Parse(normalized.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : byte.MaxValue;
            return new MapColor(r, g, b, a);
        }
    }
}
