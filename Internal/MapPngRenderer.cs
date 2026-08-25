using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using m2d;
using Polaris.Map.Debugging;
using UnityEngine;
using XX;

namespace Polaris.Map.Internal
{
    internal sealed class MapPngCapture
    {
        internal byte[] Png;
        internal int Width;
        internal int Height;
    }

    /// <summary>
    /// 把游戏现有 M2Camera 完整合成管线的各级 RT 临时重定向到整图尺寸，再做一次单摄像机中心渲染。
    /// 不复制图层、不拼块，也不重新初始化 Dungeon；finally 会恢复全部 live pipeline 引用。
    /// </summary>
    internal static class MapPngRenderer
    {
        const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;

        static readonly FieldInfo InW = RequireField(typeof(IN), "w", StaticPublic);
        static readonly FieldInfo InH = RequireField(typeof(IN), "h", StaticPublic);
        static readonly FieldInfo AreaW = RequireField(typeof(M2Camera), "areaw", InstancePrivate);
        static readonly FieldInfo AreaH = RequireField(typeof(M2Camera), "areah", InstancePrivate);
        static readonly FieldInfo CameraScale = RequireField(typeof(M2Camera), "scale", InstancePrivate);
        static readonly FieldInfo CameraPosCenter = RequireField(typeof(M2Camera), "pos_center", InstancePrivate);
        static readonly FieldInfo CameraScaleStart = RequireField(typeof(M2Camera), "anm_scale_s", InstancePrivate);
        static readonly FieldInfo CameraScaleDestination = RequireField(typeof(M2Camera), "anm_scale_d", InstancePrivate);
        static readonly FieldInfo CameraScaleTime = RequireField(typeof(M2Camera), "anm_scale_t", InstancePrivate);
        static readonly FieldInfo CameraScaleMaxTime = RequireField(typeof(M2Camera), "anm_scale_maxt", InstancePrivate);
        static readonly FieldInfo CameraMap = RequireField(typeof(M2Camera), "Md", InstancePrivate);
        static readonly FieldInfo CameraBases = RequireField(typeof(M2Camera), "AXcBase", InstancePrivate);
        static readonly FieldInfo CameraCollectors = RequireField(typeof(M2Camera), "ACam", InstancePrivate);
        static readonly FieldInfo CameraBindings = RequireField(typeof(XCameraBase), "ABind", InstancePrivate);
        static readonly FieldInfo PosEffectiveScale = RequireField(typeof(M2Camera), "PosEffectiveScaleMul", InstancePrivate);
        static readonly FieldInfo FinalCameraCache = RequireField(typeof(M2Camera), "TxFinalCameraCache", InstancePrivate);
        static readonly FieldInfo MutualTextures = RequireField(typeof(CameraComponentCollecter), "AMatualTexture", InstancePrivate);
        static readonly FieldInfo XCameraTexture = RequireField(typeof(XCameraTx), "Tx", InstancePrivate);
        static readonly FieldInfo MoverBinding = RequireField(typeof(M2MovRenderContainer), "binding", InstancePrivate);
        static readonly FieldInfo MoverTickets = RequireField(typeof(M2MovRenderContainer), "AADob", InstancePrivate);

        internal static MapPngCapture Capture(M2DBase m2d, Map2d map, MapPngExportOptions options)
        {
            if (m2d == null) throw new ArgumentNullException(nameof(m2d));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!ReferenceEquals(m2d.curMap, map))
                throw new InvalidOperationException("Only the active game map can be rendered.");
            if (m2d.Cam == null || map.Dgn == null)
                throw new InvalidOperationException("The active map camera or Dungeon is not initialized yet.");
            if (!ReferenceEquals(CameraMap.GetValue(m2d.Cam), map)
                || !ReferenceEquals(m2d.Cam.CurDgn, map.Dgn))
                throw new InvalidOperationException(
                    $"The camera is still switching to map '{map.key}'. Wait until the map is fully visible, then export again.");

