using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家武器系统：步枪 / 霰弹枪，R 换弹，右键投掷手雷。
/// 复用原工程的弹丸预制体与枪口特效，接管 PlayerProjectileSpawner 的开火逻辑
/// （该组件会被禁用，只作为弹丸 / 特效 / 音源的引用来源）。
/// </summary>
public class WeaponSystem : MonoBehaviour
{
	public enum WeaponMode { Rifle = 0, Shotgun = 1, SMG = 2, Sniper = 3, Launcher = 4 }

	[Header("Shared References")]
	public GameObject projectilePrefab;
	public Transform spawnPoint;
	public ParticleSystem muzzleParticles;
	[Tooltip("枪口粒子的根节点。可直接指向嵌套预制体暴露出来的根 Transform；为空时按名称自动查找。")]
	public Transform muzzleFlashRoot;
	public AudioSource fireAudio;
	public GameObject grenadePrefab;

	[Header("Rifle")]
	public float rifleInterval = 0.12f;
	public int rifleMagSize = 30;
	public float rifleReloadTime = 1.3f;
	public int rifleDamage = 25;
	public float rifleSpread = 0.7f;

	[Header("Shotgun")]
	public float shotgunInterval = 0.85f;
	public int shotgunMagSize = 8;
	public float shotgunReloadTime = 2f;
	public int shotgunDamage = 12;
	public int shotgunPellets = 8;
	public float shotgunSpread = 7f;

	[Header("SMG")]
	public float smgInterval = 0.07f;
	public int smgMagSize = 40;
	public float smgReloadTime = 1.5f;
	public int smgDamage = 9;
	public float smgSpread = 1.2f;

	[Header("Sniper")]
	public float sniperInterval = 1.1f;
	public int sniperMagSize = 5;
	public float sniperReloadTime = 2.2f;
	public int sniperDamage = 90;
	public float sniperSpread = 0.2f;
	[Tooltip("狙击弹可以额外穿透的敌人数（0 = 命中即停）。")]
	public int sniperPierce = 2;
	[Tooltip("狙击爆头倍率（命中点高于敌人中心一定高度时生效）。")]
	public float sniperCriticalMultiplier = 2f;

	[Header("Launcher")]
	public float launcherInterval = 1.4f;
	public int launcherMagSize = 4;
	public float launcherReloadTime = 2.6f;
	public int launcherDamage = 60;
	public float launcherSpeed = 24f;
	public float launcherSpread = 1f;

	[Header("Grenade")]
	public int grenadeCharges = 3;
	public int maxGrenadeCharges = 5;
	public float grenadeCooldown = 1.5f;
	public float grenadeThrowForce = 14f;

	public WeaponMode Mode { get; private set; }
	public int RifleAmmo { get; private set; }
	public int ShotgunAmmo { get; private set; }
	public int SmgAmmo { get; private set; }
	public int SniperAmmo { get; private set; }
	public int LauncherAmmo { get; private set; }
	public int Grenades { get; private set; }
	public bool Reloading { get; private set; }
	public string ModeName
	{
		get
		{
			switch (Mode)
			{
				case WeaponMode.Rifle: return "RIFLE";
				case WeaponMode.Shotgun: return "SHOTGUN";
				case WeaponMode.Sniper: return "SNIPER";
				case WeaponMode.Launcher: return "LAUNCHER";
				default: return "SMG";
			}
		}
	}
	public int CurrentMag
	{
		get
		{
			EnsureAmmoInitialized();
			switch (Mode)
			{
				case WeaponMode.Rifle: return RifleAmmo;
				case WeaponMode.Shotgun: return ShotgunAmmo;
				case WeaponMode.Sniper: return SniperAmmo;
				case WeaponMode.Launcher: return LauncherAmmo;
				default: return SmgAmmo;
			}
		}
	}
	public int CurrentMagSize
	{
		get
		{
			switch (Mode)
			{
				case WeaponMode.Rifle: return rifleMagSize;
				case WeaponMode.Shotgun: return shotgunMagSize;
				case WeaponMode.Sniper: return sniperMagSize;
				case WeaponMode.Launcher: return launcherMagSize;
				default: return smgMagSize;
			}
		}
	}

