using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using m2d;
using UnityEngine;

namespace Polaris.Map.Internal
{
    /// <summary>把当前游戏图集里 PMAP 实际引用的原版 MapChip 临时裁成 PNG，供 PolarisTools 内嵌预览。</summary>
    internal static class MapPreviewExtractor
    {
        static string activeDirectory;

        internal static string Extract(IEnumerable<uint> requestedImageIds)
        {
            M2ImageContainer images = M2DBase.Instance?.IMGS;
            RenderTexture atlas = images?.Atlas?.getTextureForDebug();
            if (images == null || atlas == null)
                throw new InvalidOperationException("The current map-chip atlas is not ready. Wait for the map to finish loading, then preview again.");

            uint[] ids = (requestedImageIds ?? Enumerable.Empty<uint>())
                .Where(id => id != 0)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            if (ids.Length == 0)
                throw new InvalidDataException("This PMAP does not contain any original map-chip image ids.");

            Clear();
            string root = Path.Combine(Path.GetTempPath(), "PolarisTools", "MapPreview");
            string destination = Path.Combine(root,
                System.Diagnostics.Process.GetCurrentProcess().Id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            Directory.CreateDirectory(destination);
            activeDirectory = destination;

            int exported = 0;
            var missing = new List<uint>();
            foreach (uint id in ids)
            {
                if (!(images.GetById(id) is M2ChipImage chip) || !chip.SourceAtlas.valid)
                {
                    missing.Add(id);
                    continue;
                }

                int width = chip.SourceAtlas.w;
                int height = chip.SourceAtlas.h;
                if (width <= 0 || height <= 0)
                {
                    missing.Add(id);
                    continue;
                }

                byte[] png = ReadAtlasPng(atlas, chip.SourceAtlas.x, chip.SourceAtlas.y, width, height);
                File.WriteAllBytes(Path.Combine(destination, id.ToString(CultureInfo.InvariantCulture) + ".png"), png);
                exported++;
            }

            if (exported == 0)
                throw new InvalidDataException("None of this PMAP's original map chips are present in the loaded game atlas.");

            File.WriteAllText(Path.Combine(destination, "PREVIEW-ONLY.txt"),
                "Temporarily rendered from this user's local game installation for private preview.\r\n"
                + "Do not add these PNG files to a project, commit, package, or distribution.\r\n"
                + "Use 'Clear preview' in PolarisTools when finished.\r\n");
            return destination + "|" + exported.ToString(CultureInfo.InvariantCulture) + "|"
                + missing.Count.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Clear()
        {
            string path = activeDirectory;
            activeDirectory = null;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return "Preview cache is already clear.";

            string expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PolarisTools", "MapPreview"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to clear a preview directory outside the PolarisTools temp root.");
            Directory.Delete(resolved, true);
            return "Preview cache cleared.";
        }

        static byte[] ReadAtlasPng(RenderTexture atlas, int x, int y, int width, int height)
        {
            RenderTexture old = RenderTexture.active;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = atlas;
                readable.ReadPixels(new Rect(x, y, width, height), 0, 0);
                readable.Apply(false, false);
                byte[] png = readable.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidDataException("Unity returned an empty map-chip preview image.");
                return png;
            }
            finally
            {
                RenderTexture.active = old;
                UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
