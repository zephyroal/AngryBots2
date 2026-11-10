using UnityEngine;

/// <summary>
/// 运行时自动接线入口：任何场景启动时都确保玩法基础层就位，
/// 不需要手工在场景里摆 <see cref="GameplayBootstrap"/>。
/// 已放置 bootstrap 的场景不会重复创建（幂等）。
/// </summary>
public static class GameplayRuntime
{
	/// <summary>是否禁用自动初始化（单元测试 / 特殊场景可设 true）。</summary>
	public static bool Disabled { get; set; }

	/// <summary>最近一次自动初始化完成的帧号（调试用）。</summary>
	public static int LastInitFrame { get; private set; } = -1;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	static void Install()
	{
		if (Disabled) return;
		if (GameplayBootstrap.Instance != null) return;

		GameplayBootstrap.EnsureInstance();
		if (GameplayBootstrap.Instance != null && !GameplayBootstrap.Instance.Applied)
			GameplayBootstrap.Instance.Apply();

		LastInitFrame = Time.frameCount;
	}
}