using HarmonyLib;
using UnityEngine.InputSystem;
using XX;

namespace Polaris.Map.Debugging
{
    /// <summary>地图调试启用时独占无修饰键 F11，避免原版 ActiveDebugger 同帧处理。</summary>
    [HarmonyPatch(typeof(ActiveDebugger), "runIRD")]
    internal static class MapDebugF11Patch
    {
        static bool Prefix(ref bool __result)
        {
            if (!MapDebugRuntime.IsEnabled || !IN.getKD(Key.F11) || KEY.getModifier() != MODIF.NONE)
                return true;

            __result = true;
            return false;
        }
    }
}