            int paddingX = Mathf.CeilToInt(Math.Max(1, map.width) * options.BoundsExpansion * .5f);
            int paddingY = Mathf.CeilToInt(Math.Max(1, map.height) * options.BoundsExpansion * .5f);
            int width = checked(Math.Max(1, map.width) + paddingX * 2);
            int height = checked(Math.Max(1, map.height) + paddingY * 2);
            int maxTexture = Math.Max(1, SystemInfo.maxTextureSize);
            if (width + 16 > maxTexture || height + 16 > maxTexture)
                throw new InvalidOperationException(
                    $"Map '{map.key}' is {width}x{height}, which exceeds the GPU single-render texture limit " +
                    $"({maxTexture - 16}x{maxTexture - 16} after the game's 8px margins). " +
                    "This exporter deliberately does not use tile stitching.");

            var session = new CaptureSession(m2d, m2d.Cam, map, width, height, options);
            Texture2D readable = null;
            try
            {
                session.Apply();

                M2Camera camera = m2d.Cam;
                camera.need_initialize_draw = true;
                camera.MovRender.setRedrawFlag();
                if (!camera.RenderWholeCamera(true, true))
                {
                    // RenderWholeCamera's bool only describes the finalize mesh update; a valid RT
                    // is the authoritative success signal, so continue and validate it below.
                }
                GL.Flush();

                RenderTexture finalized = camera.getFinalizeExportTexture();
                if (finalized == null)
                    throw new InvalidOperationException("The game camera did not produce a finalized RenderTexture.");
                if (finalized.width < width || finalized.height < height)
                    throw new InvalidOperationException(
                        $"The finalized camera texture is only {finalized.width}x{finalized.height}; expected at least {width}x{height}.");

                session.SetReadTarget(finalized);
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                int sourceX = (finalized.width - width) / 2;
                int sourceY = (finalized.height - height) / 2;
                readable.ReadPixels(new Rect(sourceX, sourceY, width, height), 0, 0, false);
                readable.Apply(false, false);
                byte[] png = readable.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("Unity returned an empty PNG payload.");

                return new MapPngCapture { Png = png, Width = width, Height = height };
            }
            finally
            {
                if (readable != null) UnityEngine.Object.Destroy(readable);
                session.Dispose();
            }
        }

        static FieldInfo RequireField(Type type, string name, BindingFlags flags)
            => type.GetField(name, flags)
               ?? throw new MissingFieldException(type.FullName, name);

        sealed class CaptureSession : IDisposable
        {
            readonly M2DBase m2d;
            readonly M2Camera camera;
            readonly Map2d map;
            readonly int targetWidth;
            readonly int targetHeight;
            readonly bool includeEntities;
            readonly HashSet<int> enabledLayerIndices;
            readonly int excludedLayerMask;
            readonly bool removeDarkOverlay;
            readonly HashSet<string> excludedRenderPasses;
            readonly float logicalWidth;
            readonly float logicalHeight;
            readonly float oldWh;
            readonly float oldHh;
            readonly int oldScreenWidth;
            readonly int oldScreenHeight;
            readonly float oldAreaWidth;
            readonly float oldAreaHeight;
            readonly float oldX;
            readonly float oldY;
            readonly float oldDestinationX;
            readonly float oldDestinationY;
            readonly float oldScale;
            readonly object oldPosCenter;
            readonly float oldScaleStart;
            readonly float oldScaleDestination;
            readonly int oldScaleTime;
            readonly int oldScaleMaxTime;
            readonly bool oldNeedScaleFine;
            readonly float oldCamShiftX;
            readonly float oldCamShiftY;
            readonly float oldUiShiftX;
            readonly float oldConfuse;
            readonly Vector2 oldEffectiveScale;
            readonly bool oldNoLimit;
            readonly bool oldNeedInitializeDraw;
            readonly RenderTexture oldActive;
            readonly RenderTexture oldFinalCameraCache;
            readonly uint oldMoverBinding;

            readonly Dictionary<RenderTexture, RenderTexture> replacements = new();
            readonly List<CameraTargetState> cameraTargets = new();
            readonly List<ArrayTextureState> arrayTextures = new();
            readonly List<XCameraTextureState> xCameraTextures = new();
            readonly List<ObjectTextureState> objectTextures = new();
            readonly List<MaterialTextureState> materialTextures = new();
            readonly List<ListItemState> entityTickets = new();
            readonly List<LayerVisibilityState> layerVisibility = new();
            readonly List<BinderState> binders = new();
            bool applied;
            bool disposed;

