using UnityEngine;

/// <summary>
/// 统一伤害工具：把一次攻击结算给「任意对象」，自动解析 <see cref="IDamageable"/> 链路。
/// <para>
/// 解析顺序：
/// <list type="number">
/// <item>碰撞体自身实现 <see cref="IDamageable"/>（新敌人 / 适配器）；</item>
/// <item>父级 <see cref="EnemyHealthAdapter"/>（旧 EnemyHealth 的适配器）；</item>
/// <item>父级旧 <c>EnemyHealth</c>（原版敌人，未升级）；</item>
/// <item>父级 <see cref="PlayerHealth"/>（玩家）。</item>
/// </list>
/// </para>
/// </summary>
public static class DamageUtility
{
	/// <summary>对单个碰撞体造成伤害，返回是否命中。</summary>
	public static bool Hit(Collider collider, DamageInfo info)
	{
		if (collider == null) return false;
		return Hit(collider.gameObject, info, info.HitPoint);
	}

	/// <summary>对单个对象造成伤害。</summary>
	public static bool Hit(GameObject target, DamageInfo info)
	{
		return Hit(target, info, target != null ? target.transform.position + Vector3.up : Vector3.zero);
	}

	/// <summary>对单个对象造成伤害，并指定飘字位置。</summary>
	public static bool Hit(GameObject target, DamageInfo info, Vector3 hitPoint)
	{
		if (target == null || !info.IsValid) return false;

		// 1) 自身实现 IDamageable
		var direct = target.GetComponent<IDamageable>();
		if (direct != null && !direct.IsDead)
		{
			info.HitPoint = hitPoint;
			float applied = direct.ApplyDamage(info);
			if (applied > 0f) return true;
		}

		// 2) 适配器
		var adapter = target.GetComponentInParent<EnemyHealthAdapter>();
		if (adapter != null && !adapter.IsDead)
		{
			info.HitPoint = hitPoint;
			return adapter.ApplyDamage(info) > 0f;
		}

		// 3) 旧 EnemyHealth（未升级时）
		var legacy = target.GetComponentInParent<EnemyHealth>();
		if (legacy != null && !legacy.IsDead)
		{
			legacy.RemoveHealth(GameplayMath.RoundDamage(info.Amount));
			GameEvents.RaiseEnemyDamaged(legacy.gameObject, info.Amount, info);
			DamageNumberSpawner.SpawnEnemyDamage(hitPoint, info.Amount, info.IsCritical, null);
			return true;
		}

		// 4) 玩家
		var player = target.GetComponentInParent<PlayerHealthAdapter>();
		if (player != null)
		{
			info.HitPoint = hitPoint;
			return player.ApplyDamage(info) > 0f;
		}

		var playerLegacy = target.GetComponentInParent<PlayerHealth>();
		if (playerLegacy != null && !playerLegacy.IsDead)
		{
			playerLegacy.TakeDamage(GameplayMath.RoundDamage(info.Amount));
			GameEvents.RaisePlayerDamaged(playerLegacy.CurrentHealth, playerLegacy.MaxHealth, info);
			return true;
		}

		return false;
	}

	/// <summary>
	/// 球形范围伤害（爆炸 / 震地 / 自爆）。对 <paramref name="layerMask"/> 内的碰撞体去重后逐个结算，
	/// 伤害按距离线性衰减（中心 100%，边缘 <paramref name="edgeFactor"/>）。
	/// </summary>
	public static int Explode(
		Vector3 center,
		float radius,
		float damage,
		GameObject source,
		float edgeFactor = 0.35f,
		int layerMask = ~0,
		bool damagePlayer = false)
	{
		if (radius <= 0f || damage <= 0f) return 0;

		var hits = Physics.OverlapSphere(center, radius, layerMask, QueryTriggerInteraction.Ignore);
		int affected = 0;
		var processed = new System.Collections.Generic.HashSet<int>();

		foreach (var hitCollider in hits)
		{
			if (hitCollider == null) continue;

			var enemy = hitCollider.GetComponentInParent<EnemyHealthAdapter>();
			GameObject target = enemy != null ? enemy.gameObject : null;

			if (target == null && damagePlayer)
			{
				var player = hitCollider.GetComponentInParent<PlayerHealth>();
				if (player != null) target = player.gameObject;
			}

			if (target == null) continue;
			if (!processed.Add(target.GetInstanceID())) continue;

			float distance = Vector3.Distance(center, target.transform.position);
			float t = Mathf.Clamp01(distance / radius);
			float falloff = Mathf.Lerp(1f, edgeFactor, t);

			var info = DamageInfo.Explosion(damage * falloff, source, center, 0f);
			info.Direction = (target.transform.position - center).normalized;
			if (Hit(target, info, target.transform.position + Vector3.up)) affected++;
		}

		return affected;
	}
}