	/// <summary>弹药、模式、手雷数量变化时触发，HUD 据此刷新。</summary>
	public event Action AmmoChanged;

	/// <summary>备弹（弹匣之外）。换弹从备弹扣除。</summary>
	public int RifleReserve { get; private set; }
	public int ShotgunReserve { get; private set; }
	public int SmgReserve { get; private set; }
	public int SniperReserve { get; private set; }
	public int LauncherReserve { get; private set; }

	/// <summary>当前武器备弹。</summary>
	public int CurrentReserve
	{
		get
		{
			EnsureAmmoInitialized();
			switch (Mode)
			{
				case WeaponMode.Rifle: return RifleReserve;
				case WeaponMode.Shotgun: return ShotgunReserve;
				case WeaponMode.Sniper: return SniperReserve;
				case WeaponMode.Launcher: return LauncherReserve;
				default: return SmgReserve;
			}
		}
	}

	/// <summary>补充当前武器备弹，返回实际补充数量。</summary>
	public int AddReserve(int amount)
	{
		EnsureAmmoInitialized();
		if (amount <= 0) return 0;

		int capacity = CurrentMagSize * 6;
		int before = CurrentReserve;
		int after = Mathf.Clamp(before + amount, 0, capacity);
		int added = after - before;
		if (added <= 0) return 0;

		switch (Mode)
		{
			case WeaponMode.Rifle: RifleReserve = after; break;
			case WeaponMode.Shotgun: ShotgunReserve = after; break;
			case WeaponMode.Sniper: SniperReserve = after; break;
			case WeaponMode.Launcher: LauncherReserve = after; break;
			default: SmgReserve = after; break;
		}

		RaiseAmmoChanged();
		return added;
	}

	PlayerHealth playerHealth;
	float nextFireTime;
	float nextGrenadeTime;

	void Awake()
	{
		EnsureAmmoInitialized();
		playerHealth = GetComponent<PlayerHealth>();

		// 预制体里的引用若没接上，回退到原 PlayerProjectileSpawner（枪械子物体）。
		if (projectilePrefab == null || spawnPoint == null)
		{
			var legacy = GetComponentInChildren<PlayerProjectileSpawner>(true);
			if (legacy != null)
			{
				if (projectilePrefab == null) projectilePrefab = legacy.projectilePrefab;
				if (spawnPoint == null) spawnPoint = legacy.spawnPoint;
				if (muzzleParticles == null) muzzleParticles = legacy.spawnParticles;
				if (fireAudio == null) fireAudio = legacy.spawnAudioSource;
			}
		}

		if (spawnPoint == null)
		{
			foreach (var t in GetComponentsInChildren<Transform>(true))
			{
				if (t.name.Contains("SpawnPoint")) { spawnPoint = t; break; }
			}
		}

		ResolveMuzzleFlash();
	}

	// ------------------------------------------------------------------
	// 枪口特效
	// ------------------------------------------------------------------

	ParticleSystem[] muzzleFlashSystems;
	bool muzzleFlashLookupDone;

	/// <summary>当前解析到的枪口粒子系统数量（供自检 / 调试使用）。</summary>
	public int MuzzleFlashSystemCount
	{
		get { EnsureMuzzleFlashResolved(); return muzzleFlashSystems != null ? muzzleFlashSystems.Length : 0; }
	}

	/// <summary>枪口粒子根节点（解析成功后回填，便于 Inspector 里看到）。</summary>
	public Transform ResolvedMuzzleFlashRoot { get { EnsureMuzzleFlashResolved(); return muzzleFlashRoot; } }

	void ResolveMuzzleFlash()
	{
		if (muzzleFlashSystems != null && muzzleFlashSystems.Length > 0) return;

		// 直接引用优先；没有就按名字找，兼容「枪口粒子是嵌套预制体、内部组件无法被外部资产引用」的情况。
		if (muzzleFlashRoot == null) muzzleFlashRoot = FindMuzzleFlashRoot();

		var systems = new List<ParticleSystem>();
		if (muzzleParticles != null) systems.Add(muzzleParticles);
		if (muzzleFlashRoot != null)
		{
			foreach (var ps in muzzleFlashRoot.GetComponentsInChildren<ParticleSystem>(true))
			{
				if (ps != null && !systems.Contains(ps)) systems.Add(ps);
			}
		}

		if (systems.Count > 0)
		{
			muzzleFlashSystems = systems.ToArray();
			muzzleFlashLookupDone = true;
		}
		else
		{
			muzzleFlashLookupDone = true;
			Debug.LogWarning("[WeaponSystem] 没找到枪口粒子系统（muzzleParticles / muzzleFlashRoot 都为空）", this);
		}
	}

