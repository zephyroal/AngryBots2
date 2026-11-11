// CampaignVerification.cs
// -----------------------------------------------------------------------------
// 战役 + 玩法扩展层的自动验证（Edit Mode）。
// 用法：-executeMethod ABGameplay.CampaignVerification.Run
// 产出：E:\MyPlayGround\_logs\campaign_verification.md，末尾打印 PASS / FAIL。
// -----------------------------------------------------------------------------
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ABGameplay.Campaign;
using ABGameplay.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace ABGameplay
{
    public static class CampaignVerification
    {
        const string LogDir = @"E:\MyPlayGround\_logs";
        static readonly StringBuilder Report = new StringBuilder();
        static int failures;

        static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "[ok]   " : "[FAIL] ") + message);
            if (!condition) failures++;
        }

        static void Info(string message)
        {
            Report.AppendLine("[info] " + message);
        }

        [MenuItem("ABGameplay/Verify Campaign")]
        public static void Run()
        {
            Directory.CreateDirectory(LogDir);
            Report.AppendLine("=== AB2 战役 / 玩法扩展自动验证 === " + DateTime.Now);
            Report.AppendLine("Unity " + Application.unityVersion + " / " + Application.platform);

            VerifyTypes();
            VerifySave();
            VerifyScore();
            VerifyUpgrades();
            VerifySmgAndRewards();
            VerifyLeaderboard();
            VerifyMissions();
            VerifyContentConfig();
            VerifyBuildSettings();
            VerifyMissionFlow();
            VerifyScenes();

            Report.AppendLine();
            Report.AppendLine(failures == 0 ? "CAMPAIGN RESULT: PASS" : "CAMPAIGN RESULT: FAIL (" + failures + ")");
            File.WriteAllText(Path.Combine(LogDir, "campaign_verification.md"), Report.ToString(), new UTF8Encoding(false));
            Debug.Log(Report.ToString());

            if (failures > 0) throw new Exception("[CampaignVerification] failed: " + failures);
        }

        static void VerifyTypes()
        {
            var types = new[]
            {
                typeof(ScoreSystem), typeof(UpgradeSystem), typeof(MissionController),
                typeof(CampaignManager), typeof(CampaignUI), typeof(MainMenuUI),
                typeof(SaveData), typeof(SaveSystem), typeof(MissionDefinition), typeof(CampaignConfig),
                typeof(WaveDirectorEx), typeof(EnemySpawnTable), typeof(EnemyDefinition),
            };
            foreach (var type in types)
                Check(type != null, "类型存在: " + type.Name);
        }

        static void VerifySave()
        {
            SaveSystem.Load();
            var backup = SaveSystem.Data;

            var probe = new SaveData
            {
                totalScore = 12345,
                bestScore = 999,
                bestCombo = 7,
                currency = 321,
                highestUnlockedMission = 3,
                damageLevel = 2,
                ammoLevel = 1,
                healthLevel = 4,
                sensitivity = 3.5f,
                volume = 0.42f,
                bestRuns = new System.Collections.Generic.List<int> { 4321, 2100 },
            };
            probe.MarkCompleted(1);

            SaveSystem.ReplaceInMemory(probe);
            SaveSystem.Save();
            SaveSystem.ForgetCache();
            SaveSystem.Load();

            var loaded = SaveSystem.Data;
            Check(loaded.currency == 321, "存档货币往返正确（321）");
            Check(loaded.highestUnlockedMission == 3, "存档解锁进度往返正确（3）");
            Check(loaded.damageLevel == 2 && loaded.ammoLevel == 1 && loaded.healthLevel == 4, "升级等级往返正确");
            Check(loaded.IsCompleted(1), "关卡完成记录往返正确");
            Check(loaded.bestRuns != null && loaded.bestRuns.Count == 2 && loaded.bestRuns[0] == 4321,
                "本地排行榜随存档往返正确");
            Check(Mathf.Abs(loaded.sensitivity - 3.5f) < 0.001f, "灵敏度设置往返正确");
            Check(File.Exists(SaveSystem.FilePath), "存档文件已写入: " + SaveSystem.FilePath);

            // 还原用户真实存档
            SaveSystem.ReplaceInMemory(backup);
            SaveSystem.Save();
        }

        static void VerifyScore()
        {
            var go = new GameObject("[VerifyScore]");
            var score = go.AddComponent<ScoreSystem>();

            score.RegisterKill(50);
            Check(score.Combo == 1, "击杀后连击 = 1");
            Check(score.Score == 55, "50 分 × 1.1 倍率 = 55（实际 " + score.Score + "）");
            Check(score.Currency > 0, "击杀产生货币（" + score.Currency + "）");

            int before = score.Score;
            score.RegisterKill(50);
            Check(score.Combo == 2, "连续击杀后连击 = 2");
            Check(score.Score - before == 60, "第二杀按 1.2 倍率得 60（实际 " + (score.Score - before) + "）");

            score.ResetCombo();
            Check(score.Combo == 0, "受伤/超时后连击清零");
            Check(Mathf.Abs(score.Multiplier - 1f) < 0.001f, "连击清零后倍率回到 1.0");

            int credits = score.Currency;
            Check(score.TrySpend(credits), "货币扣款成功");
            Check(score.Currency == 0, "扣款后货币为 0");
            Check(!score.TrySpend(10), "余额不足时无法扣款");

            UnityEngine.Object.DestroyImmediate(go);
        }

        static void VerifyUpgrades()
        {
            SaveSystem.ReplaceInMemory(new SaveData());
            var scoreGo = new GameObject("[VerifyScore2]");
            var score = scoreGo.AddComponent<ScoreSystem>();
            score.AddCurrency(5000);

            // 临时玩家（tag=Player），让 UpgradeSystem 的数值应用路径可测。
            var existing = GameObject.FindGameObjectWithTag("Player");
            var player = new GameObject("[VerifyPlayer]");
            player.tag = "Player";
            var health = player.AddComponent<PlayerHealth>();
            var weapons = player.AddComponent<WeaponSystem>();
            Check(health != null && weapons != null, "临时玩家组件创建成功");

            var upgradeGo = new GameObject("[VerifyUpgrade]");
            var upgrades = upgradeGo.AddComponent<UpgradeSystem>();

            int cost = upgrades.GetCost(UpgradeKind.WeaponDamage);
            Check(cost == upgrades.baseDamageCost, "0 级升级费用 = 基础费用（" + cost + "）");
            Check(upgrades.Buy(UpgradeKind.WeaponDamage), "购买伤害升级成功");
            Check(upgrades.GetLevel(UpgradeKind.WeaponDamage) == 1, "伤害升级等级 = 1");
            Check(upgrades.GetCost(UpgradeKind.WeaponDamage) == upgrades.baseDamageCost * 2, "1 级后费用翻倍");

            upgrades.ApplyToPlayer();
            Check(weapons.rifleDamage == upgrades.baseRifleDamage + upgrades.rifleDamagePerLevel,
                "玩家步枪伤害已应用升级（" + weapons.rifleDamage + "）");
            Check(health.maxHealth >= upgrades.baseMaxHealth, "玩家最大生命值不低于基线");

            Check(upgrades.Buy(UpgradeKind.AmmoCapacity), "购买弹药升级成功");
            Check(weapons.rifleMagSize == upgrades.baseRifleMag + upgrades.rifleMagPerLevel,
                "弹匣容量已应用升级（" + weapons.rifleMagSize + "）");

            for (int i = 0; i < 10; i++) upgrades.Buy(UpgradeKind.ArmorHealth);
            Check(upgrades.GetLevel(UpgradeKind.ArmorHealth) == upgrades.maxLevel, "升级等级封顶在 maxLevel");

            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(scoreGo);
            UnityEngine.Object.DestroyImmediate(upgradeGo);
            SaveSystem.ReplaceInMemory(new SaveData());
        }

        static void VerifySmgAndRewards()
        {
            SaveSystem.ReplaceInMemory(new SaveData());

            var player = new GameObject("[VerifySmgPlayer]");
            player.tag = "Player";
            var weapons = player.AddComponent<WeaponSystem>();
            var health = player.AddComponent<PlayerHealth>();
            health.maxHealth = 100;

            weapons.SwitchTo(WeaponSystem.WeaponMode.SMG);
            Check(weapons.Mode == WeaponSystem.WeaponMode.SMG, "可切换到 SMG（第 3 把武器）");
            Check(weapons.CurrentMagSize == weapons.smgMagSize, "SMG 弹匣容量正确（" + weapons.CurrentMagSize + "）");

            int before = weapons.SmgAmmo;
            weapons.TryFire(false);
            Check(weapons.SmgAmmo == before - 1, "SMG 开火消耗弹药（" + before + " → " + weapons.SmgAmmo + "）");

            health.TakeDamage(30);
            int damaged = health.CurrentHealth;
            Check(damaged < health.maxHealth, "测试玩家已受伤（" + damaged + "）");

            var rewardObject = new GameObject("[VerifyComboReward]");
            var reward = rewardObject.AddComponent<ComboReward>();
            int grenadesBefore = weapons.Grenades;
            reward.HandleCombo(5);

            Check(health.CurrentHealth > damaged, "连击里程碑回复生命（" + damaged + " → " + health.CurrentHealth + "）");
            Check(weapons.Grenades >= grenadesBefore, "连击里程碑补充手雷（" + grenadesBefore + " → " + weapons.Grenades + "）");

            var eliteField = typeof(ABGameplay.Enemies.WaveDirectorEx).GetField("eliteChance");
            Check(eliteField != null, "WaveDirectorEx 暴露精英概率参数");

            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(rewardObject);
            SaveSystem.ReplaceInMemory(new SaveData());
        }

        static void VerifyLeaderboard()
        {
            SaveSystem.ReplaceInMemory(new SaveData());

            int[] scores = { 1200, 5000, 800, 3000, 4500, 900 };
            foreach (var score in scores) SaveSystem.RecordRun(score);

            var runs = SaveSystem.Data.bestRuns;
            Check(runs != null && runs.Count == 5, "排行榜只保留 Top 5（实际 " + (runs != null ? runs.Count : 0) + "）");
            Check(runs != null && runs[0] == 5000 && runs[4] == 900, "排行榜按分数降序截断");
            Check(runs != null && !runs.Contains(800), "最低分未进入 Top 5");

            SaveSystem.RecordRun(0);
            Check(SaveSystem.Data.bestRuns.Count == 5, "0 分不写入排行榜");

            SaveSystem.ReplaceInMemory(new SaveData());
        }

        static void VerifyMissions()
        {
            var missions = MissionDefinition.CreateDefaultCampaign();
            Check(missions.Count == 6, "默认战役包含 6 个关卡（实际 " + missions.Count + "）");
            Check(missions[0].mode == ObjectiveMode.ClearWaves, "第 1 关为清场");
            Check(missions[1].mode == ObjectiveMode.ClearWaves, "第 2 关为清场");
            Check(missions[2].mode == ObjectiveMode.Timed, "第 3 关为限时");
            Check(missions[3].mode == ObjectiveMode.Defend, "第 4 关为防守");
            Check(missions[4].mode == ObjectiveMode.Escort, "第 5 关为护送");
            Check(missions.Count > 5 && missions[5].forceBossEveryWave, "第 6 关为 Boss Rush（每波 Boss）");

            foreach (var mission in missions)
            {
                Check(!string.IsNullOrEmpty(mission.sceneName), "关卡场景名非空: " + mission.displayName);
                Check(mission.rewardCurrency > 0 && mission.rewardScore > 0, "关卡有奖励: " + mission.displayName);
            }
        }

        static void VerifyContentConfig()
        {
            var config = Resources.Load<CampaignConfig>("ABCampaign/CampaignConfig");
            Check(config != null, "Resources/ABCampaign/CampaignConfig 可加载");
            if (config == null) return;

            Check(config.missions != null && config.missions.Count == 6, "配置含 6 个关卡");
            Check(config.playerPrefab != null, "配置绑定玩家预制体");
            Check(config.enemyTable != null, "配置绑定刷怪表");
            Check(config.fallbackEnemyPrefab != null, "配置绑定兜底敌人预制体");

            var table = config.enemyTable;
            if (table == null) return;

            Check(table.composition != null && table.composition.Count >= 3, "刷怪表至少 3 种敌人权重");
            foreach (var weight in table.composition)
            {
                Check(weight.definition != null && weight.definition.prefab != null,
                    "敌人定义绑定预制体: " + (weight.definition != null ? weight.definition.displayName : "<null>"));
            }
            Check(table.bossDefinition != null && table.bossDefinition.prefab != null, "Boss 定义绑定预制体");

            var kinds = table.composition.Where(w => w.definition != null).Select(w => w.definition.kind).ToList();
            Check(kinds.Contains(EnemyKind.Spider), "刷怪表包含 Spider");
            Check(kinds.Contains(EnemyKind.Mech), "刷怪表包含 Mech");
            Check(kinds.Contains(EnemyKind.Buzzer), "刷怪表包含 Buzzer");
            Check(kinds.Contains(EnemyKind.Kamikaze), "刷怪表包含 Kamikaze");
            Check(table.bossEveryNWaves > 0, "刷怪表配置了 Boss 波间隔");

            bool anyDrop = table.composition.Any(w => w.definition != null &&
                (w.definition.healthDropChance > 0f || w.definition.ammoDropChance > 0f));
            Check(anyDrop, "敌人定义配置了血包/弹药掉落概率");
        }

        static void VerifyBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            Check(scenes.Length >= 6, "Build Settings 至少 6 个场景（实际 " + scenes.Length + "）");
            Check(scenes.Length > 0 && scenes[0].path.Contains("MainMenu"),
                "Build Settings 第 0 个是主菜单（实际 " + (scenes.Length > 0 ? scenes[0].path : "-") + "）");

            foreach (var required in new[]
            {
                "Assets/Scenes/Scenes/_CompletedLevel.unity",
                "Assets/U6Port/Scenes/AngryBots.unity",
                "Assets/U6Port/Scenes/3-4_Birth.unity",
                "Assets/U6Port/Scenes/3-4_Pain.unity",
                "Assets/U6Port/Scenes/Escape.unity",
            })
            {
                Check(scenes.Any(s => s.path == required), "Build Settings 包含 " + Path.GetFileName(required));
            }
        }

        static void VerifyMissionFlow()
        {
            SaveSystem.ReplaceInMemory(new SaveData());

            var definition = MissionDefinition.CreateDefaultCampaign()[2];
            definition = UnityEngine.Object.Instantiate(definition);
            definition.targetKills = 2;
            definition.timeLimit = 60f;

            var go = new GameObject("[VerifyMission]");
            var controller = go.AddComponent<MissionController>();
            controller.Definition = definition;
            controller.Begin(definition);

            Check(controller.Running, "限时关卡进入 Running 状态");
            Check(controller.Director != null, "关卡自动创建 WaveDirectorEx");
            Check(!string.IsNullOrEmpty(controller.ObjectiveText), "目标文案已生成: " + controller.ObjectiveText);

            GameEvents.RaiseEnemyKilled(EnemyKind.Spider, Vector3.zero, null, 10);
            GameEvents.RaiseEnemyKilled(EnemyKind.Mech, Vector3.zero, null, 10);
            controller.EvaluateObjective();

            Check(controller.Kills == 2, "击杀计数 = 2（实际 " + controller.Kills + "）");
            Check(controller.Completed, "达到击杀目标后判定胜利");
            Check(!controller.Running, "胜利后停止运行");

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(definition);

            // Boss Rush：每波都是 Boss
            var bossRush = UnityEngine.Object.Instantiate(MissionDefinition.CreateDefaultCampaign()[5]);
            bossRush.totalWaves = 3;
            var bossGo = new GameObject("[VerifyBossRush]");
            var bossController = bossGo.AddComponent<MissionController>();
            bossController.Definition = bossRush;
            bossController.Begin(bossRush);
            Check(bossController.Director != null &&
                  bossController.Director.spawnTable != null &&
                  bossController.Director.spawnTable.bossEveryNWaves == 1,
                "Boss Rush 关卡把刷怪表配置为每波 Boss");
            UnityEngine.Object.DestroyImmediate(bossGo);
            UnityEngine.Object.DestroyImmediate(bossRush);

            SaveSystem.ReplaceInMemory(new SaveData());
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static void VerifyScenes()
        {
            var scenes = new[]
            {
                "Assets/Scenes/Scenes/_CompletedLevel.unity",
                "Assets/U6Port/Scenes/AngryBots.unity",
                "Assets/U6Port/Scenes/3-4_Birth.unity",
                "Assets/U6Port/Scenes/3-4_Pain.unity",
                "Assets/U6Port/Scenes/Escape.unity",
            };

            foreach (var path in scenes)
            {
                if (!File.Exists(path))
                {
                    Check(false, "场景文件存在: " + path);
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                int objects = roots.Sum(r => r.GetComponentsInChildren<Transform>(true).Length);
                var missing = new List<string>();
                foreach (var root in roots)
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.GetComponents<Component>().Any(c => c == null)) missing.Add(PathOf(t));

                Check(objects > 10, Path.GetFileName(path) + " 场景对象数 " + objects);
                Check(missing.Count == 0, Path.GetFileName(path) + " 无缺失脚本（" + missing.Count + "）");
                foreach (var name in missing.Take(6)) Info("  缺失脚本对象: " + name);

                var tri = NavMesh.CalculateTriangulation();
                Check(tri.vertices.Length > 0, Path.GetFileName(path) + " 有 NavMesh（顶点 " + tri.vertices.Length + "）");

                if (path.Contains("AngryBots.unity") || path.Contains("Escape"))
                {
                    Check(roots.Any(r => r.name == "Main Camera"), Path.GetFileName(path) + " 有主相机");
                }
            }
        }
    }
}
#endif