            internal CaptureSession(M2DBase m2d, M2Camera camera, Map2d map, int width, int height, MapPngExportOptions options)
            {
                this.m2d = m2d;
                this.camera = camera;
                this.map = map;
                targetWidth = width;
                targetHeight = height;
                includeEntities = options.IncludeEntities;
                enabledLayerIndices = options.EnabledMapLayerIndices == null
                    ? null
                    : new HashSet<int>(options.EnabledMapLayerIndices);
                excludedLayerMask = ResolveExcludedLayerMask(options);
                removeDarkOverlay = !options.IncludeDarkOverlay;
                excludedRenderPasses = new HashSet<string>(
                    options.ExcludedRenderPasses ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                logicalWidth = (float)InW.GetValue(null);
                logicalHeight = (float)InH.GetValue(null);
                oldWh = IN.wh;
                oldHh = IN.hh;
                oldScreenWidth = IN.screen_width;
                oldScreenHeight = IN.screen_height;
                oldAreaWidth = (float)AreaW.GetValue(camera);
                oldAreaHeight = (float)AreaH.GetValue(camera);
                oldX = camera.x;
                oldY = camera.y;
                oldDestinationX = camera.depx;
                oldDestinationY = camera.depy;
                oldScale = (float)CameraScale.GetValue(camera);
                oldPosCenter = CameraPosCenter.GetValue(camera);
                oldScaleStart = (float)CameraScaleStart.GetValue(camera);
                oldScaleDestination = (float)CameraScaleDestination.GetValue(camera);
                oldScaleTime = (int)CameraScaleTime.GetValue(camera);
                oldScaleMaxTime = (int)CameraScaleMaxTime.GetValue(camera);
                oldNeedScaleFine = camera.need_scale_fine;
                oldCamShiftX = camera.cam_shift_x;
                oldCamShiftY = camera.cam_shift_y;
                oldUiShiftX = m2d.ui_shift_x;
                oldConfuse = camera.effect_confuse;
                oldEffectiveScale = (Vector2)PosEffectiveScale.GetValue(camera);
                oldNoLimit = M2Camera.no_limit_camera;
                oldNeedInitializeDraw = camera.need_initialize_draw;
                oldActive = RenderTexture.active;
                oldFinalCameraCache = (RenderTexture)FinalCameraCache.GetValue(camera);
                oldMoverBinding = (uint)MoverBinding.GetValue(camera.MovRender);
            }

            internal void Apply()
            {
                BuildTextureRedirect();
                RedirectMaterials();
                ApplyTextureRedirect();
                NeutralizeScreenSpaceWaterRefraction();
                ApplyEffectLayerFilter();
                ApplyRenderPassFilter();
                ApplyMoverMode();

                InW.SetValue(null, (float)targetWidth);
                InH.SetValue(null, (float)targetHeight);
                IN.wh = targetWidth * .5f;
                IN.hh = targetHeight * .5f;
                IN.screen_width = targetWidth;
                IN.screen_height = targetHeight;

                M2Camera.no_limit_camera = true;
                PosEffectiveScale.SetValue(camera, Vector2.one);
                // The character portrait reserves a horizontal UI strip by assigning
                // M2DBase.ui_shift_x. Clearing only cam_shift_x is insufficient: map/mover
                // coordinate conversion also reads ui_shift_x directly, shifting the full-map
                // render about one portrait-width to the right and losing the same amount at left.
                m2d.ui_shift_x = 0f;
                camera.fineBaseShiftPixel(0f, 0f);
                camera.effect_confuse = 0f;
                camera.setWH(targetWidth, targetHeight, false, false);

                // Quaker offsets are part of the live camera. Counteract their current displacement
                // without clearing the effect queue, so capture cannot consume or cancel game state.
                // The output may be larger than the map. Keep the camera on the map's actual
                // center so the added pixels are distributed equally to the left/right/top/bottom.
                float centerX = map.width * .5f - camera.Qu.x * 64f;
                float centerY = map.height * .5f + camera.Qu.y * 64f;
                camera.setEditorSetPosAndScale(centerX, centerY, 1f, false);
                // initCameraFinalize creates several full-screen compositor meshes once, using
                // the then-current IN.w/IN.h. Redirecting only their RTs leaves a 1280x720 inset
                // inside the full-map texture. first:true rebuilds those meshes at target size.
                camera.fineScale(true);
                NormalizeCaptureProjection();
                ApplyMapLayerMode();
                ReinitializeMoverCompositor();
                // UCol is both the forest background compositor and a camera-following mesh.
                // It was built at the live viewport size, so redirecting only the RT leaves
                // viewport-shaped holes in a full-map image. Keep it, but rebuild its geometry
                // against the temporary full-map IN.w/IN.h.
                RebuildCameraColorMesh();
                camera.need_initialize_draw = true;
                applied = true;
            }

            internal void SetReadTarget(RenderTexture texture) => RenderTexture.active = texture;

            static int ResolveExcludedLayerMask(MapPngExportOptions options)
            {
                int mask = 0;
                foreach (string layerName in options.ExcludedEffectLayers)
                    mask |= ResolveLayer(layerName);
                return mask;
            }

            static int ResolveLayer(string layerName)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer < 0)
                    throw new ArgumentException("The game has no Unity render layer named '" + layerName + "'.");
                return 1 << layer;
            }

