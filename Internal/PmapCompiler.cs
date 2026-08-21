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
            };
            foreach (PmapLayer sourceLayer in document.Layers)
            {
                MapLayerDraft layer = draft.AddLayer(sourceLayer.Name, sourceLayer.IsKeyLayer);
                layer.Color = ParseColor(sourceLayer.Color);
                layer.Comment = sourceLayer.Comment ?? "";
                foreach (PmapElement element in sourceLayer.Elements)
                {
                    if (element.Kind == PmapElementKind.Chip)
                    {
                        layer.AddChip(element.Image, checked((int)element.X), checked((int)element.Y),
                            element.Rotation, element.Flip, element.Opacity);
                    }
                    else
                    {
                        layer.AddPicture(element.Image, element.X, element.Y,
                            element.Rotation, element.Flip, element.Opacity);
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