	void EnsureMuzzleFlashResolved()
	{
		if (muzzleFlashSystems != null && muzzleFlashSystems.Length > 0) return;
		if (muzzleFlashLookupDone) return;
		ResolveMuzzleFlash();
	}

	Transform FindMuzzleFlashRoot()
	{
		Transform best = null;
		int bestDepth = int.MaxValue;

		foreach (var t in GetComponentsInChildren<Transform>(true))
		{
			if (t == null || t == transform) continue;
			if (t.name.IndexOf("muzzleflash", StringComparison.OrdinalIgnoreCase) < 0) continue;
			if (t.GetComponentInChildren<ParticleSystem>(true) == null) continue;

			int depth = 0;
			for (var p = t.parent; p != null && p != transform; p = p.parent) depth++;
			if (best == null || depth < bestDepth)
			{
				best = t;
				bestDepth = depth;
			}
		}

		return best;
	}

	/// <summary>按武器放大/缩小枪口特效（狙击更炸、SMG 更小），让每把枪的开火手感不同。</summary>
	public float MuzzleFlashScale(WeaponMode mode)
	{
		switch (mode)
		{
			case WeaponMode.Shotgun: return 1.25f;
			case WeaponMode.SMG: return 0.85f;
			case WeaponMode.Sniper: return 1.6f;
			case WeaponMode.Launcher: return 1.8f;
			default: return 1f;
		}
	}

	/// <summary>按武器放大命中特效（狙击的冲击更大）。</summary>
	public float ImpactScale(WeaponMode mode)
	{
		switch (mode)
		{
			case WeaponMode.Shotgun: return 1.15f;
			case WeaponMode.SMG: return 0.85f;
			case WeaponMode.Sniper: return 1.5f;
			default: return 1f;
		}
	}

	/// <summary>开火镜头抖动的强度（重武器更强，SMG 最轻）。</summary>
	public float ShakeStrength(WeaponMode mode)
	{
		switch (mode)
		{
			case WeaponMode.Shotgun: return 0.35f;
			case WeaponMode.SMG: return 0.12f;
			case WeaponMode.Sniper: return 0.55f;
			case WeaponMode.Launcher: return 0.6f;
			default: return 0.18f;
		}
	}

	/// <summary>开火镜头抖动的时长（秒）。</summary>
	public float ShakeDuration(WeaponMode mode)
	{
		switch (mode)
		{
			case WeaponMode.Sniper:
			case WeaponMode.Launcher: return 0.18f;
			default: return 0.1f;
		}
	}

	/// <summary>
	/// 逐发重播枪口特效。粒子系统已经在播放时 <c>Play()</c> 是空操作，
	/// 连发（SMG 0.07s）会因此漏闪，所以每次先清空再播放。
	/// </summary>
	public void PlayMuzzleFlash()
	{
		EnsureMuzzleFlashResolved();
		if (muzzleFlashSystems == null) return;

		for (int i = 0; i < muzzleFlashSystems.Length; i++)
		{
			var ps = muzzleFlashSystems[i];
			if (ps == null) continue;
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			ps.Play();
		}

		// 按武器缩放整组枪口特效（狙击/榴弹更大，SMG 更小）
		if (muzzleFlashRoot != null)
			muzzleFlashRoot.localScale = Vector3.one * MuzzleFlashScale(Mode);
	}

	bool ammoInitialized;

	/// <summary>弹药 / 手雷惰性初始化：Edit 模式自检或运行时动态挂载时也不会是 0。</summary>
	void EnsureAmmoInitialized()
	{
		if (ammoInitialized) return;
		ammoInitialized = true;
		RifleAmmo = rifleMagSize;
		ShotgunAmmo = shotgunMagSize;
		SmgAmmo = smgMagSize;
		SniperAmmo = sniperMagSize;
		LauncherAmmo = launcherMagSize;
		RifleReserve = rifleMagSize * 4;
		ShotgunReserve = shotgunMagSize * 4;
		SmgReserve = smgMagSize * 4;
		SniperReserve = sniperMagSize * 4;
		LauncherReserve = launcherMagSize * 4;
		Grenades = grenadeCharges;
	}

