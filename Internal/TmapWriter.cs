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
                writer.Byte(MapFormat.DefaultCollectionCapacity);
                writer.Byte(MapFormat.DefaultCollectionCapacity);
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
            foreach (MapElementDraft element in layer.Elements)
            {
                WriteElement(writer, element, images[element]);
            }
            return output.ToArray();
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
                    resolved.Image, (int)element.X, (int)element.Y, rotation, element.Flip,
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
            int mapX,
            int mapY,
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

            drawX = checked(mapX * MapFormat.CellPixels);
            drawX += shift.x < 0 ? -(rotated.x - width + shift.x) : shift.x;
            drawY = checked(mapY * MapFormat.CellPixels);
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
