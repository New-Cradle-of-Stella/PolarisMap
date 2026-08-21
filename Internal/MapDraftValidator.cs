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

            if (draft.Layers.Count == 0)
            {
                throw new ArgumentException("A map must contain at least one layer.", nameof(draft));
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int keyLayerIndex = -1;
            for (int i = 0; i < draft.Layers.Count; i++)
            {
                MapLayerDraft layer = draft.Layers[i];
                RequirePascal(layer.Name, "Layer name");
                RequireString(layer.Comment ?? "", "Layer comment");
                if (!names.Add(layer.Name))
                {
                    throw new ArgumentException($"Duplicate layer name: {layer.Name}.", nameof(draft));
                }
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
                }
            }

            return keyLayerIndex < 0 ? 0 : keyLayerIndex;
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
            if (element.X != (int)element.X || element.Y != (int)element.Y)
            {
                throw new ArgumentException("Chip coordinates must be whole map cells.", nameof(draft));
            }
            if (element.X < 0 || element.X >= draft.Width || element.Y < 0 || element.Y >= draft.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(draft), "Chip coordinates are outside the map.");
            }
        }

        static void RequireDimension(int value, string name)
        {
            if (!MapFormat.IsValidDimensionPixels(value))
            {
                throw new ArgumentOutOfRangeException(
                    "draft", $"Map {name} is outside the TMAP u16 pixel range.");
            }
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
