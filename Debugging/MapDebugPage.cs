using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Polaris.Map.Authoring;
using Polaris.Map.Internal;
using UnityEngine;
using XX;

namespace Polaris.Map.Debugging
{
    /// <summary>F11 打开的 PolarisMap 专用 IMGUI 检查台；只画 .pmap 抽象蓝图，不接触游戏贴图。</summary>
    internal static class MapDebugPage
    {
        const string InputFlag = "__PMAP_DBG";
        const int WindowId = 0x504D4150; // PMAP
        static readonly string[] Tabs = { "Blueprint", "XML" };
        static readonly List<Texture2D> StyleTextures = new();

        static Rect window = new Rect(48f, 42f, 980f, 650f);
        static Vector2 mapScroll;
        static Vector2 xmlScroll;
        static bool open;
        static bool inputHeld;
        static bool stylesReady;
        static int tab;
        static string selectedKey;
        static string notice = "F11 closes this page.";

        internal static void Toggle()
        {
            if (open) Close();
            else Open();
        }

        internal static void Open()
        {
            if (open || !MapDebugRuntime.IsEnabled) return;
            open = true;
            ClampWindow();
            HoldInput(true);
        }

        internal static void Close()
        {
            if (!open) return;
            open = false;
            HoldInput(false);
        }

        internal static void Shutdown()
        {
            Close();
            foreach (Texture2D texture in StyleTextures)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            StyleTextures.Clear();
            stylesReady = false;
        }

        internal static void Draw()
        {
            if (!open) return;
            EnsureStyles();
            GUI.depth = -1090;
            window = GUI.Window(WindowId, window, DrawWindow, "POLARIS MAP / F11", Styles.Window);
        }

