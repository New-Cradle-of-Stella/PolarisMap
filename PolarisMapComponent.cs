using Polaris.Components;
using Polaris.Map.Internal;
using Polaris.Map.Debugging;
using System.Linq;

namespace Polaris.Map
{
    /// <summary>自定义地图能力的组件入口。</summary>
    public sealed class PolarisMapComponent : PolarisComponent
    {
        public override string Id => "PolarisMap";
        public override int Order => 500;

        public override void Awake() => MapRuntime.Initialize();

        public override void Start()
            => MapDebugRuntime.Start(PolarisAPI.Modules.PluginAssemblies.Any(MapRuntime.HasHotReloadMarker));

        public override void Update()
        {
            MapRuntime.Update();
            MapDebugRuntime.Update();
        }

        public override void Shutdown()
        {
            MapDebugRuntime.Shutdown();
            MapRuntime.Shutdown();
        }
    }
}
