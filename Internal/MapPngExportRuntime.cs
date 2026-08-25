using System;
using System.IO;
using m2d;

namespace Polaris.Map.Internal
{
    /// <summary>单任务、逐帧推进的导图队列；转场在 Update 等待，实际 GPU 捕获在 LateUpdate 执行。</summary>
    internal static class MapPngExportRuntime
    {
        sealed class Request
        {
            internal M2DBase Owner;
            internal Map2d Target;
            internal MapPngExport Handle;
            internal MapPngExportOptions Options;
            internal int WarmupLeft;
            internal bool ReadyToRender;
        }

        static Request active;

        internal static MapPngExport Active => active?.Handle;

        internal static MapPngExport Start(string mapId, string outputPath, MapPngExportOptions options)
        {
            MapRuntime.EnsureMainThread();
            if (string.IsNullOrWhiteSpace(mapId))
                throw new ArgumentException("A game map ID is required.", nameof(mapId));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An output PNG path is required.", nameof(outputPath));
            if (active != null && !active.Handle.IsFinished)
                throw new InvalidOperationException(
                    $"Map PNG export is already running for '{active.Handle.MapId}'. Wait for it to finish first.");

            M2DBase m2d = M2DBase.Instance;
            if (m2d == null)
                throw new InvalidOperationException("Enter the game world before exporting a map PNG.");

            string key = mapId.Trim();
            Map2d target = m2d.Get(key, true);
            if (target == null)
                throw new ArgumentException($"The game has no registered map with ID '{key}'.", nameof(mapId));

            string fullPath = Path.GetFullPath(outputPath.Trim());
            if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
                fullPath += ".png";

            MapPngExportOptions safeOptions = (options ?? new MapPngExportOptions()).CopyValidated();
            if (!safeOptions.Overwrite && File.Exists(fullPath))
                throw new IOException("The output PNG already exists: " + fullPath);

            var handle = new MapPngExport(key, fullPath);
            active = new Request
            {
                Owner = m2d,
                Target = target,
                Handle = handle,
                Options = safeOptions,
                WarmupLeft = safeOptions.WarmupFrames,
            };

            if (ReferenceEquals(m2d.curMap, target))
            {
                handle.SetStatus(MapPngExportStatus.PreparingScene);
                if (active.WarmupLeft == 0) active.ReadyToRender = true;
            }
            else
            {
                if (!safeOptions.EnterMapIfNeeded)
                {
                    active = null;
                    throw new InvalidOperationException(
                        $"Map '{key}' is not current and EnterMapIfNeeded is disabled.");
                }
                if (m2d.isLoaderLoading())
                {
                    active = null;
                    throw new InvalidOperationException("The game is already loading map materials. Try the export again after the transition finishes.");
                }

                handle.SetStatus(MapPngExportStatus.LoadingMap);
                m2d.initMapMaterialASync(target, 2, false);
            }

            return handle;
        }

        internal static void Update()
        {
            Request request = active;
            if (request == null || request.Handle.IsFinished || request.ReadyToRender) return;

            try
            {
                if (!ReferenceEquals(M2DBase.Instance, request.Owner))
                    throw new InvalidOperationException("The game world was unloaded during PNG export.");

                if (!ReferenceEquals(request.Owner.curMap, request.Target))
                {
                    if (request.Handle.Status != MapPngExportStatus.LoadingMap)
                        throw new InvalidOperationException("The active map changed before PNG capture began.");
                    if (!request.Owner.isLoaderLoading())
                        throw new InvalidOperationException(
                            $"The game stopped loading '{request.Target.key}' before entering it.");
                    return;
                }

                if (request.Owner.isLoaderLoading()) return;
                request.Handle.SetStatus(MapPngExportStatus.PreparingScene);
                if (request.WarmupLeft > 0)
                {
                    request.WarmupLeft--;
                    return;
                }
                request.ReadyToRender = true;
            }
            catch (Exception ex)
            {
                Fail(request, ex);
            }
        }

        internal static void LateUpdate()
        {
            Request request = active;
            if (request == null || !request.ReadyToRender || request.Handle.IsFinished) return;

            try
            {
                if (!ReferenceEquals(M2DBase.Instance, request.Owner)
                    || !ReferenceEquals(request.Owner.curMap, request.Target))
                    throw new InvalidOperationException("The active map changed before the PNG render pass.");

                request.Handle.SetStatus(MapPngExportStatus.Rendering);
                MapPngCapture capture = MapPngRenderer.Capture(
                    request.Owner, request.Target, request.Options);

                string directory = Path.GetDirectoryName(request.Handle.OutputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (!request.Options.Overwrite && File.Exists(request.Handle.OutputPath))
                    throw new IOException("The output PNG already exists: " + request.Handle.OutputPath);

                File.WriteAllBytes(request.Handle.OutputPath, capture.Png);
                request.Handle.Complete(capture.Width, capture.Height);
                active = null;
            }
            catch (Exception ex)
            {
                Fail(request, ex);
            }
        }

        internal static void Shutdown()
        {
            Request request = active;
            active = null;
            request?.Handle.Fail(new InvalidOperationException(
                "PolarisMap shut down before the PNG export completed."));
        }

        static void Fail(Request request, Exception error)
        {
            if (ReferenceEquals(active, request)) active = null;
            request.Handle.Fail(error);
            PolarisAPI.Errors.Report(error, "PolarisMap full-scene PNG export", typeof(MapPngExportRuntime).Assembly);
        }
    }
}
