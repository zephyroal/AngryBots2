// CampaignContentSetup.cs
// -----------------------------------------------------------------------------
// 一次性生成战役内容资产：敌人定义（含 Spider）、刷怪表、5 个关卡定义、
// Resources/CampaignConfig、主菜单场景，并注册 Build Settings。
// 用法：-executeMethod ABGameplay.CampaignSetup.RunAll
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABGameplay.Campaign;
using ABGameplay.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ABGameplay
{
    public static class CampaignSetup
    {
        const string DefinitionDir = "Assets/Scripts/Gameplay/Enemies/Definitions";
        const string ResourcesDir = "Assets/Resources/ABCampaign";
        const string MissionDir = "Assets/Resources/ABCampaign/Missions";
        const string PlayerPrefabPath = "Assets/Prefabs/Character/Player_Prefab.prefab";
        const string SpiderPrefabPath = "Assets/Prefabs/Enemies/EnemySpider_Prefab.prefab";
        const string MechPrefabPath = "Assets/Prefabs/Enemies/Ex/EnemyMech_Ex.prefab";
        const string BuzzerPrefabPath = "Assets/Prefabs/Enemies/Ex/EnemyBuzzer_Ex.prefab";
        const string KamikazePrefabPath = "Assets/Prefabs/Enemies/Ex/KamikazeBuzzer_Ex.prefab";
        const string BossPrefabPath = "Assets/Prefabs/Enemies/Ex/EnemySpiderQueen_Ex.prefab";
        const string MenuScenePath = "Assets/U6Port/Scenes/MainMenu.unity";

        [MenuItem("ABGameplay/Campaign/Setup Content")]
        public static void RunAll()
        {
            RepairExPrefabs();
            Directory.CreateDirectory(ResourcesDir);
            Directory.CreateDirectory(MissionDir);
            AssetDatabase.Refresh();

            var spider = EnsureSpiderDefinition();
            var definitions = new List<EnemyDefinition>
            {
                spider,
                Load<EnemyDefinition>(DefinitionDir + "/Enemy_Mech.asset"),
                Load<EnemyDefinition>(DefinitionDir + "/Enemy_Buzzer.asset"),
                Load<EnemyDefinition>(DefinitionDir + "/Enemy_Kamikaze.asset"),
                Load<EnemyDefinition>(DefinitionDir + "/Enemy_Boss.asset"),
            };

            AssignPrefab(definitions[1], MechPrefabPath);
            AssignPrefab(definitions[2], BuzzerPrefabPath);
            AssignPrefab(definitions[3], KamikazePrefabPath);
            AssignPrefab(definitions[4], BossPrefabPath);

            var table = EnsureSpawnTable(definitions);
            var missions = EnsureMissions();
            EnsureConfig(missions, table, spider);
            EnsureMenuScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CampaignSetup] 完成：missions=" + missions.Count);
        }

        [MenuItem("ABGameplay/Campaign/Repair Ex Enemy Prefabs")]
        public static void RepairExPrefabs()
        {
            var paths = new[]
            {
                MechPrefabPath, BuzzerPrefabPath, KamikazePrefabPath, BossPrefabPath,
            };

            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int removed = 0;
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = transform.gameObject;
                        if (go.GetComponents<Component>().Any(c => c == null))
                            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    }

                    var health = root.GetComponent<EnemyHealthEx>();
                    if (health == null)
                    {
                        health = root.AddComponent<EnemyHealthEx>();
                        health.totalHealth = 100;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("[CampaignSetup] 修复预制体 " + Path.GetFileName(path) + "：移除空脚本 " + removed);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogWarning("[CampaignSetup] 缺失资产 " + path);
            return asset;
        }

        static EnemyDefinition EnsureSpiderDefinition()
        {
            const string path = DefinitionDir + "/Enemy_Spider.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (existing != null)
            {
                AssignPrefab(existing, SpiderPrefabPath);
                return existing;
            }

            var definition = EnemyDefinition.CreateRuntimeDefault(EnemyKind.Spider);
            definition.name = "Enemy_Spider";
            definition.displayName = "Spider";
            definition.maxHealth = 100;
            definition.score = 20;
            definition.moveSpeed = 3.2f;
            definition.attackDamage = 10;
            definition.meleeRange = 2.4f;
            AssetDatabase.CreateAsset(definition, path);
            AssignPrefab(definition, SpiderPrefabPath);
            return definition;
        }

        static void AssignPrefab(EnemyDefinition definition, string prefabPath)
        {
            if (definition == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[CampaignSetup] 预制体缺失: " + prefabPath);
                return;
            }
            if (definition.prefab != prefab)
            {
                definition.prefab = prefab;
                EditorUtility.SetDirty(definition);
            }
        }

        static EnemySpawnTable EnsureSpawnTable(List<EnemyDefinition> definitions)
        {
            const string path = ResourcesDir + "/EnemySpawnTable_Campaign.asset";
            var table = AssetDatabase.LoadAssetAtPath<EnemySpawnTable>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<EnemySpawnTable>();
                table.name = "EnemySpawnTable_Campaign";
                AssetDatabase.CreateAsset(table, path);
            }

            table.composition = new List<EnemySpawnWeight>
            {
                Weight(definitions[0], 3f, 1),
                Weight(definitions[3], 2f, 2),
                Weight(definitions[2], 1.5f, 2),
                Weight(definitions[1], 1f, 3),
            };
            table.bossDefinition = definitions[4];
            table.totalWaves = 6;
            table.bossEveryNWaves = 3;
            table.waveTimeLimit = 120f;
            table.waveDelay = 4f;
            table.spawnInterval = 0.5f;
            table.firstWaveDelay = 2f;
            table.healthGrowthPerWave = 0.1f;
            table.damageGrowthPerWave = 0.05f;
            table.speedGrowthPerWave = 0.02f;
            table.grenadeRefillPerWave = 1;
            table.healPerWave = 15;
            table.spawnProtectionTime = 1.5f;

            EditorUtility.SetDirty(table);
            return table;
        }

        static EnemySpawnWeight Weight(EnemyDefinition definition, float weight, int minWave)
        {
            return new EnemySpawnWeight { definition = definition, weight = weight, minWave = minWave };
        }

        static List<MissionDefinition> EnsureMissions()
        {
            var defaults = MissionDefinition.CreateDefaultCampaign();
            var result = new List<MissionDefinition>();

            for (int i = 0; i < defaults.Count; i++)
            {
                string path = MissionDir + "/Mission_" + i.ToString("00") + ".asset";
                var asset = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
                if (asset == null)
                {
                    asset = defaults[i];
                    asset.name = "Mission_" + i.ToString("00");
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    asset.index = defaults[i].index;
                    asset.displayName = defaults[i].displayName;
                    asset.sceneName = defaults[i].sceneName;
                    asset.mode = defaults[i].mode;
                    asset.briefing = defaults[i].briefing;
                    asset.totalWaves = defaults[i].totalWaves;
                    asset.targetKills = defaults[i].targetKills;
                    asset.timeLimit = defaults[i].timeLimit;
                    asset.defendSeconds = defaults[i].defendSeconds;
                    asset.defendRadius = defaults[i].defendRadius;
                    asset.escortDistance = defaults[i].escortDistance;
                    asset.rewardCurrency = defaults[i].rewardCurrency;
                    asset.rewardScore = defaults[i].rewardScore;
                    asset.forceBossEveryWave = defaults[i].forceBossEveryWave;
                    EditorUtility.SetDirty(asset);
                }
                result.Add(asset);
            }
            return result;
        }

        static void EnsureConfig(List<MissionDefinition> missions, EnemySpawnTable table, EnemyDefinition spider)
        {
            const string path = ResourcesDir + "/CampaignConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<CampaignConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CampaignConfig>();
                config.name = "CampaignConfig";
                AssetDatabase.CreateAsset(config, path);
            }

            config.missions = missions;
            config.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            config.fallbackEnemyPrefab = spider != null ? spider.prefab : null;
            config.enemyTable = table;
            EditorUtility.SetDirty(config);
        }

        static void EnsureMenuScene()
        {
            if (File.Exists(MenuScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, MenuScenePath);
            Debug.Log("[CampaignSetup] 创建主菜单场景 " + MenuScenePath);
        }

        [MenuItem("ABGameplay/Campaign/Register Build Settings")]
        public static void RegisterBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScenePath, true)
            };

            string[] ordered =
            {
                "Assets/Scenes/Scenes/_CompletedLevel.unity",
                "Assets/U6Port/Scenes/AngryBots.unity",
                "Assets/U6Port/Scenes/3-4_Birth.unity",
                "Assets/U6Port/Scenes/3-4_Pain.unity",
                "Assets/U6Port/Scenes/Escape.unity",
                "Assets/U6Port/Scenes/Default.unity",
            };

            foreach (var path in ordered)
                if (File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, true));

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[CampaignSetup] build settings = " + scenes.Count);
        }

        [MenuItem("ABGameplay/Campaign/Setup All")]
        public static void SetupAll()
        {
            RunAll();
            RegisterBuildSettings();
        }
    }
}
#endif