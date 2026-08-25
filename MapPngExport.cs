using System;
using System.Linq;

namespace Polaris.Map
{
    public enum MapPngExportStatus
    {
        Queued,
        LoadingMap,
        PreparingScene,
        Rendering,
        Completed,
        Failed,
    }

    /// <summary>整图 PNG 导出参数。捕获只在 Unity 主线程执行，并使用游戏当前的完整地图合成管线。</summary>
    public sealed class MapPngExportOptions
    {
        /// <summary>地图 ID 不是当前地图时，是否允许通过游戏原生异步转场进入目标地图。</summary>
        public bool EnterMapIfNeeded { get; set; } = true;

        /// <summary>是否绘制玩家、NPC 与敌人等 mover。地图图块、子地图、光照与场景特效始终绘制。</summary>
        public bool IncludeEntities { get; set; } = true;

        /// <summary>
        /// 要参与本次捕获的当前地图图层索引。<c>null</c> 表示保持游戏当前可见性；
        /// 空数组表示隐藏当前地图的全部图层。
        /// </summary>
        public int[] EnabledMapLayerIndices { get; set; }

        /// <summary>
        /// 是否绘制相机产生的 <c>M2DarkRenderer</c> 暗区效果。关闭时仅在捕获期间
        /// 移除该 binder，随后按原顺序恢复；不会改写地图图层或光照缓冲。
        /// </summary>
        public bool IncludeDarkOverlay { get; set; } = false;

        /// <summary>
        /// 捕获期间从所有地图摄像机中排除的 Unity 层名。适合精确开关由独立渲染层承载的特效；
        /// 不认识的层名会令导出失败，而不是静默忽略。例：<c>new[] { "ChipsUCol" }</c>。
        /// </summary>
        public string[] ExcludedEffectLayers { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 捕获期间排除的 camera render binder 名称。可填写 binder 的 <c>ToString()</c>、类型名或完整类型名；
        /// 例：<c>new[] { "DarkRenderer" }</c>。只影响本次捕获，随后按原顺序恢复。
        /// </summary>
        public string[] ExcludedRenderPasses { get; set; } = Array.Empty<string>();

        /// <summary>输出文件已存在时是否覆盖。</summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>
        /// 在地图原始宽高之外增加的总比例。<c>0.2</c> 表示输出宽高为地图的约 120%，
        /// 多出的范围平均分配到四周。
        /// </summary>
        public float BoundsExpansion { get; set; } = 0.2f;

        /// <summary>目标地图打开后等待多少帧再捕获，让子地图与动态绘制器完成初始化。</summary>
        public int WarmupFrames { get; set; } = 2;

        internal MapPngExportOptions CopyValidated()
        {
            if (WarmupFrames < 0 || WarmupFrames > 120)
                throw new ArgumentOutOfRangeException(nameof(WarmupFrames), "WarmupFrames must be between 0 and 120.");
            if (float.IsNaN(BoundsExpansion) || float.IsInfinity(BoundsExpansion)
                || BoundsExpansion < 0f || BoundsExpansion > 1f)
                throw new ArgumentOutOfRangeException(
                    nameof(BoundsExpansion), "BoundsExpansion must be between 0 and 1.");

            return new MapPngExportOptions
            {
                EnterMapIfNeeded = EnterMapIfNeeded,
                IncludeEntities = IncludeEntities,
                EnabledMapLayerIndices = EnabledMapLayerIndices == null
                    ? null
                    : EnabledMapLayerIndices
                        .Where(value => value >= 0)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray(),
                IncludeDarkOverlay = IncludeDarkOverlay,
                ExcludedEffectLayers = (ExcludedEffectLayers ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ExcludedRenderPasses = (ExcludedRenderPasses ?? Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Overwrite = Overwrite,
                BoundsExpansion = BoundsExpansion,
                WarmupFrames = WarmupFrames,
            };
        }
    }

    /// <summary>按地图 ID 导出完整场景 PNG 的可观察句柄。</summary>
    public sealed class MapPngExport
    {
        internal MapPngExport(string mapId, string outputPath)
        {
            MapId = mapId;
            OutputPath = outputPath;
            Status = MapPngExportStatus.Queued;
        }

        public string MapId { get; }
        public string OutputPath { get; }
        public MapPngExportStatus Status { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Exception Error { get; private set; }
        public bool IsFinished => Status == MapPngExportStatus.Completed || Status == MapPngExportStatus.Failed;

        /// <summary>成功或失败时触发一次；回调在 Unity 主线程执行。</summary>
        public event Action<MapPngExport> Finished;

        internal void SetStatus(MapPngExportStatus status)
        {
            if (!IsFinished) Status = status;
        }

        internal void Complete(int width, int height)
        {
            if (IsFinished) return;
            Width = width;
            Height = height;
            Status = MapPngExportStatus.Completed;
            NotifyFinished();
        }

        internal void Fail(Exception error)
        {
            if (IsFinished) return;
            Error = error ?? new InvalidOperationException("Map PNG export failed.");
            Status = MapPngExportStatus.Failed;
            NotifyFinished();
        }

        void NotifyFinished()
        {
            Action<MapPngExport> handlers = Finished;
            if (handlers == null) return;

            foreach (Action<MapPngExport> handler in handlers.GetInvocationList())
            {
                try { handler(this); }
                catch (Exception ex)
                {
                    PolarisAPI.Errors.Report(ex, "PolarisMap PNG export callback", handler.Method?.DeclaringType?.Assembly);
                }
            }
        }
    }
}
