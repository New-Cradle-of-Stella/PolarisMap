using System.Collections.Generic;
using UnityEngine;

namespace Polaris.Map.Debugging
{
    /// <summary>F11 检查台的 IMGUI 皮肤：一次性建好字体、纯色底图和各号样式，关页时连纹理一起释放。</summary>
    internal static class MapDebugStyles
    {
        static readonly string[] BodyFonts = { "Microsoft YaHei UI", "Microsoft YaHei", "Meiryo", "Segoe UI" };
        static readonly string[] MonoFonts = { "Cascadia Mono", "Consolas", "Courier New" };
        static readonly List<Texture2D> Textures = new();

        static bool ready;

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

        internal static void Ensure()
        {
            if (ready) return;
            ready = true;
            Font body = CreateFont(BodyFonts, 13);
            Font mono = CreateFont(MonoFonts, 12);

            Texture2D graphite = SolidTexture(new Color(.105f, .125f, .15f));
            Texture2D slate = SolidTexture(new Color(.145f, .17f, .20f));
            Texture2D raised = SolidTexture(new Color(.205f, .235f, .27f));
            Texture2D cyan = SolidTexture(new Color(.50f, .72f, .79f));
            Texture2D amber = SolidTexture(new Color(.89f, .66f, .28f));
            Texture2D metric = SolidTexture(new Color(.12f, .145f, .17f));

            Window = Style(GUI.skin.window, body, Color.white, graphite);
            Window.padding = new RectOffset(10, 10, 25, 9);
            Panel = Style(GUI.skin.box, body, Color.white, slate);
            Panel.padding = new RectOffset(9, 9, 9, 9);
            Button = Style(GUI.skin.button, body, new Color(.89f, .92f, .94f), raised);
            SelectedButton = Style(GUI.skin.button, body, new Color(.08f, .12f, .15f), cyan);
            ReloadButton = Style(GUI.skin.button, body, new Color(.11f, .09f, .05f), amber);
            Header = Label(body, new Color(.78f, .88f, .93f), 18, FontStyle.Bold);
            Eyebrow = Label(mono, new Color(.50f, .72f, .79f), 10, FontStyle.Bold);
            Dim = Label(body, new Color(.60f, .65f, .69f), 12);
            MonoDim = Label(mono, new Color(.57f, .64f, .68f), 11);
            MonoValue = Label(mono, new Color(.94f, .96f, .97f), 16, FontStyle.Bold);
            Current = Label(mono, new Color(.58f, .85f, .70f), 12, FontStyle.Bold);
            Marker = Label(mono, new Color(.89f, .66f, .28f), 11, FontStyle.Bold);
            Small = Label(body, new Color(.61f, .66f, .70f), 10);
            Small.wordWrap = true;
            Empty = Label(body, new Color(.67f, .73f, .77f), 14);
            Empty.wordWrap = true;
            Error = Label(body, new Color(1f, .47f, .39f), 12);
            Error.wordWrap = true;
            Status = Label(body, new Color(.78f, .83f, .86f), 11);
            Status.wordWrap = true;
            Metric = Style(GUI.skin.box, body, Color.white, metric);
            Metric.padding = new RectOffset(7, 7, 5, 5);
            SourceFrame = Style(GUI.skin.box, mono, Color.white, metric);
            SourceFrame.padding = new RectOffset(3, 3, 3, 3);
            Source = Style(GUI.skin.textArea, mono, new Color(.82f, .88f, .91f), graphite);
            Source.wordWrap = false;
            BlockLight = Block(mono, Color.white);
            BlockDark = Block(mono, new Color(.08f, .10f, .12f));
        }

        internal static void Release()
        {
            foreach (Texture2D texture in Textures)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            Textures.Clear();
            ready = false;
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

        /// <summary>缩略图色块上的居中标签，超出色块直接裁掉。</summary>
        static GUIStyle Block(Font font, Color color)
        {
            GUIStyle style = Label(font, color, 10, FontStyle.Bold);
            style.alignment = TextAnchor.MiddleCenter;
            style.clipping = TextClipping.Clip;
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
            Textures.Add(texture);
            return texture;
        }
    }
}
