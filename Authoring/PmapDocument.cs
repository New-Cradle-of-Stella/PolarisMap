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
        public int CspExpectedCount { get; set; }
        public List<string> CspKeys { get; } = new List<string>();
        public List<string> EditorAdditional { get; } = new List<string>();
        public List<PmapMeshRect> MeshRects { get; } = new List<PmapMeshRect>();
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

            if (CspKeys.Count != 0 || CspExpectedCount != 0)
            {
                var csp = new XElement("csp", new XAttribute("expected", CspExpectedCount));
                foreach (string key in CspKeys) csp.Add(new XElement("key", new XAttribute("value", key)));
                root.Add(csp);
            }
            if (EditorAdditional.Count != 0)
            {
                var additional = new XElement("editorAdditional");
                foreach (string value in EditorAdditional)
                    additional.Add(new XElement("value", new XAttribute("text", value)));
                root.Add(additional);
            }
            if (MeshRects.Count != 0)
            {
                var rects = new XElement("meshRects");
                foreach (PmapMeshRect rect in MeshRects)
                {
                    rects.Add(new XElement("rect", new XAttribute("index", rect.Index),
                        new XAttribute("x", F(rect.X)), new XAttribute("y", F(rect.Y)),
                        new XAttribute("width", F(rect.Width)), new XAttribute("height", F(rect.Height))));
                }
                root.Add(rects);
            }

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
                    string name = ElementName(element.Kind);
                    var node = new XElement(name);
                    if (element.Kind == PmapElementKind.Chip || element.Kind == PmapElementKind.Picture)
                    {
                        node.Add(new XAttribute("image", element.Image),
                            new XAttribute("rotation", element.Rotation),
                            new XAttribute("flip", element.Flip),
                            new XAttribute("opacity", element.Opacity));
                        if (element.PatternId != 0) node.Add(new XAttribute("pattern", element.PatternId));
                    }
                    else if (element.Kind == PmapElementKind.LabelPoint)
                    {
                        node.Add(new XAttribute("key", element.Key),
                            new XAttribute("width", F(element.Width)), new XAttribute("height", F(element.Height)),
                            new XAttribute("focusX", F(element.FocusX)), new XAttribute("focusY", F(element.FocusY)));
                        if (!string.IsNullOrEmpty(element.Command)) node.Add(new XAttribute("command", element.Command));
                        if (!string.IsNullOrEmpty(element.Comment)) node.Add(new XAttribute("comment", element.Comment));
                    }
                    else if (element.Kind == PmapElementKind.Gradation)
                    {
                        node.Add(new XAttribute("key", element.Key),
                            new XAttribute("width", F(element.Width)), new XAttribute("height", F(element.Height)),
                            new XAttribute("order", element.Order), new XAttribute("direction", element.Direction),
                            new XAttribute("startColor", NormalizeColor(element.StartColor)),
                            new XAttribute("endColor", NormalizeColor(element.EndColor)));
                        if (element.Direction == 13)
                        {
                            node.Add(new XAttribute("columns", element.SlicerColumns));
                            if (element.SlicerColumns > 0)
                            {
                                node.Add(new XAttribute("rows", element.SlicerRows),
                                    new XAttribute("internalX", FloatList(element.InternalX)),
                                    new XAttribute("internalY", FloatList(element.InternalY)),
                                    new XAttribute("levels", FloatList(element.Levels)));
                            }
                        }
                    }
                    else if (element.Kind == PmapElementKind.SubMap)
                    {
                        node.Add(new XAttribute("target", element.TargetMap),
                            new XAttribute("baseX", F(element.BaseX)), new XAttribute("baseY", F(element.BaseY)),
                            new XAttribute("scaleX", F(element.ScaleX)), new XAttribute("scaleY", F(element.ScaleY)),
                            new XAttribute("scrollX", F(element.ScrollX)), new XAttribute("scrollY", F(element.ScrollY)),
                            new XAttribute("order", element.Order), new XAttribute("repeatX", element.RepeatX),
                            new XAttribute("repeatY", element.RepeatY), new XAttribute("intervalX", F(element.IntervalX)),
                            new XAttribute("intervalY", F(element.IntervalY)), new XAttribute("cameraLength", F(element.CameraLength)));
                    }
                    else
                    {
                        node.Add(new XAttribute("thickness", element.Thickness));
                        foreach (PmapJointPoint point in element.Points)
                        {
                            var pointNode = new XElement("point", new XAttribute("x", F(point.X)), new XAttribute("y", F(point.Y)));
                            if (!string.IsNullOrEmpty(point.ChipId)) pointNode.Add(new XAttribute("chip", point.ChipId));
                            node.Add(pointNode);
                        }
                    }
                    node.Add(new XAttribute("x", F(element.X)), new XAttribute("y", F(element.Y)),
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
            if (CspExpectedCount < 0 || CspExpectedCount > byte.MaxValue
                || CspKeys.Count > byte.MaxValue || EditorAdditional.Count > byte.MaxValue
                || MeshRects.Count > byte.MaxValue)
                throw new PmapFormatException("TMAP header collections/counts may contain at most 255 entries.");
            foreach (string key in CspKeys) RequirePascalValue(key, "CSP key");
            foreach (string value in EditorAdditional) RequirePascalValue(value, "Editor additional value");
            foreach (PmapMeshRect rect in MeshRects)
            {
                if (rect.Index < 0 || rect.Index > byte.MaxValue || !Finite(rect.X) || !Finite(rect.Y)
                    || !Finite(rect.Width) || !Finite(rect.Height))
                    throw new PmapFormatException("Mesh rectangle values are outside TMAP limits.");
            }
            if (Layers.Count == 0)
                throw new PmapFormatException("A .pmap must contain at least one layer.");
            if (Layers.Count(layer => layer.IsKeyLayer) != 1)
                throw new PmapFormatException("A .pmap must contain exactly one key layer.");

            var ids = new Dictionary<string, PmapElementKind>(StringComparer.Ordinal);
            var jointReferences = new List<string>();
            foreach (PmapLayer layer in Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.Name))
                    throw new PmapFormatException("Layer name cannot be empty.");
                if (MapFormat.ExceedsUtf8ByteLimit(layer.Name, MapFormat.MaxPascalStringBytes))
                    throw new PmapFormatException("Layer name exceeds 255 UTF-8 bytes: " + layer.Name + ".");
                if (MapFormat.ExceedsUtf8ByteLimit(layer.Comment, MapFormat.MaxLongStringBytes))
                    throw new PmapFormatException("Layer comment exceeds 65535 UTF-8 bytes: " + layer.Name + ".");
                NormalizeColor(layer.Color);
                foreach (PmapElement element in layer.Elements)
                {
                    if (!string.IsNullOrEmpty(element.Id))
                    {
                        if (ids.ContainsKey(element.Id))
                            throw new PmapFormatException("Duplicate element id: " + element.Id + ".");
                        ids.Add(element.Id, element.Kind);
                    }
                    if (!Finite(element.X) || !Finite(element.Y))
                        throw new PmapFormatException("Element coordinates must be finite.");
                    if (!Finite(element.VisualWidth) || !Finite(element.VisualHeight)
                        || element.VisualWidth <= 0 || element.VisualHeight <= 0)
                        throw new PmapFormatException("visualWidth and visualHeight must be positive.");
                    NormalizeColor(element.Color);
                    ValidateElement(element, jointReferences);
                }
            }
            foreach (string chipId in jointReferences)
            {
                if (!ids.TryGetValue(chipId, out PmapElementKind kind))
                    throw new PmapFormatException("Joint references an unknown put id: " + chipId + ".");
                if (kind != PmapElementKind.Chip && kind != PmapElementKind.Picture)
                    throw new PmapFormatException("Joint reference must target a chip or picture, not " + kind + ": " + chipId + ".");
            }
        }

        private void ValidateElement(PmapElement element, List<string> jointReferences)
        {
            if (element.Kind == PmapElementKind.Chip || element.Kind == PmapElementKind.Picture)
            {
                if (string.IsNullOrWhiteSpace(element.Image))
                    throw new PmapFormatException("Every chip/picture must specify an image source.");
                if (!MapFormat.IsValidOpacity(element.Opacity))
                    throw new PmapFormatException("Element opacity must be between 0 and 100.");
                if (!MapFormat.IsValidRotation(element.Rotation))
                    throw new PmapFormatException("Element rotation exceeds the TMAP i16 range.");
                if (element.Flip && element.Opacity == 0)
                    throw new PmapFormatException("TMAP cannot encode a flipped element at exactly 0% opacity.");
                return;
            }

            if (element.Kind == PmapElementKind.LabelPoint || element.Kind == PmapElementKind.Gradation)
            {
                RequirePascalValue(element.Key, element.Kind + " key");
                RequireRect(element);
            }
            if (element.Kind == PmapElementKind.LabelPoint)
            {
                if (!Finite(element.FocusX) || !Finite(element.FocusY))
                    throw new PmapFormatException("Label point focus must be finite.");
                RequireLongValue(element.Command, "Label point command");
                RequireLongValue(element.Comment, "Label point comment");
            }
            else if (element.Kind == PmapElementKind.Gradation)
            {
                if (element.Order < 0 || element.Order > 6 || element.Direction < 0 || element.Direction > 13)
                    throw new PmapFormatException("Gradation order/direction is outside TMAP v4.");
                NormalizeColor(element.StartColor);
                NormalizeColor(element.EndColor);
                ValidateSlicer(element);
            }
            else if (element.Kind == PmapElementKind.SubMap)
            {
                RequirePascalValue(element.TargetMap, "Sub-map target");
                if (element.Order < 0 || element.Order > 3 || element.RepeatX < 0 || element.RepeatX > byte.MaxValue
                    || element.RepeatY < 0 || element.RepeatY > byte.MaxValue)
                    throw new PmapFormatException("Sub-map order/repeat is outside TMAP v4.");
                float[] values = { element.BaseX, element.BaseY, element.ScaleX, element.ScaleY, element.ScrollX,
                    element.ScrollY, element.IntervalX, element.IntervalY, element.CameraLength };
                if (values.Any(value => !Finite(value)))
                    throw new PmapFormatException("Sub-map transform values must be finite.");
            }
            else if (element.Kind == PmapElementKind.Joint)
            {
                if (element.Thickness < 0 || element.Thickness > byte.MaxValue || element.Points.Count > byte.MaxValue)
                    throw new PmapFormatException("Joint thickness/point count is outside TMAP v4.");
                foreach (PmapJointPoint point in element.Points)
                {
                    if (!Finite(point.X) || !Finite(point.Y))
                        throw new PmapFormatException("Joint point coordinates must be finite.");
                    if (!string.IsNullOrEmpty(point.ChipId)) jointReferences.Add(point.ChipId);
                }
            }
        }

        private static void RequireRect(PmapElement element)
        {
            if (!Finite(element.Width) || !Finite(element.Height) || element.Width < 0 || element.Height < 0)
                throw new PmapFormatException(element.Kind + " width/height must be finite and non-negative.");
            double[] pixels = { element.X * MapFormat.CellPixels, element.Y * MapFormat.CellPixels,
                element.Width * MapFormat.CellPixels, element.Height * MapFormat.CellPixels };
            if (pixels.Any(value => value < short.MinValue || value > short.MaxValue))
                throw new PmapFormatException(element.Kind + " rectangle is outside the TMAP i16 pixel range.");
        }

        private static void ValidateSlicer(PmapElement element)
        {
            if (element.Direction != 13)
            {
                if (element.SlicerColumns != 0 || element.SlicerRows != 0 || element.InternalX.Count != 0
                    || element.InternalY.Count != 0 || element.Levels.Count != 0)
                    throw new PmapFormatException("Only SLICER gradations may contain slicer data.");
                return;
            }
            if (element.SlicerColumns < 0 || element.SlicerColumns > byte.MaxValue
                || element.SlicerRows < 0 || element.SlicerRows > byte.MaxValue)
                throw new PmapFormatException("Slicer rows/columns are outside TMAP v4.");
            if (element.SlicerColumns == 0)
            {
                if (element.SlicerRows != 0 || element.InternalX.Count != 0 || element.InternalY.Count != 0 || element.Levels.Count != 0)
                    throw new PmapFormatException("An empty slicer cannot contain rows or samples.");
                return;
            }
            if (element.SlicerColumns < 2 || element.SlicerRows < 2
                || element.InternalX.Count != element.SlicerColumns - 2
                || element.InternalY.Count != element.SlicerRows - 2
                || element.Levels.Count != element.SlicerColumns * element.SlicerRows)
                throw new PmapFormatException("Slicer sample counts do not match rows/columns.");
            if (element.InternalX.Concat(element.InternalY).Concat(element.Levels).Any(value => !Finite(value)))
                throw new PmapFormatException("Slicer samples must be finite.");
        }

        private static void RequirePascalValue(string value, string what)
        {
            if (string.IsNullOrEmpty(value) || MapFormat.ExceedsUtf8ByteLimit(value, MapFormat.MaxPascalStringBytes))
                throw new PmapFormatException(what + " must contain 1..255 UTF-8 bytes.");
        }

        private static void RequireLongValue(string value, string what)
        {
            if (MapFormat.ExceedsUtf8ByteLimit(value ?? "", MapFormat.MaxLongStringBytes))
                throw new PmapFormatException(what + " exceeds 65535 UTF-8 bytes.");
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

            XElement cspNode = root.Elements().FirstOrDefault(x => x.Name.LocalName == "csp");
            if (cspNode != null)
            {
                document.CspExpectedCount = Int(cspNode, "expected", 0);
                foreach (XElement key in cspNode.Elements().Where(x => x.Name.LocalName == "key"))
                    document.CspKeys.Add(Required(key, "value", sourceName));
            }
            XElement additionalNode = root.Elements().FirstOrDefault(x => x.Name.LocalName == "editorAdditional");
            if (additionalNode != null)
                foreach (XElement value in additionalNode.Elements().Where(x => x.Name.LocalName == "value"))
                    document.EditorAdditional.Add(String(value, "text", ""));
            XElement meshNode = root.Elements().FirstOrDefault(x => x.Name.LocalName == "meshRects");
            if (meshNode != null)
            {
                foreach (XElement rect in meshNode.Elements().Where(x => x.Name.LocalName == "rect"))
                {
                    document.MeshRects.Add(new PmapMeshRect
                    {
                        Index = Int(rect, "index", 0), X = Float(rect, "x", 0), Y = Float(rect, "y", 0),
                        Width = Float(rect, "width", 0), Height = Float(rect, "height", 0),
                    });
                }
            }

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
                    else if (node.Name.LocalName == "labelPoint") kind = PmapElementKind.LabelPoint;
                    else if (node.Name.LocalName == "gradation") kind = PmapElementKind.Gradation;
                    else if (node.Name.LocalName == "subMap") kind = PmapElementKind.SubMap;
                    else if (node.Name.LocalName == "joint") kind = PmapElementKind.Joint;
                    else continue;

                    var element = new PmapElement
                    {
                        Kind = kind,
                        Id = String(node, "id", ""),
                        Image = String(node, "image", ""),
                        X = Float(node, "x", 0),
                        Y = Float(node, "y", 0),
                        Rotation = Int(node, "rotation", 0),
                        Flip = Bool(node, "flip", false),
                        Opacity = Int(node, "opacity", 100),
                        PatternId = UInt(node, "pattern", 0),
                        Key = String(node, "key", ""),
                        Width = Float(node, "width", 1),
                        Height = Float(node, "height", 1),
                        FocusX = Float(node, "focusX", 0),
                        FocusY = Float(node, "focusY", 0),
                        Command = String(node, "command", ""),
                        Comment = String(node, "comment", ""),
                        Order = Int(node, "order", 0),
                        Direction = Int(node, "direction", 0),
                        StartColor = String(node, "startColor", "#FFFFFFFF"),
                        EndColor = String(node, "endColor", "#FFFFFF00"),
                        SlicerColumns = Int(node, "columns", 0),
                        SlicerRows = Int(node, "rows", 0),
                        TargetMap = String(node, "target", ""),
                        BaseX = Float(node, "baseX", 0),
                        BaseY = Float(node, "baseY", 0),
                        ScaleX = Float(node, "scaleX", 1),
                        ScaleY = Float(node, "scaleY", 1),
                        ScrollX = Float(node, "scrollX", 0),
                        ScrollY = Float(node, "scrollY", 0),
                        RepeatX = Int(node, "repeatX", 0),
                        RepeatY = Int(node, "repeatY", 0),
                        IntervalX = Float(node, "intervalX", 0),
                        IntervalY = Float(node, "intervalY", 0),
                        CameraLength = Float(node, "cameraLength", 0),
                        Thickness = Int(node, "thickness", 1),
                        VisualWidth = Float(node, "visualWidth", 1),
                        VisualHeight = Float(node, "visualHeight", 1),
                        Color = String(node, "color", DefaultElementColor(kind)),
                        Label = String(node, "label", ""),
                    };
                    AddFloatList(element.InternalX, String(node, "internalX", ""));
                    AddFloatList(element.InternalY, String(node, "internalY", ""));
                    AddFloatList(element.Levels, String(node, "levels", ""));
                    foreach (XElement point in node.Elements().Where(x => x.Name.LocalName == "point"))
                    {
                        element.Points.Add(new PmapJointPoint
                        {
                            X = Float(point, "x", 0), Y = Float(point, "y", 0), ChipId = String(point, "chip", ""),
                        });
                    }
                    layer.Elements.Add(element);
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

        private static uint UInt(XElement node, string name, uint fallback)
        {
            string value = node.Attribute(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
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

        private static void AddFloatList(ICollection<float> target, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (string item in value.Split(','))
                target.Add(float.Parse(item.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        private static string DefaultElementColor(PmapElementKind kind)
        {
            switch (kind)
            {
                case PmapElementKind.Chip: return "#5B6477";
                case PmapElementKind.Picture: return "#B68A5A";
                case PmapElementKind.LabelPoint: return "#4B9B77";
                case PmapElementKind.Gradation: return "#8874B8AA";
                case PmapElementKind.SubMap: return "#4A83A8";
                case PmapElementKind.Joint: return "#E0A048";
                default: return "#7F7F7F";
            }
        }

        private static string F(float value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

        private static string FloatList(IEnumerable<float> values)
            => string.Join(",", values.Select(F));

        private static string ElementName(PmapElementKind kind)
        {
            switch (kind)
            {
                case PmapElementKind.Chip: return "chip";
                case PmapElementKind.Picture: return "picture";
                case PmapElementKind.LabelPoint: return "labelPoint";
                case PmapElementKind.Gradation: return "gradation";
                case PmapElementKind.SubMap: return "subMap";
                case PmapElementKind.Joint: return "joint";
                default: throw new PmapFormatException("Unknown .pmap element kind: " + kind + ".");
            }
        }

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
        LabelPoint,
        Gradation,
        SubMap,
        Joint,
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
        public uint PatternId { get; set; }

        // LP / GRD rectangle and shared runtime key.
        public string Key { get; set; } = "";
        public float Width { get; set; } = 1;
        public float Height { get; set; } = 1;
        public float FocusX { get; set; }
        public float FocusY { get; set; }
        public string Command { get; set; } = "";
        public string Comment { get; set; } = "";

        // GRD.
        public int Order { get; set; }
        public int Direction { get; set; }
        public string StartColor { get; set; } = "#FFFFFFFF";
        public string EndColor { get; set; } = "#FFFFFF00";
        public int SlicerColumns { get; set; }
        public int SlicerRows { get; set; }
        public List<float> InternalX { get; } = new List<float>();
        public List<float> InternalY { get; } = new List<float>();
        public List<float> Levels { get; } = new List<float>();

        // SM.
        public string TargetMap { get; set; } = "";
        public float BaseX { get; set; }
        public float BaseY { get; set; }
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public int RepeatX { get; set; }
        public int RepeatY { get; set; }
        public float IntervalX { get; set; }
        public float IntervalY { get; set; }
        public float CameraLength { get; set; }

        // JOINT.
        public int Thickness { get; set; } = 1;
        public List<PmapJointPoint> Points { get; } = new List<PmapJointPoint>();

        /// <summary>仅供无素材编辑器显示；TMAP 中的实际尺寸仍由 image 指向的芯片定义决定。</summary>
        public float VisualWidth { get; set; } = 1;
        public float VisualHeight { get; set; } = 1;
        public string Color { get; set; } = "#5B6477";
        public string Label { get; set; } = "";
    }

    public sealed class PmapMeshRect
    {
        public int Index { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public sealed class PmapJointPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public string ChipId { get; set; } = "";
    }

    public sealed class PmapFormatException : FormatException
    {
        public PmapFormatException(string message) : base(message) { }
        public PmapFormatException(string message, Exception innerException) : base(message, innerException) { }
    }
}
