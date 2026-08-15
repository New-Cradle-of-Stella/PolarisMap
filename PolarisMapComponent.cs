using Polaris.Components;

namespace Polaris.Map
{
    /// <summary>自定义地图能力的组件入口。</summary>
    public sealed class PolarisMapComponent : PolarisComponent
    {
        public override string Id => "PolarisMap";
        public override int Order => 500;
    }
}