            void ApplyEffectLayerFilter()
            {
                if (excludedLayerMask == 0) return;
                foreach (CameraTargetState state in cameraTargets)
                {
                    if (state.Camera != null)
                        state.Camera.cullingMask &= ~excludedLayerMask;
                }
            }

            void ApplyRenderPassFilter()
            {
                if (!removeDarkOverlay && excludedRenderPasses.Count == 0) return;

                var visited = new HashSet<XCameraBase>();
                if (CameraBases.GetValue(camera) is XCameraBase[] bases)
                {
                    foreach (XCameraBase cameraBase in bases) FilterBindings(cameraBase, visited);
                }
                if (CameraCollectors.GetValue(camera) is CameraComponentCollecter[] collectors)
                    foreach (CameraComponentCollecter collector in collectors)
                    {
                        FilterBindings(collector, visited);
                        FilterBindings(collector?.InjectionRenderer, visited);
                    }
            }

            void FilterBindings(XCameraBase cameraBase, HashSet<XCameraBase> visited)
            {
                if (cameraBase == null || !visited.Add(cameraBase)
                    || !(CameraBindings.GetValue(cameraBase) is IList bindings))
                    return;

                for (int i = bindings.Count - 1; i >= 0; i--)
                {
                    object binder = bindings[i];
                    if (!ShouldExcludeBinder(cameraBase, binder)) continue;
                    binders.Add(new BinderState(bindings, i, binder));
                    bindings.RemoveAt(i);
                }
            }

            bool ShouldExcludeBinder(XCameraBase cameraBase, object binder)
            {
                if (binder == null) return false;
                Type type = binder.GetType();
                string displayName;
                try { displayName = binder.ToString(); }
                catch { displayName = string.Empty; }

                if (removeDarkOverlay && binder is M2DarkRenderer) return true;

                return excludedRenderPasses.Contains(displayName ?? string.Empty)
                       || excludedRenderPasses.Contains(type.Name)
                       || excludedRenderPasses.Contains(type.FullName ?? string.Empty);
            }

            void RebuildCameraColorMesh()
            {
                if (map?.MyDrawerUCol == null) return;
                map.drawUCol();
                map.MyDrawerUCol.updateForMeshRenderer(temporary: true);
            }

