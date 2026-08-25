using System;
using System.Globalization;
using m2d;

namespace Polaris.Map.Internal
{
    /// <summary>集中处理 TMAP 图像路径、运行时对象和稳定 image id 的对应关系。</summary>
    internal static class MapImageResolver
    {
        internal readonly struct ResolvedImage
        {
            internal ResolvedImage(M2ChipImage image, uint id, string directory)
            {
                Image = image;
                Id = id;
                Directory = directory;
            }

            internal M2ChipImage Image { get; }
            internal uint Id { get; }
            internal string Directory { get; }
        }

        internal static ResolvedImage Resolve(
            M2ImageContainer images,
            string source,
            bool requireStableId,
            string parameterName)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("Image source cannot be empty.", parameterName);
            }

            string path = source;
            M2ChipImage image = null;
            int hash = source.LastIndexOf('#');
            string pathForDirectory = hash > 0 ? source.Substring(0, hash) : source;
            int directorySlash = pathForDirectory.LastIndexOf('/');
            string declaredDirectory = directorySlash < 0 ? null : pathForDirectory.Substring(0, directorySlash + 1);
            if (images != null && declaredDirectory != null)
            {
                // Get/GetById 只查已经展开的目录；转换来的 PMAP 可能是本局尚未用过的图集。
                images.initializeChipsDirectory(declaredDirectory, -1, no_make_dir: true);
            }
            if (hash > 0 && uint.TryParse(source.Substring(hash + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out uint explicitId))
            {
                path = source.Substring(0, hash);
                image = images?.GetById(explicitId) as M2ChipImage;
            }
            else
            {
                image = images?.Get(source);
            }
            if (image == null)
            {
                throw new ArgumentException(
                    $"No such map image in this game version: {source}.", parameterName);
            }

            uint id = image.getChipId();
            if (requireStableId && !ReferenceEquals(images.GetById(id), image))
            {
                throw new ArgumentException(
                    $"The map image has no stable TMAP image id: {source}.", parameterName);
            }

            int slash = path.LastIndexOf('/');
            string directory = slash < 0 ? null : path.Substring(0, slash + 1);
            return new ResolvedImage(image, id, directory);
        }
    }
}
