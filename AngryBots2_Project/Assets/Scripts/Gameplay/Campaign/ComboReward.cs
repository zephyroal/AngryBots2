using UnityEngine;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 连击里程碑奖励：每达到 <see cref="milestoneStep"/> 连击，回复少量生命并补 1 颗手雷。
	/// 把“连击”从纯计分变成可感知的持续收益。
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ComboReward : MonoBehaviour
	{
		public static ComboReward Instance { get; private set; }

		public int milestoneStep = 5;
		public int healPerMilestone = 8;
		public int grenadePerMilestone = 1;

		int lastMilestone;
		ScoreSystem score;
		bool hooked;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		void OnEnable()
		{
			Hook(true);
		}

		void OnDisable()
		{
			Hook(false);
		}

		void Start()
		{
			Hook(true);
		}

		void Hook(bool subscribe)
		{
			score = ScoreSystem.Instance != null ? ScoreSystem.Instance : FindObjectOfType<ScoreSystem>();
			if (score == null) return;
			if (subscribe == hooked) return;

			hooked = subscribe;
			if (subscribe) score.ComboChanged += OnComboChanged;
			else score.ComboChanged -= OnComboChanged;
		}

		void OnComboChanged(int combo)
		{
			HandleCombo(combo);
		}

		/// <summary>处理一次连击变化（事件入口，也便于自检直接调用）。</summary>
		public void HandleCombo(int combo)
		{
			if (combo <= 0)
			{
				lastMilestone = 0;
				return;
			}

			int milestone = combo / Mathf.Max(1, milestoneStep);
			if (milestone <= lastMilestone) return;
			lastMilestone = milestone;

			var player = PlayerSource.Player;
			if (player == null) return;

			var health = player.GetComponent<PlayerHealth>();
			if (health != null && healPerMilestone > 0) health.Heal(healPerMilestone);

			var weapons = player.GetComponent<WeaponSystem>();
			if (weapons != null && grenadePerMilestone > 0) weapons.AddGrenades(grenadePerMilestone);

			if (CampaignUI.Instance != null)
				CampaignUI.Instance.ShowHint("COMBO x" + combo + "   +" + healPerMilestone + " HP   +" + grenadePerMilestone + " GRENADE", 1.6f);

			Debug.Log("[ComboReward] 连击里程碑 " + combo + "，回复 " + healPerMilestone + " HP / +" + grenadePerMilestone + " 手雷");
		}

		public static ComboReward EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<ComboReward>();
			if (existing != null) return existing;
			var go = new GameObject("[ComboReward]");
			return go.AddComponent<ComboReward>();
		}
	}
}