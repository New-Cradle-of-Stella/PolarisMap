using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Polaris.Map.Internal;

namespace Polaris.Map.Authoring
{
    /// <summary>Polaris 的 XML 地图源文件；它是 TMAP v4 的可读、高层封装。数值规则（正则、字节上限）与
    /// <see cref="MapDraftValidator"/> 共享同一份出处，见 <see cref="MapFormat"/>。</summary>
    public sealed class PmapDocument
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string Key { get; set; } = "new_map";
        public int Width { get; set; } = 32;
        public int Height { get; set; } = 18;
        public string Background { get; set; } = "#F5FFEE";
        public string Comment { get; set; } = "";
        public List<PmapLayer> Layers { get; } = new List<PmapLayer>();

        public static PmapDocument CreateDefault(string key = "new_map")
        {
            var document = new PmapDocument { Key = key };
            document.Layers.Add(new PmapLayer { Name = "main", IsKeyLayer = true });
            return document;
        }

        public static PmapDocument Parse(string xml, string sourceName = ".pmap")
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new PmapFormatException(sourceName + " is empty.");

            try
            {
                using (var text = new StringReader(xml))
                using (var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                }))
                {
                    return Read(XDocument.Load(reader, LoadOptions.SetLineInfo), sourceName);
                }
            }
            catch (PmapFormatException)
            {
                throw;
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
            {
                throw new PmapFormatException(sourceName + ": " + ex.Message, ex);
            }
        }

        public static PmapDocument Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A .pmap path is required.", nameof(path));
            return Parse(File.ReadAllText(path, Encoding.UTF8), path);
        }

        public string ToXml()
        {
            Validate();
            var root = new XElement("pmap",
                new XAttribute("version", Version),
                new XAttribute("key", Key),
                new XAttribute("width", Width),
                new XAttribute("height", Height),
                new XAttribute("background", NormalizeColor(Background)));

            if (!string.IsNullOrEmpty(Comment))
                root.Add(new XAttribute("comment", Comment));

            var layers = new XElement("layers");
            foreach (PmapLayer layer in Layers)
            {
                var layerNode = new XElement("layer",
                    new XAttribute("name", layer.Name),
                    new XAttribute("key", layer.IsKeyLayer),
                    new XAttribute("color", NormalizeColor(layer.Color)));
                if (!string.IsNullOrEmpty(layer.Comment))
                    layerNode.Add(new XAttribute("comment", layer.Comment));

                foreach (PmapElement element in layer.Elements)
                {
                    string name = element.Kind == PmapElementKind.Chip ? "chip" : "picture";
                    var node = new XElement(name,
                        new XAttribute("image", element.Image),
                        new XAttribute("x", F(element.X)),
                        new XAttribute("y", F(element.Y)),
                        new XAttribute("rotation", element.Rotation),
                        new XAttribute("flip", element.Flip),
                        new XAttribute("opacity", element.Opacity),
                        new XAttribute("visualWidth", F(element.VisualWidth)),
                        new XAttribute("visualHeight", F(element.VisualHeight)),
                        new XAttribute("color", NormalizeColor(element.Color)));
                    if (!string.IsNullOrEmpty(element.Id))
                        node.Add(new XAttribute("id", element.Id));
                    if (!string.IsNullOrEmpty(element.Label))
                        node.Add(new XAttribute("label", element.Label));
                    layerNode.Add(node);
                }
                layers.Add(layerNode);
            }
            root.Add(layers);

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root)
                .ToString(SaveOptions.None) + Environment.NewLine;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, ToXml(), new UTF8Encoding(false));
        }

        public void Validate()
        {
            if (Version != CurrentVersion)
                throw new PmapFormatException("Unsupported .pmap version " + Version + "; expected " + CurrentVersion + ".");
            if (string.IsNullOrWhiteSpace(Key))
                throw new PmapFormatException("Map key cannot be empty.");
            if (!MapFormat.IsSafeMapKey(Key))
                throw new PmapFormatException("Map key may contain only ASCII letters, digits, underscores, dots or hyphens, and may not begin with a dot.");
            if (MapFormat.ExceedsUtf8ByteLimit(Key, MapFormat.MaxPascalStringBytes))
                throw new PmapFormatException("Map key exceeds 255 UTF-8 bytes.");
            if (!MapFormat.IsValidDimensionPixels(Width) || !MapFormat.IsValidDimensionPixels(Height))
                throw new PmapFormatException("Map width/height is outside the TMAP u16 pixel range.");
            if (MapFormat.ExceedsUtf8ByteLimit(Comment, MapFormat.MaxLongStringBytes))
                throw new PmapFormatException("Map comment exceeds 65535 UTF-8 bytes.");
            NormalizeColor(Background);
            if (Layers.Count == 0)
                throw new PmapFormatException("A .pmap must contain at least one layer.");
            if (Layers.Count(layer => layer.IsKeyLayer) != 1)
                throw new PmapFormatException("A .pmap must contain exactly one key layer.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var layerNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (PmapLayer layer in Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.Name))
                    throw new PmapFormatException("Layer name cannot be empty.");
                if (MapFormat.ExceedsUtf8ByteLimit(layer.Name, MapFormat.MaxPascalStringBytes))
                    throw new PmapFormatException("Layer name exceeds 255 UTF-8 bytes: " + layer.Name + ".");
                if (!layerNames.Add(layer.Name))
                    throw new PmapFormatException("Duplicate layer name: " + layer.Name + ".");
                if (MapFormat.ExceedsUtf8ByteLimit(layer.Comment, MapFormat.MaxLongStringBytes))
                    throw new PmapFormatException("Layer comment exceeds 65535 UTF-8 bytes: " + layer.Name + ".");
                NormalizeColor(layer.Color);
                foreach (PmapElement element in layer.Elements)
                {
                    if (string.IsNullOrWhiteSpace(element.Image))
                        throw new PmapFormatException("Every chip/picture must specify an image source.");
                    if (!string.IsNullOrEmpty(element.Id) && !ids.Add(element.Id))
                        throw new PmapFormatException("Duplicate element id: " + element.Id + ".");
                    if (!Finite(element.X) || !Finite(element.Y))
                        throw new PmapFormatException("Element coordinates must be finite.");
                    if (!Finite(element.VisualWidth) || !Finite(element.VisualHeight)
                        || element.VisualWidth <= 0 || element.VisualHeight <= 0)
                        throw new PmapFormatException("visualWidth and visualHeight must be positive.");
                    if (!MapFormat.IsValidOpacity(element.Opacity))
                        throw new PmapFormatException("Element opacity must be between 0 and 100.");
                    if (element.Kind == PmapElementKind.Chip && (element.X % 1 != 0 || element.Y % 1 != 0))
                        throw new PmapFormatException("Chip x/y must be whole map-cell coordinates.");
                    if (element.Kind == PmapElementKind.Chip
                        && (element.X < 0 || element.X >= Width || element.Y < 0 || element.Y >= Height))
                        throw new PmapFormatException("Chip coordinates are outside the map.");
                    if (!MapFormat.IsValidRotation(element.Rotation))
                        throw new PmapFormatException("Element rotation exceeds the TMAP i16 range.");
                    if (element.Flip && element.Opacity == 0)
                        throw new PmapFormatException("TMAP cannot encode a flipped element at exactly 0% opacity.");
                    NormalizeColor(element.Color);
                }
            }
        }

        public static string NormalizeColor(string value)
        {
            string text = (value ?? "").Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
                text = text.Substring(1);
            if (text.Length != 6 && text.Length != 8)
                throw new PmapFormatException("Color must be #RRGGBB or #RRGGBBAA: " + value + ".");
            for (int i = 0; i < text.Length; i++)
            {
                if (!Uri.IsHexDigit(text[i]))
                    throw new PmapFormatException("Color contains a non-hex character: " + value + ".");
            }
            return "#" + text.ToUpperInvariant();
        }

        private static PmapDocument Read(XDocument xml, string sourceName)
        {
            XElement root = xml.Root;
            if (root == null || root.Name.LocalName != "pmap")
                throw Error(root, sourceName, "Root element must be <pmap>.");

            var document = new PmapDocument
            {
                Version = Int(root, "version", CurrentVersion),
                Key = Required(root, "key", sourceName),
                Width = Int(root, "width", 32),
                Height = Int(root, "height", 18),
                Background = String(root, "background", "#F5FFEE"),
                Comment = String(root, "comment", ""),
            };

            XElement layersNode = root.Elements().FirstOrDefault(x => x.Name.LocalName == "layers");
            IEnumerable<XElement> layerNodes = layersNode == null
                ? root.Elements().Where(x => x.Name.LocalName == "layer")
                : layersNode.Elements().Where(x => x.Name.LocalName == "layer");
            foreach (XElement layerNode in layerNodes)
            {
                var layer = new PmapLayer
                {
                    Name = Required(layerNode, "name", sourceName),
                    IsKeyLayer = Bool(layerNode, "key", false),
                    Color = String(layerNode, "color", "#7F7F7F"),
                    Comment = String(layerNode, "comment", ""),
                };
                foreach (XElement node in layerNode.Elements())
                {
                    PmapElementKind kind;
                    if (node.Name.LocalName == "chip") kind = PmapElementKind.Chip;
                    else if (node.Name.LocalName == "picture") kind = PmapElementKind.Picture;
                    else continue;

                    layer.Elements.Add(new PmapElement
                    {
                        Kind = kind,
                        Id = String(node, "id", ""),
                        Image = Required(node, "image", sourceName),
                        X = Float(node, "x", 0),
                        Y = Float(node, "y", 0),
                        Rotation = Int(node, "rotation", 0),
                        Flip = Bool(node, "flip", false),
                        Opacity = Int(node, "opacity", 100),
                        VisualWidth = Float(node, "visualWidth", 1),
                        VisualHeight = Float(node, "visualHeight", 1),
                        Color = String(node, "color", kind == PmapElementKind.Chip ? "#5B6477" : "#B68A5A"),
                        Label = String(node, "label", ""),
                    });
                }
                document.Layers.Add(layer);
            }

            document.Validate();
            return document;
        }

        private static string Required(XElement node, string name, string source)
        {
            XAttribute attr = node.Attribute(name);
            if (attr == null || string.IsNullOrWhiteSpace(attr.Value))
                throw Error(node, source, "Missing required attribute '" + name + "'.");
            return attr.Value.Trim();
        }

        private static string String(XElement node, string name, string fallback)
            => node.Attribute(name)?.Value ?? fallback;

        private static int Int(XElement node, string name, int fallback)
        {
            string value = node.Attribute(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static float Float(XElement node, string name, float fallback)
        {
            string value = node.Attribute(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool Bool(XElement node, string name, bool fallback)
        {
            string value = node.Attribute(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : bool.Parse(value);
        }

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static PmapFormatException Error(XObject node, string source, string message)
        {
            var line = node as IXmlLineInfo;
            string at = line != null && line.HasLineInfo() ? " (line " + line.LineNumber + ")" : "";
            return new PmapFormatException(source + at + ": " + message);
        }
    }

    public sealed class PmapLayer
    {
        public string Name { get; set; } = "layer";
        public bool IsKeyLayer { get; set; }
        public string Color { get; set; } = "#7F7F7F";
        public string Comment { get; set; } = "";
        public List<PmapElement> Elements { get; } = new List<PmapElement>();
    }

    public enum PmapElementKind
    {
        Chip,
        Picture,
    }

    public sealed class PmapElement
    {
        public PmapElementKind Kind { get; set; }
        public string Id { get; set; } = "";
        public string Image { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public int Rotation { get; set; }
        public bool Flip { get; set; }
        public int Opacity { get; set; } = 100;

        /// <summary>仅供无素材编辑器显示；TMAP 中的实际尺寸仍由 image 指向的芯片定义决定。</summary>
        public float VisualWidth { get; set; } = 1;
        public float VisualHeight { get; set; } = 1;
        public string Color { get; set; } = "#5B6477";
        public string Label { get; set; } = "";
    }

    public sealed class PmapFormatException : FormatException
    {
        public PmapFormatException(string message) : base(message) { }
        public PmapFormatException(string message, Exception innerException) : base(message, innerException) { }
    }
}
