using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ABGameplay
{
	/// <summary>
	/// P3 玩法基础层自检 —— <b>Edit 模式部分</b>（不依赖 PlayMode 生命周期）。
	/// <para>
	/// 只检查「不需要 Awake / OnEnable」的部分：类型存在性、纯数据 API、反射补弹、向后兼容。
	/// 注册表 / 事件总线 / 池化 / 飘字 / 拾取等需要真实生命周期的检查在
	/// <see cref="FoundationPlayModeCheck"/> 里以 PlayMode 方式验证。
	/// </para>
	/// <para>用法：<c>-executeMethod ABGameplay.FoundationCheck.Run</c>（可配 -quit）</para>
	/// </summary>
	public static class FoundationCheck
	{
		const string ScenePath = "Assets/Scenes/Scenes/_CompletedLevel.unity";
		const string LogDirectory = @"E:\MyPlayGround\_logs";

		internal static readonly StringBuilder Report = new StringBuilder();
		internal static int HardFailures;
		internal static int InfoLines;

		internal static void Hard(bool condition, string message)
		{
			Report.AppendLine((condition ? "[ok]   " : "[FAIL] ") + message);
			if (!condition) HardFailures++;
		}

		internal static void Info(string message)
		{
			Report.AppendLine("[info] " + message);
			InfoLines++;
		}

		[MenuItem("ABGameplay/3. Foundation Check (P3, Edit Mode)")]
		public static void Run()
		{
			Report.Clear();
			HardFailures = 0;
			InfoLines = 0;

			Report.AppendLine("=== AB2 P3 玩法基础层自检（Edit Mode）=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Report.AppendLine("Unity " + Application.unityVersion + " / " + Application.platform);

			try
			{
				CheckTypes();
				CheckDamageData();
				CheckDefinitions();
				CheckBackwardCompatibility();
				CheckSceneAssets();
				CheckWeaponAdapter();
				CheckRegistryApiWithoutLifecycle();
			}
			catch (Exception exception)
			{
				HardFailures++;
				Report.AppendLine("[FAIL] 自检过程中抛出异常：" + exception);
			}

			if (!Application.isPlaying)
			{
				Info("Edit 模式自检不驱动 Awake/OnEnable：注册表 / 事件总线 / 池化 / 飘字 / 拾取 的完整验证见 FoundationPlayModeCheck。");
			}

			Report.AppendLine();
			Report.AppendLine("Edit 模式 hard 检查失败：" + HardFailures + "，info 记录：" + InfoLines);
			Report.AppendLine(HardFailures == 0 ? "P3 EDIT RESULT: PASS" : "P3 EDIT RESULT: FAIL (" + HardFailures + ")");

			Debug.Log("[ABGameplay][FoundationCheck][Edit]\n" + Report);
			WriteReportFile("p3_foundation_edit_report.md");

			if (HardFailures > 0) EditorApplication.Exit(1);
		}

		// ─────────────────────── 类型 ───────────────────────

		static readonly string[] ExpectedRuntimeTypes =
		{
			"IDamageable", "IEnemyDamageable", "DamageInfo", "DamageType", "HitFeedbackLevel", "EnemyKind",
			"EnemyRegistry", "EnemyAgent", "EnemyHealthAdapter", "EnemyHealthEx",
			"PlayerHealthAdapter", "WeaponSystemAdapter", "GameEvents", "PlayerLocator",
			"DamageNumber", "DamageNumberSpawner", "DamageNumberSettings", "HitFeedback",
			"SimplePickup", "WeaponDefinition", "WeaponArchetype", "EnemyDefinition",
			"EnemyProjectileMode", "EnemyArchetype", "EnemyState", "PoolManager",
			"DamageUtility", "GameplayBootstrap", "GameplayRuntime", "GameplayMath", "PlayerSource",
		};

		static void CheckTypes()
		{
			int compileErrors = CompilationErrors.Scan(LogDirectory, "gc_p3_compile.log", DateTime.Now.AddHours(-2));
			Hard(compileErrors == 0, "batchmode 编译 0 error CS（gc_p3_compile.log 扫描到 " + compileErrors + " 条）");

			var runtimeAssembly = typeof(IDamageable).Assembly;
			var missing = new System.Collections.Generic.List<string>();
			foreach (var name in ExpectedRuntimeTypes)
				if (runtimeAssembly.GetType(name) == null) missing.Add(name);

			Hard(missing.Count == 0, "P3 运行时类型全部存在（缺失：" + (missing.Count == 0 ? "无" : string.Join(", ", missing)) + "）");
			Hard(typeof(FoundationCheck).Assembly != null, "Editor 程序集可加载（自检自身编译正常）");
		}

		// ─────────────────────── 伤害数据结构 ───────────────────────

		static void CheckDamageData()
		{
			var info = DamageInfo.Create(25f, null, new Vector3(1f, 2f, 3f), Vector3.forward);
			Hard(Mathf.Approximately(info.Amount, 25f) && info.Type == DamageType.Bullet, "DamageInfo.Create 字段正确");
			Hard(info.IsValid, "DamageInfo.IsValid 对正伤害返回 true");
			Hard(!DamageInfo.Create(0f, null, Vector3.zero, Vector3.forward).IsValid, "DamageInfo.IsValid 对 0 伤害返回 false");

			var explosion = DamageInfo.Explosion(80f, null, Vector3.one, 3f);
			Hard(explosion.Type == DamageType.Explosion && Mathf.Approximately(explosion.Knockback, 3f), "DamageInfo.Explosion 字段正确");

			Hard(GameplayMath.RoundDamage(0.2f) == 1, "GameplayMath.RoundDamage 最小为 1");
			Hard(GameplayMath.RoundDamage(0f) == 0, "GameplayMath.RoundDamage 0 伤害返回 0");
			Hard(Mathf.Approximately(GameplayMath.Exponential(10f, 1.1f, 3), 12.1f), "GameplayMath.Exponential 计算正确");
		}

		// ─────────────────────── 数据定义 ───────────────────────

		static void CheckDefinitions()
		{
			var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
			try
			{
				weapon.displayName = "Foundation Rifle";
				weapon.damage = 25f;
				weapon.fireRate = 8.5f;
				weapon.magazineSize = 30;
				weapon.reloadTime = 1.3f;

				var result = weapon.Validate();
				Hard(result.valid, "WeaponDefinition.Validate 通过合法定义（" + result.error + "）");
				Hard(Mathf.Approximately(weapon.ShotInterval, 1f / 8.5f), "WeaponDefinition.ShotInterval = 1/fireRate");
				Hard(weapon.TotalAmmo == 30 + 180, "WeaponDefinition.TotalAmmo = 弹匣 + 备弹 = " + weapon.TotalAmmo);
				Hard(weapon.InitialReserve == 180, "WeaponDefinition.InitialReserve = 备弹上限 " + weapon.InitialReserve);

				weapon.damage = 0f;
				Hard(!weapon.Validate().valid, "WeaponDefinition.Validate 能识别非法伤害");
			}
			finally
			{
				Object.DestroyImmediate(weapon);
			}

			var failures = new System.Collections.Generic.List<string>();
			foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
			{
				var definition = EnemyDefinition.CreateRuntimeDefault(kind);
				try
				{
					string error;
					if (!definition.Validate(out error)) failures.Add(kind + ":" + error);
				}
				finally
				{
					Object.DestroyImmediate(definition);
				}
			}
			Hard(failures.Count == 0, "5 种 EnemyDefinition 默认值全部合法（" + string.Join(";", failures) + "）");

			var mech = EnemyDefinition.CreateRuntimeDefault(EnemyKind.Mech);
			var boss = EnemyDefinition.CreateRuntimeDefault(EnemyKind.Boss);
			var buzzer = EnemyDefinition.CreateRuntimeDefault(EnemyKind.Buzzer);
			var kamikaze = EnemyDefinition.CreateRuntimeDefault(EnemyKind.Kamikaze);
			Hard(mech.archetype.HasFlag(EnemyArchetype.Ranged), "Mech 定义带远程标签");
			Hard(mech.armor > 0f, "Mech 定义有护甲");
			Hard(boss.IsBoss, "Boss 定义 IsBoss = true");
			Hard(buzzer.IsFlying, "Buzzer 定义 IsFlying = true");
			Hard(kamikaze.archetype.HasFlag(EnemyArchetype.SelfDestruct), "Kamikaze 定义带自爆标签");
			Object.DestroyImmediate(mech);
			Object.DestroyImmediate(boss);
			Object.DestroyImmediate(buzzer);
			Object.DestroyImmediate(kamikaze);
		}

		// ─────────────────────── 向后兼容 ───────────────────────

		static void CheckBackwardCompatibility()
		{
			Hard(typeof(WaveDirector).GetProperty("Wave") != null, "WaveDirector.Wave 属性保留");
			Hard(typeof(WaveDirector).GetMethod("Restart") != null, "WaveDirector.Restart 方法保留");
			Hard(typeof(WaveDirector).GetEvent("MissionEnded") != null, "WaveDirector.MissionEnded 事件保留");

			Hard(typeof(WeaponSystem).GetMethod("AddGrenades") != null, "WeaponSystem.AddGrenades 保留");
			Hard(typeof(WeaponSystem).GetEvent("AmmoChanged") != null, "WeaponSystem.AmmoChanged 保留");

			Hard(typeof(PlayerHealth).GetMethod("TakeDamage") != null, "PlayerHealth.TakeDamage 保留");
			Hard(typeof(PlayerHealth).GetEvent("Damaged") != null, "PlayerHealth.Damaged 保留");

			Hard(typeof(EnemyHealth).GetMethod("RemoveHealth") != null, "EnemyHealth.RemoveHealth 保留");
			Hard(typeof(EnemyHealth).GetEvent("EnemyDied") != null, "EnemyHealth.EnemyDied 静态事件保留");
			Hard(typeof(ProjectileBehaviour).GetEvent("EnemyHit") != null, "ProjectileBehaviour.EnemyHit 静态事件保留");
			Hard(typeof(EnemyAttack).GetField("damage") != null, "EnemyAttack.damage 字段保留");
			Hard(typeof(GameHud) != null, "GameHud 类型保留");
			Hard(typeof(GrenadeProjectile) != null, "GrenadeProjectile 类型保留");

			Hard(typeof(EnemyHealth).IsAssignableFrom(typeof(EnemyHealthEx)), "EnemyHealthEx 继承自 EnemyHealth");
			Hard(typeof(PlayerHealth).IsAssignableFrom(typeof(PlayerHealthAdapter)), "PlayerHealthAdapter 继承自 PlayerHealth");
			Hard(typeof(WeaponSystem).IsAssignableFrom(typeof(WeaponSystemAdapter)), "WeaponSystemAdapter 继承自 WeaponSystem");
			Hard(typeof(IDamageable).IsAssignableFrom(typeof(PlayerHealthAdapter)), "PlayerHealthAdapter 实现 IDamageable");
			Hard(typeof(IDamageable).IsAssignableFrom(typeof(EnemyHealthEx)), "EnemyHealthEx 实现 IDamageable");
		}

		// ─────────────────────── 场景 / 预制体资产 ───────────────────────

		static void CheckSceneAssets()
		{
			if (!File.Exists(ScenePath))
			{
				Info("基准场景不存在，跳过场景检查：" + ScenePath);
				return;
			}

			var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			if (!scene.isLoaded)
			{
				HardFailures++;
				Report.AppendLine("[FAIL] 基准场景无法打开");
				return;
			}

			try
			{
				var legacyEnemies = Object.FindObjectsOfType<EnemyHealth>(true).Length;
				var players = Object.FindObjectsOfType<PlayerHealth>(true).Length;
				var directors = Object.FindObjectsOfType<WaveDirector>(true).Length;
				Info("场景内 EnemyHealth 实例：" + legacyEnemies + "（WaveDirector：" + directors + "，PlayerHealth：" + players + "）");

				var navMeshTriangles = UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length / 3;
				Info("场景 NavMesh 三角形数：" + navMeshTriangles);
			}
			finally
			{
				EditorSceneManager.CloseScene(scene, true);
			}

			var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Character/Player_Prefab.prefab");
			var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/EnemySpider_Prefab.prefab");
			Hard(playerPrefab != null, "玩家预制体可加载");
			Hard(enemyPrefab != null, "敌人预制体可加载");

			if (enemyPrefab != null)
			{
				Hard(enemyPrefab.GetComponent<EnemyHealth>() != null, "敌人预制体仍有旧 EnemyHealth（向后兼容）");
				Hard(enemyPrefab.GetComponent<EnemyAttack>() != null, "敌人预制体仍有旧 EnemyAttack（向后兼容）");
			}
			if (playerPrefab != null)
				Hard(playerPrefab.GetComponent<WeaponSystem>() != null, "玩家预制体仍有旧 WeaponSystem（向后兼容）");
		}

		// ─────────────────────── WeaponSystemAdapter 反射通路 ───────────────────────

		static void CheckWeaponAdapter()
		{
			var host = new GameObject("FoundationCheck_WeaponHost");
			try
			{
				var weapons = host.AddComponent<WeaponSystemAdapter>();
				weapons.enabled = false;
				weapons.rifleMagSize = 30;
				weapons.shotgunMagSize = 8;
				host.SetActive(false);

				bool threw = false;
				int added = -1;
				try { added = weapons.AddAmmo(10); }
				catch (Exception exception)
				{
					threw = true;
					Report.AppendLine("[FAIL] WeaponSystemAdapter.AddAmmo 抛异常：" + exception.Message);
				}
				Hard(!threw && added >= 0, "WeaponSystemAdapter.AddAmmo 可调用且不抛异常（返回 " + added + "）");
				Hard(weapons.MissingAmmo >= 0, "WeaponSystemAdapter.MissingAmmo 可读取（" + weapons.MissingAmmo + "）");
				Hard(weapons.GetType().GetProperty("RifleAmmo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null,
					"WeaponSystem.RifleAmmo 私有 setter 可通过反射写入（补弹通路可用）");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		// ─────────────────────── 注册表纯 API ───────────────────────

		static void CheckRegistryApiWithoutLifecycle()
		{
			// Edit 模式下 OnEnable 不触发，这里只验证：手动 Register 后查询 / 注销 / 计数一致。
			var eventsRaised = 0;
			Action<EnemyAgent> onRegistered = agent => eventsRaised++;
			Action<EnemyAgent> onUnregistered = agent => eventsRaised++;

			EnemyRegistry.EnemyRegistered += onRegistered;
			EnemyRegistry.EnemyUnregistered += onUnregistered;

			var objects = new System.Collections.Generic.List<GameObject>();
			try
			{
				EnemyRegistry.Clear();
				int baseline = EnemyRegistry.AliveCount;

				for (int i = 0; i < 3; i++)
				{
					var go = new GameObject("FoundationCheck_Registry_" + i);
					go.SetActive(false);
					go.AddComponent<EnemyHealthEx>().totalHealth = 50;
					var agent = go.AddComponent<EnemyAgent>();
					agent.enabled = false;
					agent.kind = i == 0 ? EnemyKind.Mech : EnemyKind.Spider;
					objects.Add(go);
					EnemyRegistry.Register(agent);
				}

				Hard(EnemyRegistry.AliveCount == baseline + 3, "手动 Register 3 个敌人后 AliveCount = " + EnemyRegistry.AliveCount);
				Hard(EnemyRegistry.CountOf(EnemyKind.Mech) == 1, "CountOf(Mech) = 1");
				Hard(EnemyRegistry.FindNearest(Vector3.zero) != null, "FindNearest 能返回最近敌人");
				Hard(EnemyRegistry.GetAlive().Count == 3, "GetAlive 返回 3 个敌人");

				int killEvents = 0;
				EnemyKind lastKind = EnemyKind.Spider;
				Action<EnemyKind, Vector3, GameObject, int> onKilled = (kind, position, killer, score) =>
				{
					killEvents++;
					lastKind = kind;
				};
				GameEvents.EnemyKilled += onKilled;
				EnemyRegistry.NotifyKilled(objects[0].GetComponent<EnemyAgent>(), null, Vector3.zero, 10);
				GameEvents.EnemyKilled -= onKilled;

				Hard(killEvents == 1, "NotifyKilled 触发 EnemyKilled 事件 1 次（实际 " + killEvents + "）");
				Hard(lastKind == EnemyKind.Mech, "EnemyKilled 携带正确种类 Mech（实际 " + lastKind + "）");
				Hard(EnemyRegistry.AliveCount == baseline + 2, "击杀后 AliveCount 减少 1");
				Hard(eventsRaised == 4, "注册 / 注销事件共 4 次（3 注册 + 1 注销，实际 " + eventsRaised + "）");
			}
			finally
			{
				foreach (var go in objects)
					if (go != null) Object.DestroyImmediate(go);
				EnemyRegistry.EnemyRegistered -= onRegistered;
				EnemyRegistry.EnemyUnregistered -= onUnregistered;
				EnemyRegistry.Clear();
			}
		}

		// ─────────────────────── 写报告 ───────────────────────

		internal static void WriteReportFile(string fileName)
		{
			try
			{
				if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
				File.WriteAllText(Path.Combine(LogDirectory, fileName), Report.ToString(), new UTF8Encoding(false));
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[FoundationCheck] 写入报告失败：" + exception.Message);
			}
		}
	}
}