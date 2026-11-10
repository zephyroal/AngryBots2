// ABMaterialUrpConverter.cs
// -----------------------------------------------------------------------------
// 把无法在 URP 下渲染的材质（Built-in Standard / Legacy Shaders / Mobile /
// 缺失 shader）批量转换为 Universal Render Pipeline/Lit，并尽量保留贴图与颜色。
//
// 用法（batchmode）：-executeMethod ABPort.ABMaterialUrpConverter.ConvertAll
// 只读/写 Assets 下的 .mat；转换前会把原 shader 名写入报告。
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ABPort
{
    public static class ABMaterialUrpConverter
    {
        const string UrpLit = "Universal Render Pipeline/Lit";
        const string UrpUnlit = "Universal Render Pipeline/Unlit";
        const string LogDir = @"E:\MyPlayGround\_logs";

        static readonly string[] LegacyPrefixes =
        {
            "Legacy Shaders/",
            "Mobile/",
            "Self-Illumin/",   // 仅当不是 AngryBots 命名空间
            "Standard",
            "Diffuse",
            "Bumped Specular",
            "VertexLit",
        };

        [MenuItem("ABPort/Convert Materials To URP")]
        public static void ConvertAll()
        {
            Directory.CreateDirectory(LogDir);
            var report = new StringBuilder();
            report.AppendLine("=== 材质转 URP ===" + DateTime.Now);

            var paths = AssetDatabase.FindAssets("t:Material", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var stats = new Dictionary<string, int>();
            var converted = new List<string>();
            int skipped = 0;

            foreach (var path in paths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "<null>";
                bool needs = NeedsConversion(shaderName);
                if (!needs) { skipped++; continue; }

                stats[shaderName] = stats.TryGetValue(shaderName, out var c) ? c + 1 : 1;

                var snapshot = Snapshot(mat);
                bool transparent = IsTransparent(mat, shaderName);
                bool cutout = mat.HasProperty("_Cutoff") && mat.GetFloat("_Cutoff") > 0.001f && !transparent;

                var target = transparent || cutout ? Shader.Find(UrpLit) : Shader.Find(UrpLit);
                if (target == null)
                {
                    Debug.LogError("[ABMaterial] 找不到 URP/Lit");
                    break;
                }

                mat.shader = target;
                Apply(mat, snapshot, transparent, cutout);
                EditorUtility.SetDirty(mat);
                converted.Add(path + "  [" + shaderName + "]");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine("扫描材质: " + paths.Count + "，已转换: " + converted.Count + "，跳过(已支持): " + skipped);
            report.AppendLine();
            report.AppendLine("## 按原 shader 统计");
            foreach (var kv in stats.OrderByDescending(k => k.Value))
                report.AppendLine("- " + kv.Key + " x" + kv.Value);
            report.AppendLine();
            report.AppendLine("## 转换明细（前 400 条）");
            foreach (var line in converted.Take(400)) report.AppendLine("- " + line);

            File.WriteAllText(Path.Combine(LogDir, "ab_material_urp_report.md"), report.ToString(), new UTF8Encoding(false));
            Debug.Log(report.ToString());
        }

        struct MatSnapshot
        {
            public Texture mainTex;
            public Color color;
            public Texture bumpMap;
            public float bumpScale;
            public Texture emissionMap;
            public Color emissionColor;
            public float metallic;
            public float smoothness;
            public Texture metallicGlossMap;
            public Texture occlusionMap;
            public float cutoff;
        }

        static MatSnapshot Snapshot(Material mat)
        {
            var s = new MatSnapshot();
            s.mainTex = GetTexture(mat, "_MainTex", "_BaseMap");
            s.color = GetColor(mat, Color.white, "_Color", "_BaseColor", "_TintColor");
            s.bumpMap = GetTexture(mat, "_BumpMap", "_Normal");
            s.bumpScale = GetFloat(mat, 1f, "_BumpScale");
            s.emissionMap = GetTexture(mat, "_EmissionMap", "_Illum");
            s.emissionColor = GetColor(mat, Color.black, "_EmissionColor");
            s.metallic = GetFloat(mat, 0f, "_Metallic");
            s.smoothness = GetFloat(mat, 0.5f, "_Glossiness", "_Smoothness");
            s.metallicGlossMap = GetTexture(mat, "_MetallicGlossMap");
            s.occlusionMap = GetTexture(mat, "_OcclusionMap");
            s.cutoff = GetFloat(mat, 0.5f, "_Cutoff");
            return s;
        }

        static void Apply(Material mat, MatSnapshot s, bool transparent, bool cutout)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s.mainTex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s.mainTex);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", s.color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", s.color);

            if (s.bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", s.bumpMap);
                mat.SetFloat("_BumpScale", s.bumpScale);
            }

            if (mat.HasProperty("_MetallicGlossMap") && s.metallicGlossMap != null) mat.SetTexture("_MetallicGlossMap", s.metallicGlossMap);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", s.metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s.smoothness);
            else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", s.smoothness);

            if (mat.HasProperty("_OcclusionMap") && s.occlusionMap != null) mat.SetTexture("_OcclusionMap", s.occlusionMap);

            bool hasEmission = s.emissionMap != null || s.emissionColor.maxColorComponent > 0.005f;
            if (hasEmission)
            {
                if (mat.HasProperty("_EmissionMap") && s.emissionMap != null) mat.SetTexture("_EmissionMap", s.emissionMap);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", s.emissionColor == Color.black ? Color.white : s.emissionColor);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            if (cutout)
            {
                mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", s.cutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        static bool NeedsConversion(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName) || shaderName == "<null>" || shaderName.Contains("InternalErrorShader"))
                return true;

            // AngryBots 自研 shader 已在 Assets/Shaders 下改写为 URP，保持不动。
            if (shaderName.StartsWith("AngryBots/")) return false;
            if (shaderName.StartsWith("Self-Illumin/AngryBots/")) return false;
            if (shaderName.StartsWith("Universal Render Pipeline/")) return false;
            if (shaderName.StartsWith("Graphs/")) return false;
            if (shaderName.StartsWith("TextMeshPro/")) return false;
            if (shaderName.StartsWith("GUI/") || shaderName.StartsWith("UI/") || shaderName.StartsWith("Sprites/")) return false;
            if (shaderName.StartsWith("Skybox/")) return false;
            if (shaderName.StartsWith("Hidden/")) return false;
            if (shaderName.StartsWith("Particles/")) return false;

            foreach (var prefix in LegacyPrefixes)
                if (shaderName.StartsWith(prefix)) return true;

            return false;
        }

        static bool IsTransparent(Material mat, string shaderName)
        {
            if (mat.renderQueue >= 3000) return true;
            if (shaderName.Contains("Transparent")) return true;
            string renderType = mat.GetTag("RenderType", false, "");
            return renderType == "Transparent";
        }

        static Texture GetTexture(Material mat, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasProperty(n))
                {
                    var t = mat.GetTexture(n);
                    if (t != null) return t;
                }
            return null;
        }

        static Color GetColor(Material mat, Color fallback, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasProperty(n))
                {
                    var c = mat.GetColor(n);
                    if (c.maxColorComponent > 0.0001f || c.a > 0.0001f) return c;
                }
            return fallback;
        }

        static float GetFloat(Material mat, float fallback, params string[] names)
        {
            foreach (var n in names)
                if (mat.HasProperty(n)) return mat.GetFloat(n);
            return fallback;
        }
    }
}
#endif