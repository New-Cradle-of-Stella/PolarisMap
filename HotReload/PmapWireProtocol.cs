namespace Polaris.Map.HotReload
{
    /// <summary>PolarisTools 与游戏进程共用的 .pmap 调试协议常量。</summary>
    public static class PmapWireProtocol
    {
        public const int Version = 3;
        public const int MaxDocumentBytes = 4 * 1024 * 1024;
        public const int MaxPreviewImageCount = 20000;
        public const string PipeName = "Polaris.PMap.HotReload";
    }

    public enum PmapWireRequest : byte
    {
        HotReload = 1,
        ExtractOriginalMapPreview = 2,
        ClearOriginalMapPreview = 3,
    }
}
