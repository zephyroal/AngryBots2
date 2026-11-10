using UnityEngine;

namespace ABGameplay.Enemies
{
	/// <summary>
	/// 敌人生成的编程入口：把 <see cref="EnemyDefinition"/> 实例化为可直接战斗的 GameObject。
	/// <para>
	/// 优先使用定义里的 <c>prefab</c>（由 <c>EnemyPrefabSetup</c> 生成的改造版预制体）；
	/// 预制体缺失时用程序化占位体（胶囊 + 颜色）兜底，保证 batchmode 自检 /
	/// 没有美术资产的关卡也能完整跑通 AI 与波次逻辑。
	/// </para>
	/// </summary>
	public static class EnemyFactory
	{
		/// <summary>生成一个敌人并绑定定义（不套难度缩放，缩放在 <see cref="WaveDirectorEx"/> 里做）。</summary>
		public static GameObject Spawn(EnemyDefinition definition, Vector3 position, Quaternion rotation)
		{
			if (definition == null) return null;

			GameObject instance;
			if (definition.prefab != null)
			{
				instance = Object.Instantiate(definition.prefab, position, rotation);
			}
			else
			{
				instance = CreateProcedural(definition.kind, position, rotation);
			}

			instance.name = "Enemy_" + definition.kind;

			var agent = instance.GetComponent<EnemyAgent>();
			if (agent == null) agent = instance.AddComponent<EnemyAgent>();
			agent.kind = definition.kind;
			agent.definition = definition;
			agent.scoreValue = definition.score;
			agent.armor = definition.armor;

			if (instance.GetComponent<EnemyHealthEx>() == null && instance.GetComponent<EnemyHealth>() == null)
			{
				var health = instance.AddComponent<EnemyHealthEx>();
				health.totalHealth = Mathf.RoundToInt(definition.maxHealth);
				health.ResetHealth(health.totalHealth);
			}

			if (instance.GetComponent<EnemyBehaviour>() == null)
				AttachBehaviour(instance, definition.kind, definition);

			return instance;
		}

		/// <summary>给已有对象补挂对应种类的行为组件。</summary>
		public static EnemyBehaviour AttachBehaviour(GameObject target, EnemyKind kind, EnemyDefinition definition)
		{
			if (target == null) return null;

			EnemyBehaviour behaviour;
			switch (kind)
			{
				case EnemyKind.Mech:
					behaviour = target.AddComponent<MechBehaviour>();
					break;
				case EnemyKind.Buzzer:
					behaviour = target.AddComponent<BuzzerBehaviour>();
					break;
				case EnemyKind.Kamikaze:
					behaviour = target.AddComponent<KamikazeBehaviour>();
					break;
				case EnemyKind.Boss:
					behaviour = target.AddComponent<BossBehaviour>();
					break;
				default:
					// Spider 沿用旧的 EnemyChaseMovement / EnemyPatrol / EnemyAttack 体系，
					// 不叠加新状态机，避免两套 AI 抢控制权。
					return null;
			}

			if (definition != null) behaviour.definition = definition;
			return behaviour;
		}

		/// <summary>
		/// 创建程序化占位敌人：胶囊体 + 种类配色 + 碰撞体 + Rigidbody。
		/// 用于没有美术预制体的场景（自检 / 临时关卡）。
		/// </summary>
		public static GameObject CreateProcedural(EnemyKind kind, Vector3 position, Quaternion rotation)
		{
			var root = new GameObject("Enemy_" + kind + "(Procedural)");
			root.transform.position = position;
			root.transform.rotation = rotation;

			bool flying = kind == EnemyKind.Buzzer || kind == EnemyKind.Kamikaze;

			var body = GameObject.CreatePrimitive(flying ? PrimitiveType.Sphere : PrimitiveType.Capsule);
			body.name = "Body";
			body.transform.SetParent(root.transform, false);
			body.transform.localPosition = flying ? new Vector3(0f, 0.4f, 0f) : Vector3.zero;

			float scale = kind == EnemyKind.Boss ? 2.6f : (kind == EnemyKind.Mech ? 1.6f : 1f);
			body.transform.localScale = flying
				? Vector3.one * (0.7f * scale)
				: new Vector3(0.8f * scale, 1.0f * scale, 0.8f * scale);

			var collider = body.GetComponent<Collider>();
			if (collider != null) collider.isTrigger = false;

			root.tag = "Untagged";

			var bodyRigidbody = root.AddComponent<Rigidbody>();
			bodyRigidbody.mass = kind == EnemyKind.Boss ? 400f : (kind == EnemyKind.Mech ? 160f : 40f);
			bodyRigidbody.useGravity = !flying;
			bodyRigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

			var renderer = body.GetComponent<Renderer>();
			if (renderer != null)
			{
				var material = new Material(ResolveLitShader());
				material.name = "Enemy_" + kind + "_Material";
				var color = ColorFor(kind);
				if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
				material.color = color;
				renderer.sharedMaterial = material;
			}

			return root;
		}

		/// <summary>种类对应的占位配色。</summary>
		public static Color ColorFor(EnemyKind kind)
		{
			switch (kind)
			{
				case EnemyKind.Mech: return new Color(0.35f, 0.45f, 0.75f);
				case EnemyKind.Buzzer: return new Color(0.95f, 0.85f, 0.2f);
				case EnemyKind.Kamikaze: return new Color(0.95f, 0.3f, 0.15f);
				case EnemyKind.Boss: return new Color(0.6f, 0.1f, 0.7f);
				default: return new Color(0.45f, 0.75f, 0.35f);
			}
		}

		static Shader ResolveLitShader()
		{
			var shader = Shader.Find("Universal Render Pipeline/Lit");
			if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null) shader = Shader.Find("Standard");
			if (shader == null) shader = Shader.Find("Unlit/Color");
			return shader;
		}
	}
}