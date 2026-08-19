using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace S4Viewer.Editor
{
    /// <summary>Reproducible WebGL build used locally and by GitHub Pages CI.</summary>
    public static class WebGLPagesBuilder
    {
        [MenuItem("S4/Build WebGL for GitHub Pages")]
        public static void Build()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(projectRoot, "build", "WebGL");
            Directory.CreateDirectory(output);

            PlayerSettings.WebGL.template = "PROJECT:S4Pages";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "S4 Tetrahedral Symmetry Viewer";

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            if (!scenes.All(File.Exists))
                throw new InvalidOperationException("A configured build scene does not exist.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("WebGL build failed: " + report.summary.result);

            File.WriteAllText(Path.Combine(output, ".nojekyll"), string.Empty);
            Debug.Log("GitHub Pages WebGL build: " + output);
        }
    }
}
