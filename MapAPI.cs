using Polaris.Map.Internal;
using Polaris.Map.Authoring;
using System;

namespace Polaris.Map
{
    /// <summary>Polaris 自定义地图能力的统一入口。</summary>
    public static class MapAPI
    {
        /// <summary>取得当前打开的地图；标题画面、读档中或尚未进游戏时返回 <c>null</c>。</summary>
        public static LiveMap Current => MapRuntime.GetCurrent();

        /// <summary>持久化一张新地图并通过原版预载链路切换；已有 key 会被拒绝。</summary>
        public static MapTransition CreateAndEnter(MapDraft draft)
            => MapRuntime.CreateAndEnter(draft);

        /// <summary>解析 .pmap XML。</summary>
        public static PmapDocument ParsePmap(string xml, string sourceName = ".pmap")
            => PmapDocument.Parse(xml, sourceName);

        /// <summary>编译并进入 .pmap；<paramref name="ownerType"/> 用于确认所有权和热重载权限。</summary>
        public static MapTransition LoadAndEnterPmap(PmapDocument document, Type ownerType)
            => MapRuntime.LoadAndEnterPmap(document, ownerType, document?.ToXml());

        /// <summary>从 XML 文本加载并进入 .pmap。</summary>
        public static MapTransition LoadAndEnterPmap(string xml, Type ownerType, string sourceName = ".pmap")
        {
            PmapDocument document = PmapDocument.Parse(xml, sourceName);
            return MapRuntime.LoadAndEnterPmap(document, ownerType, xml);
        }
    }
}
