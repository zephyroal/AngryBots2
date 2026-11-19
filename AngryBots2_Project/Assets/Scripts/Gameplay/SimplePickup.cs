using System.Collections;
using UnityEngine;

/// <summary>
/// 通用拾取物：血包 / 弹药包 / 手雷。玩家碰到触发器时：
/// <list type="bullet">
/// <item><see cref="PickupType.Health"/> → <c>PlayerHealth.Heal</c></item>
/// <item><see cref="PickupType.Ammo"/> → <c>WeaponSystem.AddAmmo</c>（旧系统没有备弹概念则补满当前弹匣）</item>
/// <item><see cref="PickupType.Grenade"/> → <c>WeaponSystem.AddGrenades</c></item>
/// <item><see cref="PickupType.Score"/> → <see cref="GameEvents.RaiseScoreChanged"/></item>
/// </list>
/// <para>
/// 提供静态工厂 <see cref="CreateHealthPack"/> / <see cref="CreateAmmoPack"/> / <see cref="CreateGrenadePack"/>，
/// 方便 batchmode 自检与关卡脚本在没有任何美术资源的情况下生成可玩的拾取物。
/// </para>
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class SimplePickup : MonoBehaviour
{
	/// <summary>拾取物类型。</summary>
	public enum PickupType
	{
		Health = 0,
		Ammo = 1,
		Grenade = 2,
		Score = 3,
		PowerUp = 4,
	}

	[Header("Pickup")]
	public PickupType type = PickupType.Health;

	[Tooltip("PowerUp 类型拾取时授予的增益种类。")]
	public PowerUpKind powerUpKind = PowerUpKind.DamageBoost;

	[Tooltip("拾取量。Health 为血量，Ammo 为步枪/霰弹枪各补的弹药，Grenade 为手雷个数。")]
	public int amount = 30;

	[Tooltip("重生时间（秒）。<= 0 表示拾取后销毁。")]
	public float respawnTime = 20f;

	[Tooltip("是否要求玩家满血/满弹时不消耗（Health 满血、Ammo 满弹）。")]
	public bool ignoreWhenFull = true;

	[Header("Presentation")]
	[Tooltip("上下浮动的幅度（米）。")]
	public float bobAmplitude = 0.15f;

	[Tooltip("旋转速度（度/秒）。")]
	public float spinSpeed = 90f;

	[Tooltip("拾取音效。")]
	public AudioClip pickupClip;

	/// <summary>已经被拾取、等待重生。</summary>
	public bool Consumed { get; private set; }

	/// <summary>累计被拾取次数（自检 / 统计用）。</summary>
	public int PickupCount { get; private set; }

	SphereCollider trigger;
	Renderer[] renderers;
	Vector3 basePosition;
	Coroutine respawnRoutine;

	void Awake()
	{
		trigger = GetComponent<SphereCollider>();
		trigger.isTrigger = true;
		if (trigger.radius < 0.6f) trigger.radius = 0.6f;

		renderers = GetComponentsInChildren<Renderer>(true);
		basePosition = transform.position;
	}

	void Update()
	{
		if (Consumed) return;

		if (bobAmplitude > 0f)
		{
			float y = Mathf.Sin(Time.time * 2f) * bobAmplitude;
			var position = transform.position;
			position.y = basePosition.y + y;
			transform.position = position;
		}

		if (spinSpeed != 0f) transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
	}

	void OnTriggerEnter(Collider other)
	{
		if (Consumed || other == null) return;

		var player = other.GetComponentInParent<PlayerHealth>();
		if (player == null) return;
		if (player.IsDead) return;

		TryCollect(player);
	}

	/// <summary>尝试让指定玩家拾取，成功返回 true（供自检直接调用）。</summary>
	public bool TryCollect(PlayerHealth player)
	{
		if (Consumed || player == null || player.IsDead) return false;

		bool applied = false;

		switch (type)
		{
			case PickupType.Health:
				if (!ignoreWhenFull || player.CurrentHealth < player.MaxHealth)
				{
					int before = player.CurrentHealth;
					Heal(player, amount);
					applied = player.CurrentHealth > before || !ignoreWhenFull;
				}
				break;

			case PickupType.Ammo:
				applied = AddAmmo(player, amount);
				break;

			case PickupType.Grenade:
				applied = AddGrenades(player, amount);
				break;

			case PickupType.Score:
				GameEvents.RaiseScoreChanged(amount, amount);
				applied = true;
				break;

			case PickupType.PowerUp:
				var powerUps = PowerUpSystem.Ensure(player.gameObject);
				if (powerUps != null)
				{
					powerUps.Activate(powerUpKind);
					applied = true;
				}
				break;
		}

		if (!applied) return false;

		PickupCount++;
		GameEvents.RaisePickupCollected(type == PickupType.PowerUp ? powerUpKind.ToString() : type.ToString(), amount);
		if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position);

		if (respawnTime > 0f) StartRespawn();
		else Destroy(gameObject);

		return true;
	}

	void StartRespawn()
	{
		Consumed = true;
		SetVisible(false);
		if (respawnRoutine != null) StopCoroutine(respawnRoutine);
		respawnRoutine = StartCoroutine(RespawnAfterDelay());
	}

	IEnumerator RespawnAfterDelay()
	{
		yield return new WaitForSeconds(respawnTime);
		Consumed = false;
		SetVisible(true);
		respawnRoutine = null;
	}

	void SetVisible(bool visible)
	{
		if (trigger != null) trigger.enabled = visible;
		if (renderers != null)
		{
			for (int i = 0; i < renderers.Length; i++)
				if (renderers[i] != null) renderers[i].enabled = visible;
		}
	}

	// ─── 对不同血量实现的兼容 ───
	// 场景里可能挂的是 PlayerHealth、PlayerHealthAdapter（子类），
	// 或未来别的实现；这里按最具体的接口优先调用。

	static void Heal(PlayerHealth player, int amount)
	{
		var adapter = player as PlayerHealthAdapter;
		if (adapter != null) adapter.HealEx(amount);
		else player.Heal(amount);
	}

	static bool AddAmmo(PlayerHealth player, int amount)
	{
		var weapons = player.GetComponent<WeaponSystem>();
		if (weapons == null) return false;

		// 优先补备弹（换弹消耗），适配器再顺手补弹匣，保证捡到就有子弹可用。
		int added = weapons.AddReserve(amount);
		var adapter = player.GetComponent<WeaponSystemAdapter>();
		if (adapter != null) added += adapter.AddAmmo(amount);

		return added > 0;
	}

	static bool AddGrenades(PlayerHealth player, int amount)
	{
		var weapons = player.GetComponent<WeaponSystem>();
		if (weapons == null) return false;
		if (weapons.Grenades >= weapons.maxGrenadeCharges) return false;

		weapons.AddGrenades(amount);
		return true;
	}

	// ─── 静态工厂：一行生成可玩的拾取物 ───

	/// <summary>生成一个血包。</summary>
	public static SimplePickup CreateHealthPack(Vector3 position, int amount = 30, float respawn = 20f)
	{
		return Create(position, PickupType.Health, amount, respawn, new Color(0.2f, 0.9f, 0.35f));
	}

	/// <summary>生成一个弹药包。</summary>
	public static SimplePickup CreateAmmoPack(Vector3 position, int amount = 15, float respawn = 20f)
	{
		return Create(position, PickupType.Ammo, amount, respawn, new Color(0.95f, 0.8f, 0.25f));
	}

	/// <summary>生成一个手雷补给。</summary>
	public static SimplePickup CreateGrenadePack(Vector3 position, int amount = 1, float respawn = 25f)
	{
		return Create(position, PickupType.Grenade, amount, respawn, new Color(0.4f, 0.7f, 1f));
	}

	/// <summary>生成一个临时增益拾取物（默认一次性，拾取后销毁）。</summary>
	public static SimplePickup CreatePowerUp(PowerUpKind kind, Vector3 position, float respawn = 0f)
	{
		var pickup = Create(position, PickupType.PowerUp, 0, respawn, ColorFor(kind));
		pickup.powerUpKind = kind;
		pickup.name = "Pickup_PowerUp_" + kind;
		return pickup;
	}

	static Color ColorFor(PowerUpKind kind)
	{
		switch (kind)
		{
			case PowerUpKind.RapidFire: return new Color(1f, 0.55f, 0.15f);
			case PowerUpKind.Shield: return new Color(0.35f, 0.8f, 1f);
			default: return new Color(1f, 0.25f, 0.45f);
		}
	}

	static SimplePickup Create(Vector3 position, PickupType type, int amount, float respawn, Color color)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = "Pickup_" + type;
		go.transform.position = position + Vector3.up * 0.5f;
		go.transform.localScale = Vector3.one * 0.45f;

		var collider = go.GetComponent<BoxCollider>();
		if (collider != null) collider.isTrigger = true;

		var sphere = go.AddComponent<SphereCollider>();
		sphere.isTrigger = true;
		sphere.radius = 0.9f;

		var renderer = go.GetComponent<Renderer>();
		if (renderer != null)
		{
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null) shader = Shader.Find("Unlit/Color");
			if (shader != null)
			{
				var material = new Material(shader);
				material.name = "Pickup_" + type + "_Material";
				if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
				material.color = color;
				renderer.sharedMaterial = material;
			}
		}

		var pickup = go.AddComponent<SimplePickup>();
		pickup.type = type;
		pickup.amount = amount;
		pickup.respawnTime = respawn;
		return pickup;
	}
}