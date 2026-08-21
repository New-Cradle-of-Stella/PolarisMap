using System;
using Polaris.Map.Internal;
using UnityEngine;
using UnityEngine.InputSystem;
using XX;

namespace Polaris.Map.Debugging
{
    internal static class MapDebugRuntime
    {
        static GameObject root;

        internal static bool IsEnabled { get; private set; }

        internal static void Start(bool enabled)
        {
            IsEnabled = enabled;
            if (!enabled) return;

            root = new GameObject("PolarisMap Debug");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<MapDebugOverlay>();
            PmapHotReloadServer.Start();
            Debug.Log("[PolarisMap] .pmap debug enabled; F11 opens the map inspector.");
        }

        internal static void Update()
        {
            if (!IsEnabled) return;
            try
            {
                if (IN.getKD(Key.F11) && KEY.getModifier() == MODIF.NONE)
                    MapDebugPage.Toggle();
            }
            catch (Exception ex)
            {
                PolarisAPI.Errors.Report(ex, "PolarisMap F11 debug hotkey", typeof(MapDebugRuntime).Assembly);
            }
        }

        internal static void Shutdown()
        {
            if (!IsEnabled) return;
            MapDebugPage.Shutdown();
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
            IsEnabled = false;
        }
    }
}
