// CampaignPlaymodeProbe.cs
// -----------------------------------------------------------------------------
// 战役 Play Mode 冒烟测试入口：打开 AngryBots 战役场景，临时挂一个运行时探针，
// 进入 Play Mode。探针自身在 8 秒后自检并写报告、退出编辑器。
// 用法（不要加 -quit）：-executeMethod ABGameplay.CampaignPlaymodeProbe.Run
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using ABGameplay.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
    public static class CampaignPlaymodeProbe
    {
        const string ScenePath = "Assets/U6Port/Scenes/AngryBots.unity";

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var go = new GameObject("[CampaignPlaymodeProbe]");
            go.AddComponent<CampaignPlaymodeProbeRunner>();

            EditorApplication.EnterPlaymode();
        }
    }
}
#endif