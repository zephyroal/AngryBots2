// U6PortCommon.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 共享工具 (namespace ABPort)
//
// 提供：
//   * 路径常量（只读源 / 目标 / 日志）
//   * U6 资产 -> AB2 目标路径映射（战役专属资产全部规整进 Assets/U6Port/）
//   * guid 解析 / 生成 / 引用改写
//   * 编码检测与 GBK -> UTF-8 转码
//   * meta 文件生成
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ABPort
{
    public static class U6PortCommon
    {
        // --- 路径（实际常量在 ABPort.U6PortPaths，位于运行时程序集） ----------
        public const string U6Root = U6PortPaths.U6Root;
        public const string LogDir = U6PortPaths.LogDir;
        public const string PortRoot = U6PortPaths.PortRoot;
        public const string CampaignDir = U6PortPaths.CampaignDir;
        public const string MaterialsDir = U6PortPaths.MaterialsDir;
        public const string TexturesDir = U6PortPaths.TexturesDir;
        public const string ShadersDir = U6PortPaths.ShadersDir;
        public const string ScenesDir = U6PortPaths.ScenesDir;
        public const string ScriptsDir = U6PortPaths.ScriptsDir;
        public const string ModelsDir = U6PortPaths.ModelsDir;

        /// <summary>
        /// "Assets/U6Port/xxx" -> 磁盘绝对路径。
        /// 注意：入参必须是【含 Assets/ 前缀的工程相对路径】，否则会落到工程根目录（曾因此误写过 1301 个文件）。
        /// </summary>
        public static string AbsPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("AbsPath: empty asset path");
            var p = Slash(assetPath.TrimStart('/'));
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("AbsPath: path must start with 'Assets/': " + assetPath);
            return Path.Combine(Application.dataPath, p.Substring("Assets/".Length)).Replace('/', '\\');
        }

        public static string ToAssetPath(string absPath)
        {
            var ad = Application.dataPath.Replace('\\', '/');
            var p = absPath.Replace('\\', '/');
            if (p.StartsWith(ad, StringComparison.OrdinalIgnoreCase)) return "Assets" + p.Substring(ad.Length);
            return null;
        }

        // --- GUID ------------------------------------------------------------
        static readonly Regex GuidLine = new Regex(@"^guid:\s*([0-9a-f]{32})", RegexOptions.Multiline | RegexOptions.Compiled);
        public static readonly Regex GuidRef = new Regex(@"guid: ([0-9a-f]{32})", RegexOptions.Compiled);
        public static readonly Regex FileIdRef = new Regex(@"fileID: (-?\d+), guid: ([0-9a-f]{32})", RegexOptions.Compiled);

        /// <summary>读取某个 .meta 里的 guid。失败返回 null。</summary>
        public static string ReadMetaGuid(string metaFullPath)
        {
            try
            {
                if (!File.Exists(metaFullPath)) return null;
                var m = GuidLine.Match(File.ReadAllText(metaFullPath, Encoding.UTF8));
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>构建 root 下所有 .meta 的 guid -> 相对路径（不含 .meta）。</summary>
        public static Dictionary<string, string> BuildGuidMap(string root)
        {
            var h = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return h;
            foreach (var m in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
            {
                var g = ReadMetaGuid(m);
                if (g == null) continue;
                h[g] = m.Substring(root.Length + 1);
            }
            return h;
        }

        public static Dictionary<string, string> BuildU6GuidMap() => BuildGuidMap(U6Root);
        public static Dictionary<string, string> BuildAb2GuidMap() => BuildGuidMap(Application.dataPath);

        static readonly HashSet<string> _usedGuids = new HashSet<string>(StringComparer.Ordinal);
        static bool _usedLoaded;

        /// <summary>生成一个不与 AB2 现有 guid 冲突的新 guid。</summary>
        public static string NewGuid()
        {
            if (!_usedLoaded)
            {
                foreach (var g in BuildAb2GuidMap().Keys) _usedGuids.Add(g);
                _usedLoaded = true;
            }
            while (true)
            {
                var g = Guid.NewGuid().ToString("N");
                if (_usedGuids.Contains(g)) continue;
                _usedGuids.Add(g);
                return g;
            }
        }

        /// <summary>
        /// 判断一个 U6 guid 在 AB2 中是否可原样保留（identity：相对路径一致，或 U6 的
        /// 'AngryBots2/xxx' 对应 AB2 的 'xxx'）。
        /// </summary>
        public static bool IsIdentity(string u6RelPath, string ab2RelPath)
        {
            var a = Norm(u6RelPath);
            var b = Norm(ab2RelPath);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            const string pfx = "AngryBots2\\";
            return a.StartsWith(pfx, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(a.Substring(pfx.Length), b, StringComparison.OrdinalIgnoreCase);
        }

        public static string Norm(string rel) => rel.Replace('/', '\\');
        public static string Slash(string rel) => rel.Replace('\\', '/');

        // --- 编码 ------------------------------------------------------------
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>读取文本并自动识别 GBK(936)：不是合法 UTF-8 时按 GBK 解码。</summary>
        public static string ReadTextAuto(string path, out bool wasGb)
        {
            wasGb = false;
            var bytes = File.ReadAllBytes(path);
            // 严格 UTF-8 校验
            try
            {
                var strict = new UTF8Encoding(false, true);
                var s = strict.GetString(bytes);
                return s;
            }
            catch (DecoderFallbackException)
            {
                wasGb = true;
                // .NET Framework (Unity Mono) 自带 GBK 936 代码页，无需 RegisterProvider
                try { return Encoding.GetEncoding(936).GetString(bytes); }
                catch { return Encoding.Default.GetString(bytes); }
            }
        }

        public static void WriteUtf8(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, Utf8NoBom);
        }

        // --- meta ------------------------------------------------------------
        /// <summary>按扩展名写一个最小的、Unity 能接受的 .meta。</summary>
        public static string DefaultMeta(string guid, string ext, string fileName)
        {
            ext = (ext ?? "").ToLowerInvariant();
            var sb = new StringBuilder();
            sb.Append("fileFormatVersion: 2\n");
            sb.Append("guid: ").Append(guid).Append('\n');
            switch (ext)
            {
                case ".shader":
                case ".cginc":
                case ".hlsl":
                    sb.Append("ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".cs":
                    sb.Append("MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".png": case ".psd": case ".tga": case ".tif": case ".jpg": case ".jpeg": case ".exr":
                case ".hdr": case ".bmp": case ".gif": case ".iff": case ".pict":
                    sb.Append("TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 12\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  vTOnly: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {x: 0.5, y: 0.5}\n  spritePixelsToUnits: 100\n  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationDetail: -1\n  textureType: 0\n  textureShape: 1\n  singleChannelComponent: 0\n  flipbookRows: 1\n  flipbookColumns: 1\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  ignorePngGamma: 0\n  applyGammaDecoding: 0\n  cookieLightType: 0\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    physicsShape: []\n    bones: []\n    spriteID: 5e97eb03825dee720800000000000000\n    internalID: 0\n    vertices: []\n    indices: \n    edges: []\n    weights: []\n    secondaryTextures: []\n  spritePackingTag: \n  pSDRemoveMatte: 0\n  pSDShowRemoveMatteOption: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff":
                    sb.Append("AudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 1\n    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 1\n  loadInBackground: 0\n  ambisonic: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".prefab":
                    sb.Append("PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".mat":
                    sb.Append("NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".asset":
                    sb.Append("NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".fbx": case ".obj": case ".ma": case ".mb": case ".max": case ".blend": case ".dae":
                    sb.Append("ModelImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".anim": case ".controller": case ".overrideController":
                    sb.Append("NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 7400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".unity":
                    sb.Append("DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".physicMaterial":
                    sb.Append("NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 13400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".cubemap":
                    sb.Append("NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 8900000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".mtl":
                case ".json":
                case ".txt":
                case ".xml":
                case ".bytes":
                    sb.Append("TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                case ".ttf": case ".otf":
                    sb.Append("TrueTypeFontImporter:\n  externalObjects: {}\n  serializedVersion: 4\n  fontSize: 16\n  forceTextureCase: -2\n  characterSpacing: 0\n  characterPadding: 1\n  includeFontData: 1\n  fontNames:\n  - ").Append(Path.GetFileNameWithoutExtension(fileName)).Append("\n  fallbackFontReferences: []\n  customCharacters: \n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n  useLegacyBoundsCalculation: 0\n  shouldRoundAdvanceValue: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
                default:
                    sb.Append("DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    break;
            }
            return sb.ToString();
        }

        /// <summary>确保 targetFull 存在一个 .meta，且 guid 为给定值。</summary>
        public static void EnsureMeta(string targetFullPath, string guid)
        {
            var meta = targetFullPath + ".meta";
            if (File.Exists(meta)) return; // 绝不覆盖已存在的 meta
            var ext = Path.GetExtension(targetFullPath);
            WriteUtf8(meta, DefaultMeta(guid, ext, Path.GetFileName(targetFullPath)));
        }

        /// <summary>确保目录及其所有父目录（到 Assets 为止）都有 .meta。</summary>
        public static void EnsureFolderMeta(string assetFolderPath)
        {
            var parts = assetFolderPath.Split('/');
            var cur = parts[0]; // Assets
            for (int i = 1; i < parts.Length; i++)
            {
                cur += "/" + parts[i];
                var abs = AbsPath(cur);
                var meta = abs + ".meta";
                if (File.Exists(meta)) continue;
                var g = NewGuid();
                WriteUtf8(meta, "fileFormatVersion: 2\nguid: " + g +
                    "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
            }
        }

        // --- 哈希 ------------------------------------------------------------
        public static string Sha1(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // --- 日志 ------------------------------------------------------------
        public static void AppendLog(string markdown)
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(Path.Combine(LogDir, "port_assets_log.md"), markdown + "\n", Utf8NoBom);
        }

        public static string NowStamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
#endif