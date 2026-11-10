using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家武器系统：步枪 / 霰弹枪，R 换弹，右键投掷手雷。
/// 复用原工程的弹丸预制体与枪口特效，接管 PlayerProjectileSpawner 的开火逻辑
/// （该组件会被禁用，只作为弹丸 / 特效 / 音源的引用来源）。
/// </summary>
public class WeaponSystem : MonoBehaviour
{
	public enum WeaponMode { Rifle = 0, Shotgun = 1 }

	[Header("Shared References")]
	public GameObject projectilePrefab;
	public Transform spawnPoint;
	public ParticleSystem muzzleParticles;
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

	[Header("Grenade")]
	public int grenadeCharges = 3;
	public int maxGrenadeCharges = 5;
	public float grenadeCooldown = 1.5f;
	public float grenadeThrowForce = 14f;

	public WeaponMode Mode { get; private set; }
	public int RifleAmmo { get; private set; }
	public int ShotgunAmmo { get; private set; }
	public int Grenades { get; private set; }
	public bool Reloading { get; private set; }
	public string ModeName { get { return Mode == WeaponMode.Rifle ? "RIFLE" : "SHOTGUN"; } }
	public int CurrentMag { get { return Mode == WeaponMode.Rifle ? RifleAmmo : ShotgunAmmo; } }
	public int CurrentMagSize { get { return Mode == WeaponMode.Rifle ? rifleMagSize : shotgunMagSize; } }

	/// <summary>弹药、模式、手雷数量变化时触发，HUD 据此刷新。</summary>
	public event Action AmmoChanged;

	PlayerHealth playerHealth;
	float nextFireTime;
	float nextGrenadeTime;

	void Awake()
	{
		RifleAmmo = rifleMagSize;
		ShotgunAmmo = shotgunMagSize;
		Grenades = grenadeCharges;
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
	}

	void Start()
	{
		RaiseAmmoChanged();
	}

	void Update()
	{
		if (playerHealth != null && playerHealth.IsDead) return;

		if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(WeaponMode.Rifle);
		if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(WeaponMode.Shotgun);

		if (Input.GetKeyDown(KeyCode.R)) StartReload();

		if (Input.GetMouseButton(0)) TryFire(false);
		if (Input.GetMouseButtonDown(1)) ThrowGrenade();
	}

	public void SwitchTo(WeaponMode mode)
	{
		if (Mode == mode) return;
		Mode = mode;
		Reloading = false;
		RaiseAmmoChanged();
	}

	public void TryFire(bool force)
	{
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
		else
		{
			ShotgunAmmo--;
			nextFireTime = Time.time + shotgunInterval;
			SpawnProjectile(shotgunDamage, shotgunSpread, shotgunPellets);
		}

		if (muzzleParticles != null) muzzleParticles.Play();
		if (fireAudio != null) fireAudio.Play();
		RaiseAmmoChanged();

		if (CurrentMag <= 0) StartReload();
	}

	public void StartReload()
	{
		if (Reloading) return;

		int magSize = CurrentMagSize;
		bool full = Mode == WeaponMode.Rifle ? RifleAmmo >= magSize : ShotgunAmmo >= magSize;
		if (full) return;

		StartCoroutine(ReloadRoutine());
	}

	IEnumerator ReloadRoutine()
	{
		Reloading = true;
		RaiseAmmoChanged();
		yield return new WaitForSeconds(Mode == WeaponMode.Rifle ? rifleReloadTime : shotgunReloadTime);

		if (Mode == WeaponMode.Rifle) RifleAmmo = rifleMagSize;
		else ShotgunAmmo = shotgunMagSize;

		Reloading = false;
		RaiseAmmoChanged();
	}

	public void ThrowGrenade()
	{
		if (Grenades <= 0 || grenadePrefab == null) return;
		if (Time.time < nextGrenadeTime) return;

		nextGrenadeTime = Time.time + grenadeCooldown;
		Grenades--;
		RaiseAmmoChanged();

		Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position + Vector3.up * 1.2f;
		Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
		var grenade = Instantiate(grenadePrefab, position, rotation);
		var body = grenade.GetComponent<Rigidbody>();
		if (body != null)
			body.velocity = (rotation * Vector3.forward) * grenadeThrowForce + Vector3.up * 4f;
	}

	public void AddGrenades(int amount)
	{
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

			var projectile = Instantiate(projectilePrefab, spawnPoint.position, rotation);
			var behaviour = projectile.GetComponent<ProjectileBehaviour>();
			if (behaviour != null) behaviour.damageToEnemy = damage;
		}
	}

	void RaiseAmmoChanged()
	{
		if (AmmoChanged != null) AmmoChanged();
	}
}