            void ApplyMapLayerMode()
            {
                M2MapLayer[] layers = map.getLayerArray() ?? Array.Empty<M2MapLayer>();
                if (enabledLayerIndices != null)
                {
                    foreach (int index in enabledLayerIndices)
                        if (index >= layers.Length)
                            throw new ArgumentOutOfRangeException(
                                nameof(MapPngExportOptions.EnabledMapLayerIndices),
                                $"Map '{map.key}' has {layers.Length} layers, so layer index {index} is invalid.");

                    for (int i = 0; i < layers.Length; i++)
                    {
                        M2MapLayer layer = layers[i];
                        if (layer == null) continue;
                        bool visible = enabledLayerIndices.Contains(i);
                        if (layer.visible == visible) continue;
                        layerVisibility.Add(new LayerVisibilityState(layer, layer.visible));
                        layer.visible = visible;
                    }
                }

                // Re-entry mutates the live map. Only do it when the user actually changed a
                // layer switch, and never propagate it into background/parallax submaps.
                if (layerVisibility.Count != 0)
                {
                    map.setReentryFlag(chip: true, grad: true, to_submap: false);
                    map.drawCheck(0f);
                }
            }

            void ReinitializeMoverCompositor()
            {
                CameraComponentCollecter[] collectors = CameraCollectors.GetValue(camera) as CameraComponentCollecter[];
                XCameraBase[] bases = CameraBases.GetValue(camera) as XCameraBase[];
                if (collectors == null || bases == null) return;
                camera.MovRender.initCameraFinalize(collectors, bases);
            }

            void NormalizeCaptureProjection()
            {
                CameraComponentCollecter[] collectors = CameraCollectors.GetValue(camera) as CameraComponentCollecter[];
                if (collectors == null) return;

                foreach (CameraComponentCollecter collector in collectors)
                {
                    if (collector?.PxC == null) continue;
                    float integerPixelScale = collector.PxC.pixel_scale;
                    if (integerPixelScale <= 0f) integerPixelScale = 1f;

                    // The temporary RT is a wider world viewport, not a higher-DPI copy of the
                    // original viewport. Cancel PerfectPixelCamera's automatic integer zoom while
                    // preserving the native 1x/0.5x/0.125x scale of each camera pass.
                    collector.PxC.float_scaling = collector.scale / integerPixelScale;
                    collector.scaling_need_fine_all = true;
                }
            }

            void BuildTextureRedirect()
            {
                CameraComponentCollecter[] collectors = CameraCollectors.GetValue(camera) as CameraComponentCollecter[]
                    ?? throw new InvalidOperationException("The game camera collector list is unavailable.");
                foreach (CameraComponentCollecter collector in collectors)
                {
                    if (collector?.Cam != null)
                    {
                        RenderTexture source = collector.Cam.targetTexture;
                        cameraTargets.Add(new CameraTargetState(collector.Cam, source));
                        AddReplacement(source);
                    }

                    RenderTexture[] mutual = MutualTextures.GetValue(collector) as RenderTexture[];
                    if (mutual == null) continue;
                    for (int i = 0; i < mutual.Length; i++)
                    {
                        arrayTextures.Add(new ArrayTextureState(mutual, i, mutual[i]));
                        AddReplacement(mutual[i]);
                    }
                }

                XCameraBase[] bases = CameraBases.GetValue(camera) as XCameraBase[]
                    ?? throw new InvalidOperationException("The game camera render stack is unavailable.");
                foreach (XCameraBase cameraBase in bases)
                {
                    if (!(cameraBase is XCameraTx textureCamera)) continue;
                    RenderTexture source = (RenderTexture)XCameraTexture.GetValue(textureCamera);
                    xCameraTextures.Add(new XCameraTextureState(textureCamera, source));
                    AddReplacement(source);
                }

                // Some compositors keep RTs outside Camera.targetTexture. DungeonBright, for
                // example, has MainBlured and a BrightCacheRenderer with direct Src/Dest fields.
                // Redirect those live objects too; otherwise that pass silently renders at the
                // old 1280x720 resolution and produces the exact kind of missing/offset regions
                // this exporter is meant to avoid.
                CollectObjectTextures(camera.CurDgn);
                foreach (XCameraBase cameraBase in bases)
                {
                    if (cameraBase == null) continue;
                    if (!(CameraBindings.GetValue(cameraBase) is IList bindings)) continue;
                    foreach (object binder in bindings) CollectObjectTextures(binder);
                }

                AddReplacement(camera.getFinalizedTexture());
                AddReplacement(camera.getFinalizeExportTexture());
                AddReplacement(camera.getLightTexture());
                AddReplacement(oldFinalCameraCache);
            }