	void Start()
	{
		// 运行时升级成 WeaponSystemAdapter 时，字段是在 Awake 之后才拷贝进来的，这里补一次解析。
		if (muzzleFlashSystems == null || muzzleFlashSystems.Length == 0)
		{
			muzzleFlashLookupDone = false;
			ResolveMuzzleFlash();
		}

		RaiseAmmoChanged();
	}

	void Update()
	{
		if (playerHealth != null && playerHealth.IsDead) return;

		if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(WeaponMode.Rifle);
		if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(WeaponMode.Shotgun);
		if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(WeaponMode.SMG);
		if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchTo(WeaponMode.Sniper);
		if (Input.GetKeyDown(KeyCode.Alpha5)) SwitchTo(WeaponMode.Launcher);

		if (Input.GetKeyDown(KeyCode.R)) StartReload();

		if (Input.GetMouseButton(0)) TryFire(false);
		if (Input.GetMouseButtonDown(1)) ThrowGrenade();
	}

	public void SwitchTo(WeaponMode mode)
	{
		EnsureAmmoInitialized();
		if (Mode == mode) return;
		Mode = mode;
		Reloading = false;
		RaiseAmmoChanged();
	}

	public void TryFire(bool force)
	{
		EnsureAmmoInitialized();
		if (Reloading || Time.time < nextFireTime) return;

		int magSize = CurrentMagSize;
		if (CurrentMag <= 0)
		{
			StartReload();
			return;
		}

		float rateScale = RateScale();

		if (Mode == WeaponMode.Rifle)
		{
			RifleAmmo--;
			nextFireTime = Time.time + rifleInterval * rateScale;
			SpawnProjectile(ScaledDamage(rifleDamage), rifleSpread, 1);
		}
		else if (Mode == WeaponMode.Shotgun)
		{
			ShotgunAmmo--;
			nextFireTime = Time.time + shotgunInterval * rateScale;
			SpawnProjectile(ScaledDamage(shotgunDamage), shotgunSpread, shotgunPellets);
		}
		else if (Mode == WeaponMode.Sniper)
		{
			SniperAmmo--;
			nextFireTime = Time.time + sniperInterval * rateScale;
			SpawnProjectile(ScaledDamage(sniperDamage), sniperSpread, 1, sniperPierce, sniperCriticalMultiplier);
		}
		else if (Mode == WeaponMode.Launcher)
		{
			LauncherAmmo--;
			nextFireTime = Time.time + launcherInterval * rateScale;
			SpawnLauncherGrenade(ScaledDamage(launcherDamage), launcherSpread);
		}
		else
		{
			SmgAmmo--;
			nextFireTime = Time.time + smgInterval * rateScale;
			SpawnProjectile(ScaledDamage(smgDamage), smgSpread, 1);
		}

		PlayMuzzleFlash();
		CameraShake.Shake(ShakeStrength(Mode), ShakeDuration(Mode));
		if (fireAudio != null) fireAudio.Play();
		RaiseAmmoChanged();

		if (CurrentMag <= 0) StartReload();
	}

	// ── 临时增益（PowerUp）接入：未挂 PowerUpSystem 时倍率恒为 1 ──
	PowerUpSystem powerUps;

	PowerUpSystem ResolvePowerUps()
	{
		if (powerUps == null) powerUps = GetComponent<PowerUpSystem>();
		return powerUps;
	}

	int ScaledDamage(int baseDamage)
	{
		var system = ResolvePowerUps();
		if (system == null) return baseDamage;
		return Mathf.Max(1, Mathf.RoundToInt(baseDamage * system.DamageMultiplier));
	}

	float RateScale()
	{
		var system = ResolvePowerUps();
		return system != null ? system.FireIntervalMultiplier : 1f;
	}

	public void StartReload()
	{
		EnsureAmmoInitialized();
		if (Reloading) return;

		int magSize = CurrentMagSize;
		bool full = CurrentMag >= magSize;
		if (full) return;

		StartCoroutine(ReloadRoutine());
	}

