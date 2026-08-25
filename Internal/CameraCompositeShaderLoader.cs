using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Polaris.Map.Internal
{
    internal static class CameraCompositeShaderLoader
    {
        const string BundleResourceName = "Polaris.Map.Resources.polarismap_shaders.bundle";
        const string ShaderAssetName = "Assets/PolarisMapNoCameraFade.shader";

        static AssetBundle bundle;
        static Shader noCameraFadeShader;
        static bool loadAttempted;

        internal static Shader GetNoCameraFadeShader()
        {
            if (noCameraFadeShader != null) return noCameraFadeShader;
            if (loadAttempted) return null;
            loadAttempted = true;

            using (Stream stream = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream(BundleResourceName))
            {
                if (stream == null) return null;
                byte[] bytes = new byte[stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) return null;
                    offset += read;
                }

                bundle = AssetBundle.LoadFromMemory(bytes);
            }

            if (bundle == null) return null;
            noCameraFadeShader = bundle.LoadAsset<Shader>(ShaderAssetName);
            if (noCameraFadeShader == null)
            {
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                if (shaders != null && shaders.Length != 0)
                    noCameraFadeShader = shaders[0];
            }
            return noCameraFadeShader;
        }
    }
}
