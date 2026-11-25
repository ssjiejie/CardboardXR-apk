using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    /// <summary>
    /// ARCore 的两张相机背景 shader 没有任何资产引用它们：插件只在运行时用
    /// <c>Shader.Find("Unlit/ARCoreBackground")</c> 去找。构建器看到"没人用"，就把它们剥掉，
    /// 于是 <c>ARCameraManager.cameraMaterial</c> 为 null，屏幕全黑。
    ///
    /// 这里生成两个引用该 shader 的材质，放进 Resources。Resources 下的资产一律打包，
    /// shader 因此进入包里；运行时 Shader.Find 能命中，渲染路径完全不用改。
    /// </summary>
    public static class ArBackgroundMaterials
    {
        const string Folder = "Assets/_Project/Resources/ArBackground";
        const string BeforeShader = "Unlit/ARCoreBackground";
        const string AfterShader = "Unlit/ARCoreBackground/AfterOpaques";

        [MenuItem("CardboardXR/Ensure AR Background Materials")]
        public static void Ensure()
        {
            Directory.CreateDirectory(Folder);

            var found = new Dictionary<string, Shader>();
            foreach (var guid in AssetDatabase.FindAssets("ARCoreBackground t:Shader", new[] { "Assets", "Packages" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && !found.ContainsKey(shader.name))
                    found[shader.name] = shader;
            }

            foreach (var name in new[] { BeforeShader, AfterShader })
            {
                if (!found.TryGetValue(name, out var shader))
                {
                    Debug.LogError($"[ArBackground] shader not found: {name} — the camera background will be black");
                    continue;
                }

                var assetPath = $"{Folder}/{name.Replace('/', '_')}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (existing != null)
                {
                    if (existing.shader != shader) existing.shader = shader;
                    EditorUtility.SetDirty(existing);
                    Debug.Log($"[ArBackground] kept {assetPath}");
                }
                else
                {
                    var material = new Material(shader) { name = name.Replace('/', '_') };
                    AssetDatabase.CreateAsset(material, assetPath);
                    Debug.Log($"[ArBackground] created {assetPath}");
                }

                var loaded = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                Debug.Log(loaded != null && loaded.shader == shader
                    ? $"[ArBackground] verified {assetPath} -> {shader.name}"
                    : $"[ArBackground] FAILED to verify {assetPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
