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
	public enum WeaponMode { Rifle = 0, Shotgun = 1, SMG = 2 }

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

	[Header("Grenade")]
	public int grenadeCharges = 3;
	public int maxGrenadeCharges = 5;
	public float grenadeCooldown = 1.5f;
	public float grenadeThrowForce = 14f;

	public WeaponMode Mode { get; private set; }
	public int RifleAmmo { get; private set; }
	public int ShotgunAmmo { get; private set; }
	public int SmgAmmo { get; private set; }
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
		RifleReserve = rifleMagSize * 4;
		ShotgunReserve = shotgunMagSize * 4;
		SmgReserve = smgMagSize * 4;
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

		if (Mode == WeaponMode.Rifle)
		{
			RifleAmmo--;
			nextFireTime = Time.time + rifleInterval;
			SpawnProjectile(rifleDamage, rifleSpread, 1);
		}
		else if (Mode == WeaponMode.Shotgun)
		{
			ShotgunAmmo--;
			nextFireTime = Time.time + shotgunInterval;
			SpawnProjectile(shotgunDamage, shotgunSpread, shotgunPellets);
		}
		else
		{
			SmgAmmo--;
			nextFireTime = Time.time + smgInterval;
			SpawnProjectile(smgDamage, smgSpread, 1);
		}

		PlayMuzzleFlash();
		if (fireAudio != null) fireAudio.Play();
		RaiseAmmoChanged();

		if (CurrentMag <= 0) StartReload();
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
			: (Mode == WeaponMode.Shotgun ? shotgunReloadTime : smgReloadTime);
		yield return new WaitForSeconds(reloadTime);

		if (take > 0)
		{
			if (Mode == WeaponMode.Rifle) { RifleAmmo += take; RifleReserve -= take; }
			else if (Mode == WeaponMode.Shotgun) { ShotgunAmmo += take; ShotgunReserve -= take; }
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

	void SpawnProjectile(int damage, float spread, int count)
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
			if (behaviour != null) behaviour.damageToEnemy = damage;
		}
	}

	void RaiseAmmoChanged()
	{
		if (AmmoChanged != null) AmmoChanged();
	}
}