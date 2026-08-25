using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using m2d;
using Polaris.Map.Internal;
using UnityEngine;
using XX;

namespace Polaris.Map.Debugging
{
    /// <summary>Minimal, default-skin IMGUI controls for map reload and PNG export.</summary>
    internal static class MapDebugPage
    {
        const string InputFlag = "__PMAP_DBG";
        const int WindowId = 0x504D4150;
        const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Tabs = { "Map Export", "PMap Debug" };

        static readonly FieldInfo CameraMeshes = typeof(M2Camera).GetField("MMRD", InstancePrivate)
            ?? throw new MissingFieldException(typeof(M2Camera).FullName, "MMRD");
        static readonly FieldInfo ActiveCameraMap = typeof(M2Camera).GetField("Md", InstancePrivate)
            ?? throw new MissingFieldException(typeof(M2Camera).FullName, "Md");

        static M2MeshContainer cameraMeshes;
        static GameObject cameraCompositeObject;
        static int cameraCompositeIndex = -1;
        static Material cameraCompositeMaterial;
        static Material shadowFreeCameraCompositeMaterial;
        static Map2d cameraCompositeMap;

        static Rect window = new Rect(40f, 40f, 620f, 610f);
        static Vector2 mapScroll;
        static Vector2 layerScroll;
        static bool open;
        static bool inputHeld;
        static int tab;
        static string selectedKey;
        static string notice = "Ready.";
        static string exportMapId = string.Empty;
        static string exportPath = string.Empty;
        static bool exportEntities = true;
        static bool hideCameraBorderShadow;
        static float exportBoundsExpansionPercent = 20f;
        static MapPngExport exportJob;
        static Map2d analyzedLayerMap;
        static M2MapLayer[] analyzedLayers = Array.Empty<M2MapLayer>();
        static bool[] exportLayerEnabled = Array.Empty<bool>();

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
            hideCameraBorderShadow = false;
            ApplyCameraBorderShadowSwitch();
            Close();
        }

        internal static void Draw()
        {
            ApplyCameraBorderShadowSwitch();
            if (!open) return;
            GUI.depth = -1090;
            window = GUI.Window(WindowId, window, DrawWindow, "PolarisMap Debug (F11)");
        }

        static void DrawWindow(int id)
        {
            try
            {
                MapDebugSnapshot snapshot = MapRuntime.GetDebugSnapshot();
                InitializeFields(snapshot);
                tab = GUILayout.Toolbar(tab, Tabs);
                GUILayout.Space(6f);

                if (tab == 0) DrawExportPage();
                else DrawPmapDebugPage(snapshot);

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(80f))) Close();
                GUILayout.EndHorizontal();

