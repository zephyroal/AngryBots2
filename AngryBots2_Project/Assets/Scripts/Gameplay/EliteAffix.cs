using UnityEngine;
using UnityEngine.AI;

/// <summary>精英敌人词缀。</summary>
public enum EliteAffix
{
	None = 0,

	/// <summary>自爆：死亡时在自身位置爆炸，对范围内玩家造成伤害。</summary>
	Explosive = 1,

	/// <summary>迅捷：移动速度额外提升。</summary>
	Swift = 2,

	/// <summary>铁壁：额外生命上限。</summary>
	Tank = 3,
}

/// <summary>
/// 精英词缀宿主：由 <see cref="ABGameplay.Enemies.WaveDirectorEx"/> 在生成精英时挂上，
/// 负责一次性属性修正与死亡时的自爆效果。
/// </summary>
[DisallowMultipleComponent]
public sealed class EliteAffixHost : MonoBehaviour
{
	[Header("Affix")]
	public EliteAffix affix = EliteAffix.Explosive;

	[Header("Explosive")]
	[Tooltip("爆炸半径（米）。")]
	public float explosionRadius = 3.5f;

	[Tooltip("爆心伤害（边缘衰减到 35%）。")]
	public int explosionDamage = 22;

	[Tooltip("爆炸特效（可选）。")]
	public GameObject explosionPrefab;

	[Header("Swift")]
	[Tooltip("迅捷词缀的额外移速倍率。")]
	public float swiftSpeedScale = 1.3f;

	[Header("Tank")]
	[Tooltip("铁壁词缀的额外生命倍率。")]
	public float tankHealthScale = 1.5f;

	/// <summary>词缀权重：40% 自爆 / 30% 迅捷 / 30% 铁壁。</summary>
	public static EliteAffix PickAffix(float roll01)
	{
		float roll = Mathf.Clamp01(roll01);
		if (roll < 0.4f) return EliteAffix.Explosive;
		if (roll < 0.7f) return EliteAffix.Swift;
		return EliteAffix.Tank;
	}

	public static string Label(EliteAffix affix)
	{
		switch (affix)
		{
			case EliteAffix.Explosive: return "Explosive";
			case EliteAffix.Swift: return "Swift";
			case EliteAffix.Tank: return "Tank";
			default: return "None";
		}
	}

	/// <summary>给目标挂上词缀（幂等），并立即应用属性修正。</summary>
	public static EliteAffixHost Attach(GameObject target, EliteAffix affix)
	{
		if (target == null) return null;

		var host = target.GetComponent<EliteAffixHost>();
		if (host == null) host = target.AddComponent<EliteAffixHost>();
		host.affix = affix;
		host.Apply();
		return host;
	}

	/// <summary>按距离衰减的爆炸伤害（超出半径返回 0），纯函数便于自检。</summary>
	public static int ExplosionDamage(int baseDamage, float distance, float radius)
	{
		if (baseDamage <= 0) return 0;
		if (radius <= 0f) return baseDamage;
		if (distance > radius) return 0;

		float falloff = Mathf.Clamp01(1f - distance / radius);
		return Mathf.Max(1, Mathf.RoundToInt(baseDamage * (0.35f + 0.65f * falloff)));
	}

	void OnEnable()
	{
		EnemyRegistry.EnemyUnregistered += OnEnemyUnregistered;
	}

	void OnDisable()
	{
		EnemyRegistry.EnemyUnregistered -= OnEnemyUnregistered;
	}

	/// <summary>应用一次性的属性修正（重复调用不会叠加，因为倍率基于当前值只加一次）。</summary>
	public void Apply()
	{
		if (applied) return;
		applied = true;

		switch (affix)
		{
			case EliteAffix.Swift:
				var nav = GetComponent<NavMeshAgent>();
				if (nav != null) nav.speed *= swiftSpeedScale;
				break;

			case EliteAffix.Tank:
				var health = GetComponent<EnemyHealthEx>();
				if (health != null) health.ResetHealth(Mathf.RoundToInt(health.MaxHealth * tankHealthScale));
				break;
		}

		if (affix != EliteAffix.None) gameObject.name = gameObject.name + "_" + Label(affix);
	}

	bool applied;

	void OnEnemyUnregistered(EnemyAgent dead)
	{
		if (affix != EliteAffix.Explosive) return;
		if (dead == null || dead.gameObject != gameObject) return;
		Explode();
	}

	void Explode()
	{
		Vector3 center = transform.position + Vector3.up * 0.6f;

		if (explosionPrefab != null)
			VfxPool.Spawn(explosionPrefab, center, Quaternion.identity);

		var player = Object.FindObjectOfType<PlayerHealth>();
		if (player == null || player.IsDead) return;

		float distance = Vector3.Distance(player.transform.position, center);
		int damage = ExplosionDamage(explosionDamage, distance, explosionRadius);
		if (damage > 0) player.TakeDamage(damage);
	}
}
