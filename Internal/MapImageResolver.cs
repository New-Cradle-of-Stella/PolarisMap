using System;
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

            M2ChipImage image = images?.Get(source);
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

            int slash = source.LastIndexOf('/');
            string directory = slash < 0 ? null : source.Substring(0, slash + 1);
            return new ResolvedImage(image, id, directory);
        }
    }
}
