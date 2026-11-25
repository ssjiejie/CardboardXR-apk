using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // The Sentis convolution kernel expands into hundreds of compute shader variants that each need a
    // separate compile. This build machine caps the process at 8 GB, so that one step is enough to
    // push it into the OOM killer before IL2CPP ever starts. Dropping the variants keeps the apk
    // buildable here; hand tracking then falls back instead of running inference.
    public class StripConvGeneric : IPreprocessComputeShaders
    {
        public int callbackOrder => 0;

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (shader.name.IndexOf("ConvGeneric", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;
            Debug.Log($"[CiBuild] stripping {data.Count} variants of {shader.name}/{kernelName}");
            data.Clear();
        }
    }
}
