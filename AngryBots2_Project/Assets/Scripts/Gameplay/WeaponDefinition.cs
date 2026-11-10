using UnityEngine;

/// <summary>
/// 武器定义数据（ScriptableObject）：把「一把枪」的全部数值与资源引用集中成资产，
/// 供 P3W 的通用 <c>WeaponControllerEx</c> 读取。旧 <see cref="WeaponSystem"/> 的字段保持不动，
/// 本资产只作为新系统的数据源，二者可并存。
/// </summary>
[CreateAssetMenu(fileName = "Weapon_", menuName = "ABGameplay/Weapon Definition", order = 10)]
public sealed class WeaponDefinition : ScriptableObject
{
	[Header("Identity")]
	[Tooltip("显示名（HUD 用）。")]
	public string displayName = "Rifle";

	[Tooltip("武器种类标识，用于切换与存档。")]
	public WeaponArchetype archetype = WeaponArchetype.Rifle;

	[Tooltip("武器图标（可选）。")]
	public Sprite icon;

	[Tooltip("武器模型预制体（可选）。")]
	public GameObject modelPrefab;

	[Header("Ballistics")]
	[Tooltip("每次射击的基础伤害（未计部位倍率）。")]
	public float damage = 25f;

	[Tooltip("射速（发/秒）。")]
	public float fireRate = 8.5f;

	[Tooltip("单次开火的弹丸数（霰弹枪 > 1）。")]
	public int pelletsPerShot = 1;

	[Tooltip("散布角度（度）。")]
	public float spreadDegrees = 0.7f;

	[Tooltip("散布随连续射击增长的角度（度/发）。")]
	public float spreadGrowthPerShot = 0.05f;

	[Tooltip("散布回落速度（度/秒）。")]
	public float spreadRecovery = 6f;

	[Tooltip("弹丸速度（米/秒）。")]
	public float muzzleVelocity = 90f;

	[Tooltip("射程（米），命中判定用。")]
	public float range = 120f;

	[Tooltip("穿透目标数（0 = 不穿透）。")]
	public int penetration = 0;

	[Header("Ammo")]
	[Tooltip("弹匣容量。")]
	public int magazineSize = 30;

	[Tooltip("备弹上限（0 = 无限备弹）。")]
	public int reserveAmmo = 180;

	[Tooltip("换弹时间（秒）。")]
	public float reloadTime = 1.3f;

	[Tooltip("每发消耗的弹匣子弹数。")]
	public int ammoPerShot = 1;

	[Header("Feel")]
	[Tooltip("后坐力：准星上抬角度（度）。")]
	public float recoilKick = 0.6f;

	[Tooltip("后坐力回落速度（度/秒）。")]
	public float recoilRecovery = 9f;

	[Tooltip("开火时的相机震动强度（米）。")]
	public float cameraShake = 0.012f;

	[Tooltip("是否自动连发。")]
	public bool automatic = true;

	[Header("Content")]
	[Tooltip("弹丸预制体。")]
	public GameObject projectilePrefab;

	[Tooltip("枪口特效。")]
	public GameObject muzzleFlashPrefab;

	[Tooltip("命中特效（敌人）。")]
	public GameObject hitEffectPrefab;

	[Tooltip("开火音效。")]
	public AudioClip fireClip;

	[Tooltip("换弹音效。")]
	public AudioClip reloadClip;

	/// <summary>单发间隔（秒）。</summary>
	public float ShotInterval { get { return fireRate > 0f ? 1f / fireRate : 0.2f; } }

	/// <summary>初始弹药总量（弹匣 + 备弹）。</summary>
	public int TotalAmmo { get { return Mathf.Max(0, magazineSize) + Mathf.Max(0, reserveAmmo); } }

	/// <summary>初始携带的备弹（不含弹匣内）。</summary>
	public int InitialReserve { get { return Mathf.Max(0, reserveAmmo); } }

	public void OnValidate()
	{
		if (damage < 0f) damage = 0f;
		if (fireRate <= 0f) fireRate = 0.1f;
		if (magazineSize <= 0) magazineSize = 1;
		if (pelletsPerShot <= 0) pelletsPerShot = 1;
		if (ammoPerShot <= 0) ammoPerShot = 1;
		if (spreadDegrees < 0f) spreadDegrees = 0f;
		if (reloadTime < 0f) reloadTime = 0f;
		if (reserveAmmo < 0) reserveAmmo = 0;
	}
}

/// <summary>武器种类（与 P3W 的 5 把枪一一对应）。</summary>
public enum WeaponArchetype
{
	Rifle = 0,
	Shotgun = 1,
	Smg = 2,
	Sniper = 3,
	GrenadeLauncher = 4,
}

/// <summary>
/// 定义资产的运行时校验结果。
/// </summary>
public struct WeaponValidation
{
	public bool valid;
	public string error;

	public static WeaponValidation Ok { get { return new WeaponValidation { valid = true, error = string.Empty }; } }

	public static WeaponValidation Fail(string reason)
	{
		return new WeaponValidation { valid = false, error = reason };
	}
}

/// <summary>武器定义的校验扩展。</summary>
public static class WeaponDefinitionExtensions
{
	/// <summary>检查数值合法性；P3W 的 WeaponCheck 会批量调用。</summary>
	public static WeaponValidation Validate(this WeaponDefinition definition)
	{
		if (definition == null) return WeaponValidation.Fail("definition is null");
		if (string.IsNullOrEmpty(definition.displayName)) return WeaponValidation.Fail("displayName empty");
		if (definition.damage <= 0f) return WeaponValidation.Fail("damage <= 0");
		if (definition.fireRate <= 0f) return WeaponValidation.Fail("fireRate <= 0");
		if (definition.magazineSize <= 0) return WeaponValidation.Fail("magazineSize <= 0");
		if (definition.pelletsPerShot <= 0) return WeaponValidation.Fail("pelletsPerShot <= 0");
		if (definition.reloadTime < 0f) return WeaponValidation.Fail("reloadTime < 0");
		if (definition.spreadDegrees < 0f) return WeaponValidation.Fail("spreadDegrees < 0");
		if (definition.ammoPerShot <= 0) return WeaponValidation.Fail("ammoPerShot <= 0");
		return WeaponValidation.Ok;
	}
}