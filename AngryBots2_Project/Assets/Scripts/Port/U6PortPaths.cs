// U6PortPaths.cs
// -----------------------------------------------------------------------------
// P1 资产移植 —— 路径常量 (namespace ABPort)
//
// 该文件不含 #if UNITY_EDITOR，因此默认进入 Assembly-CSharp（运行时程序集），
// 保证 Assets/Scripts/Editor/Port/ 下的编辑器脚本在编译时一定先于编辑器程序集
// 编译完成并可见（避免烧入缓存导致的 "method not found"）。
// -----------------------------------------------------------------------------
namespace ABPort
{
    public static class U6PortPaths
    {
        /// <summary>只读源工程 Assets 根目录。</summary>
        public const string U6Root = @"E:\MyPlayGround\AngryBots-Unity6\Assets";

        /// <summary>统一日志目录（E 盘，绝不写 C 盘）。</summary>
        public const string LogDir = @"E:\MyPlayGround\_logs";

        public const string PortRoot = "Assets/U6Port";
        public const string CampaignDir = "Assets/U6Port/Campaign";
        public const string MaterialsDir = "Assets/U6Port/Materials";
        public const string TexturesDir = "Assets/U6Port/Textures";
        public const string ShadersDir = "Assets/U6Port/Shaders";
        public const string ScenesDir = "Assets/U6Port/Scenes";
        public const string ScriptsDir = "Assets/U6Port/Scripts";
        public const string ModelsDir = "Assets/U6Port/Models";
    }
}