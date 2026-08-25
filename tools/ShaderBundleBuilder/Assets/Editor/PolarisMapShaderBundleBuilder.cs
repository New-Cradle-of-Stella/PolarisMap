using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class PolarisMapShaderBundleBuilder
{
    public static void Build()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        string sourceShader = Path.Combine(repositoryRoot, "Shaders", "PolarisMapNoCameraFade.shader");
        string projectShader = Path.Combine(Application.dataPath, "PolarisMapNoCameraFade.shader");
        string outputDirectory = Path.Combine(projectRoot, "AssetBundles");
        string resourceDirectory = Path.Combine(repositoryRoot, "Resources");

        File.Copy(sourceShader, projectShader, true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AssetImporter importer = AssetImporter.GetAtPath("Assets/PolarisMapNoCameraFade.shader");
        importer.assetBundleName = "polarismap_shaders.bundle";
        importer.SaveAndReimport();

        Directory.CreateDirectory(outputDirectory);
        BuildPipeline.BuildAssetBundles(
            outputDirectory,
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        Directory.CreateDirectory(resourceDirectory);
        string bundlePath = Path.Combine(outputDirectory, "polarismap_shaders.bundle");
        File.Copy(bundlePath, Path.Combine(resourceDirectory, "polarismap_shaders.bundle"), true);

        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        Shader shader = bundle == null
            ? null
            : bundle.LoadAsset<Shader>("Assets/PolarisMapNoCameraFade.shader");
        if (shader == null)
            throw new BuildFailedException("The no-camera-fade shader was not loadable from the built bundle.");
        bundle.Unload(true);
    }
}