	IEnumerator ReloadRoutine()
	{
		Reloading = true;
		RaiseAmmoChanged();

		// 换弹开始时就锁定要装填的数量（避免换弹过程中切枪导致数量错乱）。
		int needed = Mathf.Max(0, CurrentMagSize - CurrentMag);
		int take = Mathf.Min(needed, CurrentReserve);

		float reloadTime = Mode == WeaponMode.Rifle ? rifleReloadTime
			: (Mode == WeaponMode.Shotgun ? shotgunReloadTime
				: (Mode == WeaponMode.Sniper ? sniperReloadTime
					: (Mode == WeaponMode.Launcher ? launcherReloadTime : smgReloadTime)));
		yield return new WaitForSeconds(reloadTime);

		if (take > 0)
		{
			if (Mode == WeaponMode.Rifle) { RifleAmmo += take; RifleReserve -= take; }
			else if (Mode == WeaponMode.Shotgun) { ShotgunAmmo += take; ShotgunReserve -= take; }
			else if (Mode == WeaponMode.Sniper) { SniperAmmo += take; SniperReserve -= take; }
			else if (Mode == WeaponMode.Launcher) { LauncherAmmo += take; LauncherReserve -= take; }
			else { SmgAmmo += take; SmgReserve -= take; }
		}

		Reloading = false;
		RaiseAmmoChanged();
	}

	public void ThrowGrenade()
	{
		EnsureAmmoInitialized();
		if (Grenades <= 0 || grenadePrefab == null) return;
		if (Time.time < nextGrenadeTime) return;

		nextGrenadeTime = Time.time + grenadeCooldown;
		Grenades--;
		RaiseAmmoChanged();

		Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position + Vector3.up * 1.2f;
		Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
		var grenade = PoolManager.Spawn(grenadePrefab, position, rotation);
		if (grenade == null) return;
		var body = grenade.GetComponent<Rigidbody>();
		if (body != null)
			body.velocity = (rotation * Vector3.forward) * grenadeThrowForce + Vector3.up * 4f;
	}

	public void AddGrenades(int amount)
	{
		EnsureAmmoInitialized();
		Grenades = Mathf.Clamp(Grenades + amount, 0, maxGrenadeCharges);
		RaiseAmmoChanged();
	}

	void SpawnProjectile(int damage, float spread, int count, int pierce = 0, float criticalMultiplier = 1f)
	{
		if (projectilePrefab == null || spawnPoint == null) return;

		for (int i = 0; i < count; i++)
		{
			Quaternion rotation = spawnPoint.rotation * Quaternion.Euler(
				UnityEngine.Random.Range(-spread, spread),
				UnityEngine.Random.Range(-spread, spread),
				0f);

			var projectile = PoolManager.Spawn(projectilePrefab, spawnPoint.position, rotation);
			if (projectile == null) continue;
			var behaviour = projectile.GetComponent<ProjectileBehaviour>();
			if (behaviour != null)
			{
				behaviour.damageToEnemy = damage;
				behaviour.pierceRemaining = Mathf.Max(0, pierce);
				behaviour.criticalMultiplier = Mathf.Max(1f, criticalMultiplier);
				behaviour.impactScale = ImpactScale(Mode);
			}
		}
	}

	/// <summary>榴弹发射器：复用工程里的手雷预制体，给一个向前的初速。</summary>
	void SpawnLauncherGrenade(int damage, float spread)
	{
		if (grenadePrefab == null) return;

		Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position + Vector3.up * 1.2f;
		Quaternion baseRotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
		Quaternion rotation = baseRotation * Quaternion.Euler(
			UnityEngine.Random.Range(-spread, spread),
			UnityEngine.Random.Range(-spread, spread),
			0f);

		var grenade = PoolManager.Spawn(grenadePrefab, position, rotation);
		if (grenade == null) return;

		var behaviour = grenade.GetComponent<GrenadeProjectile>();
		if (behaviour != null) behaviour.damage = damage;

		var body = grenade.GetComponent<Rigidbody>();
		if (body != null) body.velocity = (rotation * Vector3.forward) * launcherSpeed;
	}

	void RaiseAmmoChanged()
	{
		if (AmmoChanged != null) AmmoChanged();
	}
}