        static void DrawWindow(int id)
        {
            MapDebugSnapshot snapshot;
            try
            {
                snapshot = MapRuntime.GetDebugSnapshot();
            }
            catch (Exception ex)
            {
                GUILayout.Label(ex.Message, Styles.Error);
                GUI.DragWindow(new Rect(0, 0, window.width, 24));
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("MAP SESSION", Styles.Eyebrow, GUILayout.Width(100f));
            GUILayout.Label(snapshot.CurrentKey == null ? "No active map" : "Current  " + snapshot.CurrentKey,
                snapshot.CurrentKey == null ? Styles.Dim : Styles.Current);
            GUILayout.FlexibleSpace();
            GUILayout.Label(snapshot.CapturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Styles.MonoDim);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            DrawMapList(snapshot);
            GUILayout.Space(8f);
            DrawInspector(snapshot);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(notice) ? snapshot.Activity : notice, Styles.Status);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  F11", Styles.Button, GUILayout.Width(108f))) Close();
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, window.width, 24f));
        }

        static void DrawMapList(MapDebugSnapshot snapshot)
        {
            float height = Mathf.Max(160f, window.height - 94f);
            GUILayout.BeginVertical(Styles.Panel, GUILayout.Width(250f), GUILayout.Height(height));
            GUILayout.Label("LOADED .PMAP", Styles.Eyebrow);
            GUILayout.Label(snapshot.Maps.Count == 0
                ? "No generated .pmap has entered this session."
                : snapshot.Maps.Count + " managed map(s)", Styles.Dim);
            GUILayout.Space(5f);

            mapScroll = GUILayout.BeginScrollView(mapScroll);
            foreach (MapDebugEntry entry in snapshot.Maps)
            {
                bool selected = string.Equals(selectedKey, entry.Key, StringComparison.Ordinal);
                string prefix = entry.IsLoading ? "↻  " : entry.IsCurrent ? "●  " : "○  ";
                if (GUILayout.Button(prefix + entry.Key, selected ? Styles.SelectedButton : Styles.Button))
                {
                    selectedKey = entry.Key;
                    xmlScroll = Vector2.zero;
                    notice = "Selected " + entry.Key + ".";
                }
                GUILayout.Label(
                    $"{entry.Document.Width}×{entry.Document.Height}  ·  {entry.Document.Layers.Count} layers  ·  {entry.ElementCount} elements",
                    Styles.MonoDim);
                GUILayout.Space(4f);
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.Label("[PMapHotFixEnabled]", Styles.Marker);
            GUILayout.Label("F11 and the debug pipe are enabled because a plugin assembly carries this marker.", Styles.Small);
            GUILayout.EndVertical();
        }

        static void DrawInspector(MapDebugSnapshot snapshot)
        {
            float height = Mathf.Max(160f, window.height - 94f);
            GUILayout.BeginVertical(Styles.Panel, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            MapDebugEntry entry = Selected(snapshot);
            if (entry == null)
            {
                GUILayout.Label("NO PMAP SELECTED", Styles.Header);
                GUILayout.Space(8f);
                GUILayout.Label(snapshot.Maps.Count == 0
                    ? "Load a generated .pmap through PolarisMap, then return here. The page will show its abstract blueprint without reading game textures."
                    : "Choose a managed map on the left to inspect or reload it.", Styles.Empty);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(entry.Key, Styles.Header);
            GUILayout.Label("owner  " + entry.Owner, Styles.MonoDim);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            tab = GUILayout.Toolbar(tab, Tabs, Styles.Button, GUILayout.Width(220f));
            GUILayout.EndHorizontal();
            GUILayout.Space(7f);

            if (tab == 0) DrawBlueprint(entry);
            else DrawXml(entry);

            GUILayout.Space(7f);
            DrawActions(entry);
            GUILayout.EndVertical();
        }

        static void DrawBlueprint(MapDebugEntry entry)
        {
            PmapDocument document = entry.Document;
            GUILayout.BeginHorizontal();
            GUILayout.Label("ABSTRACT MAP", Styles.Eyebrow);
            GUILayout.FlexibleSpace();
            GUILayout.Label("color blocks + labels · no game assets", Styles.MonoDim);
            GUILayout.EndHorizontal();

            float canvasHeight = Mathf.Max(220f, window.height - 315f);
            Rect canvas = GUILayoutUtility.GetRect(320f, canvasHeight, GUILayout.ExpandWidth(true));
            DrawMiniMap(canvas, document);

            GUILayout.Space(7f);
            GUILayout.BeginHorizontal();
            Metric("SIZE", document.Width + " × " + document.Height);
            Metric("LAYERS", document.Layers.Count.ToString(CultureInfo.InvariantCulture));
            Metric("ELEMENTS", entry.ElementCount.ToString(CultureInfo.InvariantCulture));
            Metric("STATE", entry.IsLoading ? "LOADING" : entry.IsCurrent ? "CURRENT" : "READY");
            GUILayout.EndHorizontal();
        }

        static void DrawMiniMap(Rect outer, PmapDocument document)
        {
            DrawSolid(outer, new Color(0.09f, 0.11f, 0.13f, 1f));
            float padding = 14f;
            float availableW = Mathf.Max(1f, outer.width - padding * 2f);
            float availableH = Mathf.Max(1f, outer.height - padding * 2f);
            float scale = Mathf.Min(availableW / document.Width, availableH / document.Height);
            var map = new Rect(
                outer.x + (outer.width - document.Width * scale) * .5f,
                outer.y + (outer.height - document.Height * scale) * .5f,
                document.Width * scale,
                document.Height * scale);

            DrawSolid(map, ParseColor(document.Background, new Color(.78f, .86f, .89f, 1f)));
            Color grid = IsDark(ParseColor(document.Background, Color.gray))
                ? new Color(1f, 1f, 1f, .11f)
                : new Color(.10f, .14f, .17f, .15f);
            int xStep = Math.Max(1, document.Width / 40);
            int yStep = Math.Max(1, document.Height / 30);
            for (int x = 0; x <= document.Width; x += xStep)
                DrawSolid(new Rect(map.x + x * scale, map.y, 1f, map.height), grid);
            for (int y = 0; y <= document.Height; y += yStep)
                DrawSolid(new Rect(map.x, map.y + y * scale, map.width, 1f), grid);

            foreach (PmapLayer layer in document.Layers)
            {
                foreach (PmapElement element in layer.Elements)
                {
                    var rect = new Rect(
                        map.x + element.X * scale,
                        map.y + element.Y * scale,
                        Mathf.Max(2f, element.VisualWidth * scale),
                        Mathf.Max(2f, element.VisualHeight * scale));
                    rect = Intersect(rect, map);
                    if (rect.width <= 0 || rect.height <= 0) continue;
                    Color color = ParseColor(element.Color,
                        element.Kind == PmapElementKind.Chip
                            ? new Color(.36f, .40f, .47f, 1f)
                            : new Color(.71f, .54f, .35f, 1f));
                    color.a *= Mathf.Max(.2f, element.Opacity / 100f);
                    DrawSolid(rect, color);
                    if (rect.width >= 44f && rect.height >= 15f)
                    {
                        string label = string.IsNullOrWhiteSpace(element.Label)
                            ? Path.GetFileNameWithoutExtension(element.Image)
                            : element.Label;
                        GUI.Label(rect, label, IsDark(color) ? Styles.BlockLight : Styles.BlockDark);
                    }
                }
            }
            Color frame = new Color(.50f, .72f, .79f, .9f);
            DrawSolid(new Rect(map.x, map.y, map.width, 1f), frame);
            DrawSolid(new Rect(map.x, map.yMax - 1f, map.width, 1f), frame);
            DrawSolid(new Rect(map.x, map.y, 1f, map.height), frame);
            DrawSolid(new Rect(map.xMax - 1f, map.y, 1f, map.height), frame);
        }

        static void DrawXml(MapDebugEntry entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("CANONICAL SOURCE", Styles.Eyebrow);
            GUILayout.FlexibleSpace();
            GUILayout.Label(entry.Xml.Length + " chars", Styles.MonoDim);
            GUILayout.EndHorizontal();
            xmlScroll = GUILayout.BeginScrollView(xmlScroll, Styles.SourceFrame,
                GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            bool enabled = GUI.enabled;
            GUI.enabled = false;
            GUILayout.TextArea(entry.Xml, Styles.Source, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            GUI.enabled = enabled;
            GUILayout.EndScrollView();
        }

        static void DrawActions(MapDebugEntry entry)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = !entry.IsLoading;
            if (GUILayout.Button("Full reload & enter", Styles.ReloadButton, GUILayout.Width(175f)))
            {
                try
                {
                    MapTransition transition = MapRuntime.DebugReload(entry.Key);
                    notice = "Full reload started for " + entry.Key + ".";
                    transition.Finished += result => notice = result.Status == MapTransitionStatus.Completed
                        ? "Full reload completed: " + result.TargetKey + "."
                        : "Full reload failed: " + result.Error?.Message;
                }
                catch (Exception ex)
                {
                    notice = "Reload rejected: " + ex.Message;
                }
            }
            GUI.enabled = true;
            if (GUILayout.Button("Copy XML", Styles.Button, GUILayout.Width(95f)))
            {
                GUIUtility.systemCopyBuffer = entry.Xml;
                notice = "Copied " + entry.Key + ".pmap XML.";
            }
            GUILayout.Space(8f);
            GUILayout.Label("Reload closes the old Map2d, releases its layers, creates a new instance and runs async map loading.", Styles.Small);
            GUILayout.EndHorizontal();
        }

        static void Metric(string name, string value)
        {
            GUILayout.BeginVertical(Styles.Metric, GUILayout.MinWidth(90f));
            GUILayout.Label(name, Styles.Eyebrow);
            GUILayout.Label(value, Styles.MonoValue);
            GUILayout.EndVertical();
        }

        static MapDebugEntry Selected(MapDebugSnapshot snapshot)
        {
            if (snapshot.Maps.Count == 0) return null;
            MapDebugEntry selected = snapshot.Maps.FirstOrDefault(
                item => string.Equals(item.Key, selectedKey, StringComparison.Ordinal));
            if (selected != null) return selected;
            selected = snapshot.Maps.FirstOrDefault(item => item.IsCurrent) ?? snapshot.Maps[0];
            selectedKey = selected.Key;
            return selected;
        }

        static void HoldInput(bool hold)
        {
            if (inputHeld == hold) return;
            inputHeld = hold;
            if (hold) IN.FlgUiUse.Add(InputFlag);
            else IN.FlgUiUse.Rem(InputFlag);
        }

        static void ClampWindow()
        {
            float width = Mathf.Min(window.width, Screen.width - 20f);
            float height = Mathf.Min(window.height, Screen.height - 20f);
            window = new Rect(
                Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width - width)),
                Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height - height)),
                width, height);
        }

        static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            Font body = CreateFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Meiryo", "Segoe UI" }, 13);
            Font mono = CreateFont(new[] { "Cascadia Mono", "Consolas", "Courier New" }, 12);

            Texture2D graphite = SolidTexture(new Color(.105f, .125f, .15f));
            Texture2D slate = SolidTexture(new Color(.145f, .17f, .20f));
            Texture2D raised = SolidTexture(new Color(.205f, .235f, .27f));
            Texture2D cyan = SolidTexture(new Color(.50f, .72f, .79f));
            Texture2D amber = SolidTexture(new Color(.89f, .66f, .28f));
            Texture2D metric = SolidTexture(new Color(.12f, .145f, .17f));

            Styles.Window = Style(GUI.skin.window, body, Color.white, graphite);
            Styles.Window.padding = new RectOffset(10, 10, 25, 9);
            Styles.Panel = Style(GUI.skin.box, body, Color.white, slate);
            Styles.Panel.padding = new RectOffset(9, 9, 9, 9);
            Styles.Button = Style(GUI.skin.button, body, new Color(.89f, .92f, .94f), raised);
            Styles.SelectedButton = Style(GUI.skin.button, body, new Color(.08f, .12f, .15f), cyan);
            Styles.ReloadButton = Style(GUI.skin.button, body, new Color(.11f, .09f, .05f), amber);
            Styles.Header = Label(body, new Color(.78f, .88f, .93f), 18, FontStyle.Bold);
            Styles.Eyebrow = Label(mono, new Color(.50f, .72f, .79f), 10, FontStyle.Bold);
            Styles.Dim = Label(body, new Color(.60f, .65f, .69f), 12);
            Styles.MonoDim = Label(mono, new Color(.57f, .64f, .68f), 11);
            Styles.MonoValue = Label(mono, new Color(.94f, .96f, .97f), 16, FontStyle.Bold);
            Styles.Current = Label(mono, new Color(.58f, .85f, .70f), 12, FontStyle.Bold);
            Styles.Marker = Label(mono, new Color(.89f, .66f, .28f), 11, FontStyle.Bold);
            Styles.Small = Label(body, new Color(.61f, .66f, .70f), 10);
            Styles.Small.wordWrap = true;
            Styles.Empty = Label(body, new Color(.67f, .73f, .77f), 14);
            Styles.Empty.wordWrap = true;
            Styles.Error = Label(body, new Color(1f, .47f, .39f), 12);
            Styles.Error.wordWrap = true;
            Styles.Status = Label(body, new Color(.78f, .83f, .86f), 11);
            Styles.Status.wordWrap = true;
            Styles.Metric = Style(GUI.skin.box, body, Color.white, metric);
            Styles.Metric.padding = new RectOffset(7, 7, 5, 5);
            Styles.SourceFrame = Style(GUI.skin.box, mono, Color.white, metric);
            Styles.SourceFrame.padding = new RectOffset(3, 3, 3, 3);
            Styles.Source = Style(GUI.skin.textArea, mono, new Color(.82f, .88f, .91f), graphite);
            Styles.Source.wordWrap = false;
            Styles.BlockLight = Label(mono, Color.white, 10, FontStyle.Bold);
            Styles.BlockLight.alignment = TextAnchor.MiddleCenter;
            Styles.BlockLight.clipping = TextClipping.Clip;
            Styles.BlockDark = Label(mono, new Color(.08f, .10f, .12f), 10, FontStyle.Bold);
            Styles.BlockDark.alignment = TextAnchor.MiddleCenter;
            Styles.BlockDark.clipping = TextClipping.Clip;
        }

        static GUIStyle Style(GUIStyle basis, Font font, Color text, Texture2D background)
        {
            var style = new GUIStyle(basis) { font = font };
            style.normal.textColor = text;
            style.normal.background = background;
            style.hover.textColor = text;
            style.hover.background = background;
            style.active.textColor = text;
            style.active.background = background;
            style.focused.textColor = text;
            style.focused.background = background;
            return style;
        }

        static GUIStyle Label(Font font, Color color, int size, FontStyle fontStyle = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                fontStyle = fontStyle,
            };
            style.normal.textColor = color;
            return style;
        }

        static Font CreateFont(string[] names, int size)
        {
            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(names, size);
                if (font != null) font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return font;
            }
            catch { return null; }
        }

        static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            StyleTextures.Add(texture);
            return texture;
        }

        static void DrawSolid(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static Color ParseColor(string value, Color fallback)
        {
            try
            {
                string text = PmapDocument.NormalizeColor(value).Substring(1);
                byte r = byte.Parse(text.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte a = text.Length == 8
                    ? byte.Parse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : byte.MaxValue;
                return new Color32(r, g, b, a);
            }
            catch { return fallback; }
        }

        static bool IsDark(Color color)
            => .2126f * color.r + .7152f * color.g + .0722f * color.b < .55f;

        static Rect Intersect(Rect a, Rect b)
        {
            float left = Mathf.Max(a.xMin, b.xMin);
            float top = Mathf.Max(a.yMin, b.yMin);
            float right = Mathf.Min(a.xMax, b.xMax);
            float bottom = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(left, top, Mathf.Max(left, right), Mathf.Max(top, bottom));
        }

        static class Styles
        {
            internal static GUIStyle Window;
            internal static GUIStyle Panel;
            internal static GUIStyle Button;
            internal static GUIStyle SelectedButton;
            internal static GUIStyle ReloadButton;
            internal static GUIStyle Header;
            internal static GUIStyle Eyebrow;
            internal static GUIStyle Dim;
            internal static GUIStyle MonoDim;
            internal static GUIStyle MonoValue;
            internal static GUIStyle Current;
            internal static GUIStyle Marker;
            internal static GUIStyle Small;
            internal static GUIStyle Empty;
            internal static GUIStyle Error;
            internal static GUIStyle Status;
            internal static GUIStyle Metric;
            internal static GUIStyle SourceFrame;
            internal static GUIStyle Source;
            internal static GUIStyle BlockLight;
            internal static GUIStyle BlockDark;
        }
    }
}
