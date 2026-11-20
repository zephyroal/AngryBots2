using UnityEngine;

/// <summary>
/// 旧 <see cref="WeaponSystem"/> 的扩展子类：在不改动原脚本、不影响旧序列化数据的前提下
/// 补出「补弹」能力，供 <see cref="SimplePickup"/>（弹药包）与后续 P3W 武器系统使用。
/// <para>
/// <c>Player_Prefab</c> 里的 <see cref="WeaponSystem"/> 组件可直接替换成这个子类，
/// 序列化字段完全一致，旧数值全部保留；旧的 <c>AddGrenades</c> / <c>TryFire</c> 等 API 继续可用。
/// </para>
/// <para>
/// 实现说明：父类的 <c>RifleAmmo</c> / <c>ShotgunAmmo</c> 是 <c>{ get; private set; }</c>，
/// 父类没有提供补弹入口。这里用反射写回私有 setter（IL2CPP 下私有属性的 setter 依然保留，
/// 只是不能内联），并在写回后调用父类私有的 <c>RaiseAmmoChanged</c> 让 HUD 同步刷新。
/// 反射只在本类初始化时查一次。
/// </para>
/// </summary>
public sealed class WeaponSystemAdapter : WeaponSystem
{
	[Tooltip("补弹时是否两种武器各补一半（否则只补当前武器弹匣）。")]
	public bool fillBothMags = true;

	static readonly System.Reflection.PropertyInfo RifleAmmoProperty = FindAmmoProperty("RifleAmmo");
	static readonly System.Reflection.PropertyInfo ShotgunAmmoProperty = FindAmmoProperty("ShotgunAmmo");
	static readonly System.Reflection.PropertyInfo SmgAmmoProperty = FindAmmoProperty("SmgAmmo");
	static readonly System.Reflection.PropertyInfo SniperAmmoProperty = FindAmmoProperty("SniperAmmo");
	static readonly System.Reflection.MethodInfo RaiseAmmoChangedMethod = typeof(WeaponSystem).GetMethod(
		"RaiseAmmoChanged",
		System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

	/// <summary>
	/// 补弹：给当前武器弹匣补 <paramref name="amount"/> 发，返回实际补充数量。
	/// </summary>
	public int AddAmmo(int amount)
	{
		if (amount <= 0) return 0;

		int added;

		if (fillBothMags)
		{
			int rifleMissing = Mathf.Max(0, rifleMagSize - RifleAmmo);
			int shotgunMissing = Mathf.Max(0, shotgunMagSize - ShotgunAmmo);
			int smgMissing = Mathf.Max(0, smgMagSize - SmgAmmo);
			int sniperMissing = Mathf.Max(0, sniperMagSize - SniperAmmo);

			int rifleAdd = Mathf.Min(rifleMissing, Mathf.CeilToInt(amount / 4f));
			int shotgunAdd = Mathf.Min(shotgunMissing, Mathf.CeilToInt(amount / 4f));
			int smgAdd = Mathf.Min(smgMissing, Mathf.CeilToInt(amount / 4f));
			int sniperAdd = Mathf.Min(sniperMissing, Mathf.Max(0, amount - rifleAdd - shotgunAdd - smgAdd));
			if (rifleAdd + shotgunAdd + smgAdd + sniperAdd == 0) return 0;

			WriteAmmo(RifleAmmo + rifleAdd, ShotgunAmmo + shotgunAdd, SmgAmmo + smgAdd, SniperAmmo + sniperAdd);
			added = rifleAdd + shotgunAdd + smgAdd + sniperAdd;
		}
		else if (Mode == WeaponMode.Rifle)
		{
			int add = Mathf.Min(Mathf.Max(0, rifleMagSize - RifleAmmo), amount);
			if (add == 0) return 0;
			WriteAmmo(RifleAmmo + add, ShotgunAmmo, SmgAmmo, SniperAmmo);
			added = add;
		}
		else if (Mode == WeaponMode.Shotgun)
		{
			int add = Mathf.Min(Mathf.Max(0, shotgunMagSize - ShotgunAmmo), amount);
			if (add == 0) return 0;
			WriteAmmo(RifleAmmo, ShotgunAmmo + add, SmgAmmo, SniperAmmo);
			added = add;
		}
		else if (Mode == WeaponMode.Sniper)
		{
			int add = Mathf.Min(Mathf.Max(0, sniperMagSize - SniperAmmo), amount);
			if (add == 0) return 0;
			WriteAmmo(RifleAmmo, ShotgunAmmo, SmgAmmo, SniperAmmo + add);
			added = add;
		}
		else
		{
			int add = Mathf.Min(Mathf.Max(0, smgMagSize - SmgAmmo), amount);
			if (add == 0) return 0;
			WriteAmmo(RifleAmmo, ShotgunAmmo, SmgAmmo + add, SniperAmmo);
			added = add;
		}

		if (added > 0) NotifyAmmoChanged();
		return added;
	}

	/// <summary>把两种武器都补满，并把手雷补到上限。</summary>
	public void RefillAll()
	{
		WriteAmmo(rifleMagSize, shotgunMagSize, smgMagSize, sniperMagSize);
		AddGrenades(maxGrenadeCharges);
		NotifyAmmoChanged();
	}

	/// <summary>当前武器剩余的弹匣空间。</summary>
	public int MissingAmmo
	{
		get { return Mathf.Max(0, CurrentMagSize - CurrentMag); }
	}

	void WriteAmmo(int rifle, int shotgun, int smg, int sniper)
	{
		int clampedRifle = Mathf.Clamp(rifle, 0, rifleMagSize);
		int clampedShotgun = Mathf.Clamp(shotgun, 0, shotgunMagSize);
		int clampedSmg = Mathf.Clamp(smg, 0, smgMagSize);
		int clampedSniper = Mathf.Clamp(sniper, 0, sniperMagSize);

		if (RifleAmmoProperty != null && RifleAmmoProperty.CanWrite) RifleAmmoProperty.SetValue(this, clampedRifle);
		else Debug.LogWarning("[WeaponSystemAdapter] 无法写入 RifleAmmo（反射失败）");

		if (ShotgunAmmoProperty != null && ShotgunAmmoProperty.CanWrite) ShotgunAmmoProperty.SetValue(this, clampedShotgun);
		else Debug.LogWarning("[WeaponSystemAdapter] 无法写入 ShotgunAmmo（反射失败）");

		if (SmgAmmoProperty != null && SmgAmmoProperty.CanWrite) SmgAmmoProperty.SetValue(this, clampedSmg);
		else Debug.LogWarning("[WeaponSystemAdapter] 无法写入 SmgAmmo（反射失败）");

		if (SniperAmmoProperty != null && SniperAmmoProperty.CanWrite) SniperAmmoProperty.SetValue(this, clampedSniper);
		else Debug.LogWarning("[WeaponSystemAdapter] 无法写入 SniperAmmo（反射失败）");
	}

	void NotifyAmmoChanged()
	{
		// C# 事件只能在声明类内触发，这里统一走父类的私有 RaiseAmmoChanged。
		if (RaiseAmmoChangedMethod != null) RaiseAmmoChangedMethod.Invoke(this, null);
	}

	static System.Reflection.PropertyInfo FindAmmoProperty(string name)
	{
		var property = typeof(WeaponSystem).GetProperty(
			name,
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		if (property == null) Debug.LogError("[WeaponSystemAdapter] 找不到 WeaponSystem." + name);
		return property;
	}
}