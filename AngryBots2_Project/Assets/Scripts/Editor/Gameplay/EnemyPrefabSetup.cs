using System.Collections.Generic;
using System.Text;
using ABGameplay.Enemies;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ABGameplay
{
	/// <summary>
	/// P3B 敌人预制体改造器（Editor）。
	/// <para>
	/// <b>不修改原 prefab</b>：从旧 prefab 加载内容，改造后另存为 <c>Assets/Prefabs/Enemies/Ex/&lt;Name&gt;_Ex.prefab</c>。
	/// 旧场景 / 旧代码继续引用原文件，互不影响。
	/// </para>
	/// <para>
	/// 对每个目标执行的改造：
	/// <list type="number">
	/// <item>移除旧 <c>Health</c>（<c>damageSignals</c> 未初始化会在受伤时 NRE）；</item>
	/// <item>把旧 <c>EnemyHealth</c> 换成 <c>EnemyHealthEx</c>（保留 totalHealth / pointToGive / explosionPrefab）；</item>
	/// <item>补挂 <see cref="EnemyAgent"/>（种类 / 定义 / 计分）；</item>
	/// <item>补挂对应 <c>EnemyBehaviour</c>（Mech / Buzzer / Kamikaze / Boss）；</item>
	/// <item>禁用旧的移动 / AI 控制脚本，避免两套 AI 抢控制权；</item>
	/// <item>确保有 Collider + Rigidbody / NavMeshAgent；</item>
	/// <item>生成对应的 <see cref="EnemyDefinition"/> 资产并回填到 prefab。</item>
	/// </list>
	/// </para>
	/// <para>用法：<c>-executeMethod ABGameplay.EnemyPrefabSetup.Run</c></para>
	/// </summary>
	public static class EnemyPrefabSetup
	{
		const string EnemyDir = "Assets/Prefabs/Enemies";
		const string ExDir = "Assets/Prefabs/Enemies/Ex";
		const string DefinitionDir = "Assets/Scripts/Gameplay/Enemies/Definitions";

		/// <summary>一个改造目标。</summary>
		sealed class Target
		{
			public string SourcePrefab;
			public string OutputName;
			public EnemyKind Kind;
			public float ModelScale = 1f;
			public bool UseNavMesh;
			public bool KeepGravity;
		}

		static readonly Target[] Targets =
		{
			new Target
			{
				SourcePrefab = EnemyDir + "/EnemyMech.prefab",
				OutputName = "EnemyMech_Ex",
				Kind = EnemyKind.Mech,
				ModelScale = 1f,
				UseNavMesh = false,   // 旧 Mech 用 Rigidbody + MechMovementMotor；新 AI 也走刚体回退
				KeepGravity = true,
			},
			new Target
			{
				SourcePrefab = EnemyDir + "/EnemyBuzzer.prefab",
				OutputName = "EnemyBuzzer_Ex",
				Kind = EnemyKind.Buzzer,
				ModelScale = 1f,
				UseNavMesh = false,
				KeepGravity = false,
			},
			new Target
			{
				SourcePrefab = EnemyDir + "/KamikazeBuzzer.prefab",
				OutputName = "KamikazeBuzzer_Ex",
				Kind = EnemyKind.Kamikaze,
				ModelScale = 1f,
				UseNavMesh = false,
				KeepGravity = false,
			},
			new Target
			{
				// Boss 复用蜘蛛模型放大 3 倍（SpiderQueen）。
				SourcePrefab = EnemyDir + "/EnemySpider.prefab",
				OutputName = "EnemySpiderQueen_Ex",
				Kind = EnemyKind.Boss,
				ModelScale = 3f,
				UseNavMesh = false,
				KeepGravity = true,
			},
		};

		/// <summary>旧脚本里需要禁用的类型（避免与新的 EnemyBehaviour 抢控制）。</summary>
		static readonly System.Type[] LegacyControllers =
		{
			typeof(AI),
			typeof(MechMovementMotor),
			typeof(MechAttackMoveController),
			typeof(HoverMovementMotor),
			typeof(KamikazeMovementMotor),
			typeof(BuzzerKamikazeControllerAndAi),
			typeof(SwarmAI),
			typeof(SlowBulletFire),
			typeof(EnemyAttack),
			typeof(EnemyChaseMovement),
			typeof(EnemyPatrol),
			typeof(AttackMoveController),
			typeof(PatrolMoveController),
			typeof(SpiderAttackMoveController),
			typeof(SpiderReturnMoveController),
			typeof(Health),
		};

		static readonly StringBuilder Report = new StringBuilder();

		[MenuItem("ABGameplay/5. Build Enemy Prefabs (P3B)")]
		public static void Run()
		{
			Report.Clear();
			Report.AppendLine("=== AB2 P3B 敌人预制体改造 === " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

			EnsureFolder(ExDir);
			EnsureFolder(DefinitionDir);

			int built = 0;
			foreach (var target in Targets)
			{
				if (Process(target)) built++;
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Report.AppendLine();
			Report.AppendLine("改造完成：" + built + " / " + Targets.Length);
			Report.AppendLine("P3B PREFAB RESULT: " + (built == Targets.Length ? "PASS" : "PARTIAL (" + built + "/" + Targets.Length + ")"));

			Debug.Log("[ABGameplay][EnemyPrefabSetup]\n" + Report);
			WriteReport();

			if (built != Targets.Length) EditorApplication.Exit(1);
		}

		static bool Process(Target target)
		{
			var source = AssetDatabase.LoadAssetAtPath<GameObject>(target.SourcePrefab);
			if (source == null)
			{
				Report.AppendLine("[skip] 源预制体不存在：" + target.SourcePrefab);
				return false;
			}

			var contents = PrefabUtility.LoadPrefabContents(target.SourcePrefab);
			if (contents == null)
			{
				Report.AppendLine("[fail] 无法加载预制体内容：" + target.SourcePrefab);
				return false;
			}

			try
			{
				Report.AppendLine();
				Report.AppendLine("── " + target.OutputName + "（" + target.Kind + "）← " + target.SourcePrefab);

				// 1) 模型缩放（Boss 用放大版）
				if (!Mathf.Approximately(target.ModelScale, 1f))
				{
					contents.transform.localScale = Vector3.one * target.ModelScale;
					Report.AppendLine("  [scale] 根节点缩放 x" + target.ModelScale);
				}

				// 2) 移除旧 Health（damageSignals 未初始化会 NRE）
				int removedHealth = RemoveComponents<Health>(contents);
				if (removedHealth > 0) Report.AppendLine("  [remove] 旧 Health x" + removedHealth);

				// 3) 旧 EnemyHealth → EnemyHealthEx（替换，不能共存）
				bool replacedHealth = ReplaceEnemyHealth(contents);

				// 4) 禁用旧控制器
				int disabled = DisableLegacyControllers(contents);
				Report.AppendLine("  [disable] 旧控制器 x" + disabled);

				// 5) 确保血量组件
				var healthEx = contents.GetComponent<EnemyHealthEx>();
				if (healthEx == null)
				{
					healthEx = contents.AddComponent<EnemyHealthEx>();
					Report.AppendLine("  [add] EnemyHealthEx（新建）");
				}
				if (replacedHealth) Report.AppendLine("  [replace] EnemyHealth → EnemyHealthEx");

				// 6) 定义资产
				var definition = GetOrCreateDefinition(target.Kind);
				if (healthEx != null && definition != null)
					healthEx.ResetHealth(Mathf.RoundToInt(definition.maxHealth));

				// 7) EnemyAgent
				var agent = contents.GetComponent<EnemyAgent>();
				if (agent == null)
				{
					agent = contents.AddComponent<EnemyAgent>();
					Report.AppendLine("  [add] EnemyAgent");
				}
				agent.kind = target.Kind;
				agent.definition = definition;
				agent.scoreValue = definition != null ? definition.score : 10;
				agent.armor = definition != null ? definition.armor : 0f;

				// 8) EnemyBehaviour
				var behaviour = AttachBehaviour(contents, target.Kind, definition);
				if (behaviour != null)
					Report.AppendLine("  [add] " + behaviour.GetType().Name);

				// 9) 物理：Collider + Rigidbody / NavMeshAgent
				EnsureCollider(contents);
				EnsurePhysicsBody(contents, target);

				// 10) 回填定义里的 prefab 引用（保存后才有资产路径，这里先记下）
				string outputPath = ExDir + "/" + target.OutputName + ".prefab";

				PrefabUtility.SaveAsPrefabAsset(contents, outputPath);
				Report.AppendLine("  [save] " + outputPath);

				// 11) 保存后再把 prefab 绑定回定义资产（此时资产已存在）
				var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
				if (definition != null && saved != null)
				{
					definition.prefab = saved;
					EditorUtility.SetDirty(definition);
					Report.AppendLine("  [link] " + definition.name + ".prefab → " + target.OutputName);
				}

				return saved != null;
			}
			catch (System.Exception exception)
			{
				Report.AppendLine("  [fail] " + exception.GetType().Name + ": " + exception.Message);
				return false;
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(contents);
			}
		}

		// ─── 组件处理 ───

		static int RemoveComponents<T>(GameObject root) where T : Component
		{
			var components = root.GetComponentsInChildren<T>(true);
			int count = 0;
			foreach (var component in components)
			{
				if (component == null) continue;
				Object.DestroyImmediate(component);
				count++;
			}
			return count;
		}

		/// <summary>把旧 EnemyHealth 替换成 EnemyHealthEx（复制血量字段）。</summary>
		static bool ReplaceEnemyHealth(GameObject root)
		{
			var legacy = root.GetComponentsInChildren<EnemyHealth>(true);
			bool replaced = false;

			foreach (var old in legacy)
			{
				if (old == null) continue;
				if (old is EnemyHealthEx) continue;

				var host = old.gameObject;
				int totalHealth = Mathf.Max(1, old.totalHealth);
				int points = old.pointToGive;
				var explosion = old.explosionPrefab;

				bool wasEnabled = old.enabled;
				old.enabled = false;
				Object.DestroyImmediate(old);

				var upgraded = host.AddComponent<EnemyHealthEx>();
				upgraded.totalHealth = totalHealth;
				upgraded.pointToGive = points;
				upgraded.explosionPrefab = explosion;
				upgraded.ResetHealth(totalHealth);
				upgraded.enabled = wasEnabled;
				replaced = true;
			}

			return replaced;
		}

		static int DisableLegacyControllers(GameObject root)
		{
			int disabled = 0;
			var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

			foreach (var behaviour in behaviours)
			{
				if (behaviour == null) continue;
				if (behaviour is EnemyBehaviour || behaviour is EnemyAgent || behaviour is EnemyHealthAdapter) continue;
				if (!behaviour.enabled) continue;

				var type = behaviour.GetType();
				foreach (var legacy in LegacyControllers)
				{
					if (type == legacy || type.IsSubclassOf(legacy))
					{
						behaviour.enabled = false;
						disabled++;
						break;
					}
				}
			}
			return disabled;
		}

		static EnemyBehaviour AttachBehaviour(GameObject root, EnemyKind kind, EnemyDefinition definition)
		{
			var existing = root.GetComponent<EnemyBehaviour>();
			if (existing != null)
			{
				if (definition != null) existing.definition = definition;
				return existing;
			}

			EnemyBehaviour behaviour = null;

			switch (kind)
			{
				case EnemyKind.Mech:
					behaviour = root.AddComponent<MechBehaviour>();
					break;
				case EnemyKind.Buzzer:
					behaviour = root.AddComponent<BuzzerBehaviour>();
					break;
				case EnemyKind.Kamikaze:
					behaviour = root.AddComponent<KamikazeBehaviour>();
					break;
				case EnemyKind.Boss:
					var boss = root.AddComponent<BossBehaviour>();
					boss.phases = BossPhase.CreateDefaultPhases();
					behaviour = boss;
					break;
				default:
					// Spider 沿用旧 AI 体系，不挂新状态机。
					return null;
			}

			if (definition != null) behaviour.definition = definition;
			return behaviour;
		}

		static void EnsureCollider(GameObject root)
		{
			if (root.GetComponentInChildren<Collider>(true) != null) return;

			var collider = root.AddComponent<CapsuleCollider>();
			collider.center = Vector3.up;
			collider.height = 2f;
			collider.radius = 0.5f;
			Report.AppendLine("  [add] CapsuleCollider（源预制体没有碰撞体）");
		}

		static void EnsurePhysicsBody(GameObject root, Target target)
		{
			var body = root.GetComponent<Rigidbody>();
			if (body == null)
			{
				body = root.AddComponent<Rigidbody>();
				Report.AppendLine("  [add] Rigidbody");
			}
			body.useGravity = target.KeepGravity;
			body.drag = target.KeepGravity ? 0f : 1.5f;
			body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
			if (body.mass <= 0f) body.mass = target.Kind == EnemyKind.Boss ? 400f : 80f;

			var nav = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
			if (target.UseNavMesh && nav == null)
			{
				nav = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
				Report.AppendLine("  [add] NavMeshAgent");
			}
			if (nav != null && !target.UseNavMesh)
			{
				nav.enabled = false;
				Report.AppendLine("  [disable] NavMeshAgent（使用刚体移动）");
			}
		}

		// ─── 定义资产 ───

		static EnemyDefinition GetOrCreateDefinition(EnemyKind kind)
		{
			string path = DefinitionDir + "/Enemy_" + kind + ".asset";
			var existing = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
			if (existing != null) return existing;

			var defaults = EnemyDefinition.CreateRuntimeDefault(kind);
			var asset = Object.Instantiate(defaults);
			asset.name = "Enemy_" + kind;
			defaults.prefab = null;

			AssetDatabase.CreateAsset(asset, path);
			Object.DestroyImmediate(defaults);

			Report.AppendLine("  [asset] 新建定义 " + path);
			return asset;
		}

		// ─── 工具 ───

		static void EnsureFolder(string path)
		{
			if (AssetDatabase.IsValidFolder(path)) return;

			var parts = path.Split('/');
			string current = parts[0];

			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}

		static void WriteReport()
		{
			try
			{
				const string logDir = @"E:\MyPlayGround\_logs";
				if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
				System.IO.File.WriteAllText(
					System.IO.Path.Combine(logDir, "gc_p3b_prefab_report.md"),
					Report.ToString(),
					new UTF8Encoding(false));
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[EnemyPrefabSetup] 写入报告失败：" + exception.Message);
			}
		}

		/// <summary>只生成定义资产（不碰 prefab），供其它自检复用。</summary>
		public static List<EnemyDefinition> EnsureDefinitions()
		{
			EnsureFolder(DefinitionDir);
			var result = new List<EnemyDefinition>();
			foreach (EnemyKind kind in System.Enum.GetValues(typeof(EnemyKind)))
				result.Add(GetOrCreateDefinition(kind));
			AssetDatabase.SaveAssets();
			return result;
		}
	}
}