                GUILayout.Space(5f);
                GUILayout.Label(notice ?? string.Empty);
            }
            catch (Exception ex)
            {
                GUILayout.Label(ex.ToString());
            }

            GUI.DragWindow(new Rect(0f, 0f, window.width, 22f));
        }

        static void DrawExportPage()
        {
            RefreshMapLayers();
            GUILayout.Label("Current map: " + (exportMapId ?? "(none)"));

            bool hideBorder = GUILayout.Toggle(
                hideCameraBorderShadow,
                "Remove main_rendered camera shadow");
            if (hideBorder != hideCameraBorderShadow)
            {
                hideCameraBorderShadow = hideBorder;
                ApplyCameraBorderShadowSwitch();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Output PNG", GUILayout.Width(70f));
            exportPath = GUILayout.TextField(exportPath ?? string.Empty);
            if (GUILayout.Button("Default", GUILayout.Width(90f)))
                exportPath = DefaultExportPath(exportMapId);
            GUILayout.EndHorizontal();

            exportEntities = GUILayout.Toggle(exportEntities, "Include player, NPCs and enemies");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Expand bounds", GUILayout.Width(70f));
            exportBoundsExpansionPercent = GUILayout.HorizontalSlider(
                exportBoundsExpansionPercent, 0f, 100f, GUILayout.MinWidth(120f));
            exportBoundsExpansionPercent = Mathf.Round(exportBoundsExpansionPercent);
            GUILayout.Label($"{exportBoundsExpansionPercent:0}%", GUILayout.Width(45f));
            GUILayout.EndHorizontal();

            DrawMapLayers();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(exportJob == null ? "" : exportJob.Status.ToString(), GUILayout.Width(100f));
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled
                && !string.IsNullOrWhiteSpace(exportMapId)
                && (exportJob == null || exportJob.IsFinished);
            if (GUILayout.Button("Export PNG", GUILayout.Width(100f))) StartPngExport();
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }

        static void DrawPmapDebugPage(MapDebugSnapshot snapshot)
        {
            GUILayout.Label("Loaded PolarisMap maps");
            mapScroll = GUILayout.BeginScrollView(mapScroll, GUILayout.Height(360f));
            if (snapshot.Maps.Count == 0)
            {
                GUILayout.Label("No generated .pmap is loaded in this session.");
            }
            else
            {
                foreach (MapDebugEntry entry in snapshot.Maps)
                {
                    GUILayout.BeginHorizontal();
                    bool selected = string.Equals(selectedKey, entry.Key, StringComparison.Ordinal);
                    if (GUILayout.Toggle(selected, entry.Key, "Button")) selectedKey = entry.Key;
                    GUILayout.Label(entry.IsCurrent ? "current" : entry.IsLoading ? "loading" : "", GUILayout.Width(55f));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            MapDebugEntry selectedEntry = Selected(snapshot);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && selectedEntry != null && !selectedEntry.IsLoading;
            if (GUILayout.Button("Reload and enter")) Reload(selectedEntry);
            if (GUILayout.Button("Copy XML"))
            {
                GUIUtility.systemCopyBuffer = selectedEntry.Xml;
                notice = "XML copied for " + selectedEntry.Key + ".";
            }
            GUI.enabled = oldEnabled;
            GUILayout.EndHorizontal();
        }

        static void InitializeFields(MapDebugSnapshot snapshot)
        {
            string current = M2DBase.Instance?.curMap?.key;
            if (!string.Equals(exportMapId, current, StringComparison.Ordinal))
            {
                exportMapId = current;
                exportPath = string.IsNullOrWhiteSpace(current) ? string.Empty : DefaultExportPath(current);
            }
            if (string.IsNullOrWhiteSpace(selectedKey))
                selectedKey = snapshot.Maps.FirstOrDefault(item => item.IsCurrent)?.Key
                              ?? snapshot.Maps.FirstOrDefault()?.Key;
        }

        static void StartPngExport()
        {
            try
            {
                exportJob = MapAPI.ExportMapPng(
                    exportMapId,
                    exportPath,
                    new MapPngExportOptions
                    {
                        EnterMapIfNeeded = false,
                        IncludeEntities = exportEntities,
                        EnabledMapLayerIndices = EnabledLayerIndices(),
                        IncludeDarkOverlay = true,
                        BoundsExpansion = exportBoundsExpansionPercent * 0.01f,
                    });
                notice = "Export started for " + exportJob.MapId + ".";
                exportJob.Finished += result =>
                {
                    notice = result.Status == MapPngExportStatus.Completed
                        ? $"Saved {result.Width}x{result.Height}: {result.OutputPath}"
                        : "Export failed: " + result.Error?.Message;
                    exportPath = result.OutputPath;
                };
            }
            catch (Exception ex)
            {
                notice = "Export rejected: " + ex.Message;
            }
        }

        static void Reload(MapDebugEntry entry)
        {
            if (entry == null) return;
            try
            {
                MapTransition transition = MapRuntime.DebugReload(entry.Key);
                notice = "Reload started for " + entry.Key + ".";
                transition.Finished += result => notice = result.Status == MapTransitionStatus.Completed
                    ? "Reload completed: " + result.TargetKey + "."
                    : "Reload failed: " + result.Error?.Message;
            }
            catch (Exception ex)
            {
                notice = "Reload rejected: " + ex.Message;
            }
        }

        static MapDebugEntry Selected(MapDebugSnapshot snapshot)
        {
            MapDebugEntry entry = snapshot.Maps.FirstOrDefault(
                item => string.Equals(item.Key, selectedKey, StringComparison.Ordinal));
            if (entry != null) return entry;
            entry = snapshot.Maps.FirstOrDefault(item => item.IsCurrent) ?? snapshot.Maps.FirstOrDefault();
            selectedKey = entry?.Key;
            return entry;
        }

        static string DefaultExportPath(string mapId)
        {
            string safe = string.IsNullOrWhiteSpace(mapId) ? "map" : mapId.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PolarisCaptures", safe + ".png"));
        }

        static void HoldInput(bool hold)
        {
            if (inputHeld == hold) return;
            inputHeld = hold;
            if (hold) IN.FlgUiUse.Add(InputFlag);
            else IN.FlgUiUse.Rem(InputFlag);
        }

        static void ApplyCameraBorderShadowSwitch()
        {
            M2DBase world = M2DBase.Instance;
            Map2d currentMap = world?.curMap;
            if (!hideCameraBorderShadow || currentMap == null)
            {
                RestoreCameraComposite();
                return;
            }

            if (cameraCompositeMap != null && !ReferenceEquals(cameraCompositeMap, currentMap))
                RestoreCameraComposite();

            M2Camera camera = world.Cam;
            if (camera == null
                || !ReferenceEquals(ActiveCameraMap.GetValue(camera), currentMap)
                || !ReferenceEquals(camera.CurDgn, currentMap.Dgn))
            {
                RestoreCameraComposite();
                return;
            }
            M2MeshContainer meshes = camera == null
                ? null
                : CameraMeshes.GetValue(camera) as M2MeshContainer;
            int index = FindCameraMesh(meshes, "MpMMRD- main_rendered");
            if (meshes == null || index < 0)
            {
                RestoreCameraComposite();
                return;
            }

            GameObject compositeObject = meshes.GetGob(index);
            if (cameraMeshes != meshes
                || cameraCompositeIndex != index
                || cameraCompositeObject != compositeObject
                || (shadowFreeCameraCompositeMaterial != null
                    && meshes.getMaterial(index) != shadowFreeCameraCompositeMaterial))
            {
                RestoreCameraComposite();

                Material original = meshes.getMaterial(index);
                if (original == null
                    || original.shader == null
                    || original.shader.name != "M2d/ImageWithLight")
                    return;

                Shader noFadeShader = CameraCompositeShaderLoader.GetNoCameraFadeShader();
                if (noFadeShader == null)
                {
                    notice = "Camera shadow shader could not be loaded.";
                    return;
                }

                cameraMeshes = meshes;
                cameraCompositeObject = compositeObject;
                cameraCompositeIndex = index;
                cameraCompositeMaterial = original;
                cameraCompositeMap = currentMap;
                shadowFreeCameraCompositeMaterial = new Material(noFadeShader)
                {
                    name = "PolarisMap Shadow-Free Camera Composite",
                    hideFlags = HideFlags.HideAndDontSave
                };
                shadowFreeCameraCompositeMaterial.CopyPropertiesFromMaterial(original);
                meshes.setMaterial(index, shadowFreeCameraCompositeMaterial);
            }

            // DungeonBright keeps updating the original material as lighting/weather changes.
            // Mirror those property values while retaining our replacement shader.
            if (cameraCompositeMap == null
                || !ReferenceEquals(cameraCompositeMap, currentMap)
                || shadowFreeCameraCompositeMaterial == null
                || cameraCompositeMaterial == null)
            {
                RestoreCameraComposite();
                return;
            }

            shadowFreeCameraCompositeMaterial.CopyPropertiesFromMaterial(cameraCompositeMaterial);
            if (meshes.getMaterial(index) != shadowFreeCameraCompositeMaterial)
                meshes.setMaterial(index, shadowFreeCameraCompositeMaterial);
        }

        static int FindCameraMesh(M2MeshContainer meshes, string name)
        {
            if (meshes == null) return -1;
            for (int i = 0; i < meshes.Length; i++)
            {
                GameObject target = meshes.GetGob(i);
                if (target != null && target.name == name) return i;
            }
            return -1;
        }

        static void RestoreCameraComposite()
        {
            if (cameraMeshes != null
                && cameraCompositeIndex >= 0
                && cameraCompositeIndex < cameraMeshes.Length
                && cameraCompositeObject != null
                && cameraMeshes.GetGob(cameraCompositeIndex) == cameraCompositeObject
                && cameraMeshes.getMaterial(cameraCompositeIndex) == shadowFreeCameraCompositeMaterial
                && cameraCompositeMaterial != null)
                cameraMeshes.setMaterial(cameraCompositeIndex, cameraCompositeMaterial);

            if (shadowFreeCameraCompositeMaterial != null)
                UnityEngine.Object.Destroy(shadowFreeCameraCompositeMaterial);

            cameraMeshes = null;
            cameraCompositeObject = null;
            cameraCompositeIndex = -1;
            cameraCompositeMaterial = null;
            shadowFreeCameraCompositeMaterial = null;
            cameraCompositeMap = null;
        }

        static void RefreshMapLayers()
        {
            Map2d current = M2DBase.Instance?.curMap;
            M2MapLayer[] currentLayers = current?.getLayerArray() ?? Array.Empty<M2MapLayer>();
            if (ReferenceEquals(analyzedLayerMap, current)
                && analyzedLayers.Length == currentLayers.Length)
            {
                bool unchanged = true;
                for (int i = 0; i < currentLayers.Length; i++)
                    if (!ReferenceEquals(analyzedLayers[i], currentLayers[i]))
                    {
                        unchanged = false;
                        break;
                    }
                if (unchanged) return;
            }

            analyzedLayerMap = current;
            analyzedLayers = currentLayers;
            exportLayerEnabled = new bool[analyzedLayers.Length];
            for (int i = 0; i < analyzedLayers.Length; i++)
                exportLayerEnabled[i] = analyzedLayers[i] != null && analyzedLayers[i].visible;
            layerScroll = Vector2.zero;
        }

        static void DrawMapLayers()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map layers", GUILayout.Width(70f));
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && analyzedLayers.Length != 0;
            if (GUILayout.Button("All", GUILayout.Width(48f)))
                for (int i = 0; i < exportLayerEnabled.Length; i++) exportLayerEnabled[i] = true;
            if (GUILayout.Button("None", GUILayout.Width(48f)))
                for (int i = 0; i < exportLayerEnabled.Length; i++) exportLayerEnabled[i] = false;
            GUI.enabled = oldEnabled;
            GUILayout.EndHorizontal();

            layerScroll = GUILayout.BeginScrollView(layerScroll, GUILayout.Height(120f));
            if (analyzedLayers.Length == 0)
            {
                GUILayout.Label("No active map layers.");
            }
            else
            {
                for (int i = 0; i < analyzedLayers.Length; i++)
                {
                    M2MapLayer layer = analyzedLayers[i];
                    string name = layer == null || string.IsNullOrEmpty(layer.name)
                        ? "(unnamed)"
                        : layer.name;
                    exportLayerEnabled[i] = GUILayout.Toggle(
                        exportLayerEnabled[i], $"[{i}] {name}");
                }
            }
            GUILayout.EndScrollView();
        }

        static int[] EnabledLayerIndices()
        {
            var enabled = new List<int>(exportLayerEnabled.Length);
            for (int i = 0; i < exportLayerEnabled.Length; i++)
                if (exportLayerEnabled[i]) enabled.Add(i);
            return enabled.ToArray();
        }

        static void ClampWindow()
        {
            float width = Mathf.Min(window.width, Mathf.Max(260f, Screen.width - 20f));
            float height = Mathf.Min(window.height, Mathf.Max(220f, Screen.height - 20f));
            window = new Rect(
                Mathf.Clamp(window.x, 0f, Math.Max(0f, Screen.width - width)),
                Mathf.Clamp(window.y, 0f, Math.Max(0f, Screen.height - height)),
                width,
                height);
        }

    }
}