            void CollectObjectTextures(object target)
            {
                if (target == null) return;
                for (Type type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(
                                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsStatic || field.IsInitOnly
                            || !typeof(RenderTexture).IsAssignableFrom(field.FieldType))
                            continue;
                        RenderTexture source;
                        try { source = field.GetValue(target) as RenderTexture; }
                        catch { continue; }
                        objectTextures.Add(new ObjectTextureState(target, field, source));
                        AddReplacement(source);
                    }
                }
            }

            void AddReplacement(RenderTexture source)
            {
                if (source == null || replacements.ContainsKey(source)) return;
                int width = ResizeDimension(source.width, logicalWidth, targetWidth);
                int height = ResizeDimension(source.height, logicalHeight, targetHeight);
                int max = SystemInfo.maxTextureSize;
                if (width > max || height > max)
                    throw new InvalidOperationException(
                        $"A camera pass for the full map would require {width}x{height}, above the GPU limit {max}.");

                RenderTextureDescriptor descriptor = source.descriptor;
                descriptor.width = Math.Max(1, width);
                descriptor.height = Math.Max(1, height);
                var replacement = new RenderTexture(descriptor)
                {
                    name = source.name + " [Polaris full map]",
                    filterMode = source.filterMode,
                    wrapMode = source.wrapMode,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                replacement.Create();
                ClearRenderTexture(replacement);
                replacements.Add(source, replacement);
            }

            static int ResizeDimension(int source, float logical, int target)
            {
                if (logical <= 0f) return target;
                // Full-size passes are either logical size or logical+16 (the game's 8px border).
                // Smaller passes, notably the 1/8 light buffer, preserve their original ratio.
                if (source >= logical * .75f)
                    return Math.Max(1, target + Mathf.RoundToInt(source - logical));
                return Math.Max(1, Mathf.RoundToInt((target + 16f) * source / (logical + 16f)));
            }

            void RedirectMaterials()
            {
                foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (material == null) continue;
                    string[] properties;
                    try { properties = material.GetTexturePropertyNames(); }
                    catch { continue; }

                    foreach (string property in properties)
                    {
                        Texture current;
                        try { current = material.GetTexture(property); }
                        catch { continue; }
                        if (!(current is RenderTexture source)
                            || !replacements.TryGetValue(source, out RenderTexture replacement))
                            continue;

                        materialTextures.Add(new MaterialTextureState(material, property, source));
                        material.SetTexture(property, replacement);
                    }
                }
            }

            void ApplyTextureRedirect()
            {
                foreach (CameraTargetState state in cameraTargets)
                    if (state.Source != null) state.Camera.targetTexture = replacements[state.Source];
                foreach (ArrayTextureState state in arrayTextures)
                    if (state.Source != null) state.Values[state.Index] = replacements[state.Source];
                foreach (XCameraTextureState state in xCameraTextures)
                    if (state.Source != null) XCameraTexture.SetValue(state.Camera, replacements[state.Source]);
                foreach (ObjectTextureState state in objectTextures)
                    if (state.Source != null) state.Field.SetValue(state.Target, replacements[state.Source]);
                if (oldFinalCameraCache != null)
                    FinalCameraCache.SetValue(camera, replacements[oldFinalCameraCache]);
            }

            void NeutralizeScreenSpaceWaterRefraction()
            {
                Material water = map?.MyDrawerWater?.getMaterial();
                if (water == null || !water.HasProperty("_NoiseTex")) return;

                // WaterInBright offsets its screen-space scene lookup with (_NoiseTex - 0.5).
                // That animated lookup is valid for the live viewport, but on a single
                // full-map render it turns the whole submerged part of maps such as
                // forest_wood_slash into a warped camera image. A neutral 0.5 texture keeps
                // the map's water mesh, tint, masks and lighting while making that offset zero.
                Texture source;
                try { source = water.GetTexture("_NoiseTex"); }
                catch { return; }
                materialTextures.Add(new MaterialTextureState(water, "_NoiseTex", source));
                water.SetTexture("_NoiseTex", Texture2D.grayTexture);
            }

            void ApplyMoverMode()
            {
                Array groups = MoverTickets.GetValue(camera.MovRender) as Array;
                if (!includeEntities && groups != null)
                {
                    foreach (object group in groups)
                    {
                        if (!(group is IList list)) continue;
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (!(list[i] is M2RenderTicket ticket) || ticket.AssignMover == null) continue;
                            entityTickets.Add(new ListItemState(list, i, ticket));
                            list.RemoveAt(i);
                        }
                    }
                }

                camera.MovRender.setRedrawFlag();
                uint binding = (uint)MoverBinding.GetValue(camera.MovRender);
                MoverBinding.SetValue(camera.MovRender, binding & ~3840u);

                if (!includeEntities)
                    ClearRenderTexture(camera.getMoverCameraCC()?.Cam?.targetTexture);
            }

            static void ClearRenderTexture(RenderTexture target)
            {
                if (target == null) return;
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    GL.Clear(clearDepth: true, clearColor: true, backgroundColor: Color.clear);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                RenderTexture.active = oldActive;

                for (int i = entityTickets.Count - 1; i >= 0; i--)
                    entityTickets[i].Restore();
                try { MoverBinding.SetValue(camera.MovRender, oldMoverBinding); } catch { }

                // Items were removed from each list from high to low indices. Reverse restoration
                // therefore inserts low to high and recreates the exact original ordering.
                for (int i = binders.Count - 1; i >= 0; i--)
                    binders[i].Restore();
                for (int i = materialTextures.Count - 1; i >= 0; i--)
                    materialTextures[i].Restore();
                for (int i = cameraTargets.Count - 1; i >= 0; i--)
                    cameraTargets[i].Restore();
                for (int i = arrayTextures.Count - 1; i >= 0; i--)
                    arrayTextures[i].Restore();
                for (int i = xCameraTextures.Count - 1; i >= 0; i--)
                    xCameraTextures[i].Restore();
                for (int i = objectTextures.Count - 1; i >= 0; i--)
                    objectTextures[i].Restore();
                try { FinalCameraCache.SetValue(camera, oldFinalCameraCache); } catch { }

                try
                {
                    InW.SetValue(null, logicalWidth);
                    InH.SetValue(null, logicalHeight);
                    IN.wh = oldWh;
                    IN.hh = oldHh;
                    IN.screen_width = oldScreenWidth;
                    IN.screen_height = oldScreenHeight;
                    M2Camera.no_limit_camera = oldNoLimit;
                    PosEffectiveScale.SetValue(camera, oldEffectiveScale);
                    camera.effect_confuse = oldConfuse;
                    camera.setWH(oldAreaWidth, oldAreaHeight, false, false);
                    camera.setEditorSetPosAndScale(oldX, oldY, oldScale, false);
                    m2d.ui_shift_x = oldUiShiftX;
                    // Assigning cam_shift_x directly does not mark the finalized compositor
                    // transform dirty. Use the game's setter so the live viewport visibly returns.
                    camera.fineBaseShiftPixel(oldCamShiftX * 64f, oldCamShiftY * 64f);
                    // Rebuild the same compositor meshes back to the normal game viewport.
                    camera.fineScale(true);
                    ReinitializeMoverCompositor();
                    RebuildCameraColorMesh();
                    if (layerVisibility.Count != 0)
                    {
                        for (int i = layerVisibility.Count - 1; i >= 0; i--)
                            layerVisibility[i].Restore();
                        map.setReentryFlag(chip: true, grad: true, to_submap: false);
                        map.drawCheck(0f);
                    }
                    camera.depx = oldDestinationX;
                    camera.depy = oldDestinationY;
                    CameraPosCenter.SetValue(camera, oldPosCenter);
                    CameraScaleStart.SetValue(camera, oldScaleStart);
                    CameraScaleDestination.SetValue(camera, oldScaleDestination);
                    CameraScaleTime.SetValue(camera, oldScaleTime);
                    CameraScaleMaxTime.SetValue(camera, oldScaleMaxTime);
                    camera.need_scale_fine = oldNeedScaleFine;
                    camera.need_initialize_draw = true;
                    camera.MovRender.setRedrawFlag();
                    camera.MovRender.need_clip_check = true;
                }
                catch (Exception ex)
                {
                    camera.need_initialize_draw = oldNeedInitializeDraw || applied;
                    PolarisAPI.Errors.Report(ex, "restoring the game camera after full-map PNG export", typeof(MapPngRenderer).Assembly);
                }

                foreach (RenderTexture replacement in replacements.Values)
                {
                    try
                    {
                        replacement.Release();
                        UnityEngine.Object.Destroy(replacement);
                    }
                    catch { }
                }
                replacements.Clear();
            }
        }

        sealed class CameraTargetState
        {
            internal CameraTargetState(Camera camera, RenderTexture source)
            {
                Camera = camera;
                Source = source;
                CullingMask = camera != null ? camera.cullingMask : 0;
            }
            internal readonly Camera Camera;
            internal readonly RenderTexture Source;
            internal readonly int CullingMask;
            internal void Restore()
            {
                if (Camera == null) return;
                Camera.targetTexture = Source;
                Camera.cullingMask = CullingMask;
            }
        }

        sealed class ArrayTextureState
        {
            internal ArrayTextureState(RenderTexture[] values, int index, RenderTexture source)
            { Values = values; Index = index; Source = source; }
            internal readonly RenderTexture[] Values;
            internal readonly int Index;
            internal readonly RenderTexture Source;
            internal void Restore() { if (Values != null && Index < Values.Length) Values[Index] = Source; }
        }

        sealed class XCameraTextureState
        {
            internal XCameraTextureState(XCameraTx camera, RenderTexture source) { Camera = camera; Source = source; }
            internal readonly XCameraTx Camera;
            internal readonly RenderTexture Source;
            internal void Restore() { if (Camera != null) XCameraTexture.SetValue(Camera, Source); }
        }

        sealed class MaterialTextureState
        {
            internal MaterialTextureState(Material material, string property, Texture source)
            { Material = material; Property = property; Source = source; }
            internal readonly Material Material;
            internal readonly string Property;
            internal readonly Texture Source;
            internal void Restore()
            {
                try { if (Material != null) Material.SetTexture(Property, Source); }
                catch { }
            }
        }

        sealed class ObjectTextureState
        {
            internal ObjectTextureState(object target, FieldInfo field, RenderTexture source)
            { Target = target; Field = field; Source = source; }
            internal readonly object Target;
            internal readonly FieldInfo Field;
            internal readonly RenderTexture Source;
            internal void Restore()
            {
                try { Field.SetValue(Target, Source); }
                catch { }
            }
        }

        sealed class ListItemState
        {
            internal ListItemState(IList values, int index, object value)
            { Values = values; Index = index; Value = value; }
            internal readonly IList Values;
            internal readonly int Index;
            internal readonly object Value;
            internal void Restore()
            {
                try
                {
                    if (Values == null || Value == null || Values.Contains(Value)) return;
                    Values.Insert(Math.Min(Index, Values.Count), Value);
                }
                catch { }
            }
        }

        sealed class LayerVisibilityState
        {
            internal LayerVisibilityState(M2MapLayer layer, bool visible)
            { Layer = layer; Visible = visible; }
            internal readonly M2MapLayer Layer;
            internal readonly bool Visible;
            internal void Restore()
            {
                if (Layer != null) Layer.visible = Visible;
            }
        }

        sealed class BinderState
        {
            internal BinderState(IList values, int index, object value)
            { Values = values; Index = index; Value = value; }
            internal readonly IList Values;
            internal readonly int Index;
            internal readonly object Value;
            internal void Restore()
            {
                try
                {
                    if (Values == null || Value == null || Values.Contains(Value)) return;
                    Values.Insert(Math.Min(Index, Values.Count), Value);
                }
                catch { }
            }
        }

    }
}
