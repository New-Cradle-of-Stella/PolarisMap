using UnityEngine;

namespace Polaris.Map.Debugging
{
    internal sealed class MapDebugOverlay : MonoBehaviour
    {
        void OnGUI() => MapDebugPage.Draw();
    }
}
