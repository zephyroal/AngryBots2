using System;
using UnityEngine;

namespace ABGameplay.Campaign
{
	/// <summary>
	/// 计分 / 连击 / 货币系统。
	/// <para>
	/// 击杀 → 连击 +1（连击窗口 4 秒）→ 倍率 1 + 0.1×连击（上限 <see cref="MaxMultiplier"/>）。
	/// 受伤或窗口超时 → 连击清零。分数按 10:1 折算为商店货币。
	/// </para>
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ScoreSystem : MonoBehaviour
	{
		public static ScoreSystem Instance { get; private set; }

		[Header("Combo")]
		public float comboWindow = 4f;
		public float comboMultiplierStep = 0.1f;
		public float maxMultiplier = 3f;

		[Header("Economy")]
		[Tooltip("每多少分折算 1 货币。")]
		public int scorePerCurrency = 10;

		public int Score { get; private set; }
		public int Combo { get; private set; }
		public int BestCombo { get; private set; }
		public int Currency { get; private set; }
		public int TotalKills { get; private set; }

		/// <summary>(总分, 本次增量, 当前连击)</summary>
		public event Action<int, int, int> ScoreChanged;
		public event Action<int> ComboChanged;
		public event Action<int> CurrencyChanged;

		float comboTimer;
		bool hooked;

		public float Multiplier
		{
			get { return Mathf.Min(1f + Mathf.Max(0, Combo) * comboMultiplierStep, maxMultiplier); }
		}

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
			var save = SaveSystem.Data;
			Currency = save.currency;
			BestCombo = save.bestCombo;
			RaiseAll();
		}

		void Hook(bool subscribe)
		{
			if (subscribe == hooked) return;
			hooked = subscribe;

			if (subscribe)
			{
				GameEvents.EnemyKilled += OnEnemyKilled;
				GameEvents.PlayerDamaged += OnPlayerDamaged;
				GameEvents.PickupCollected += OnPickupCollected;
				GameEvents.ScoreChanged += OnExternalScore;
			}
			else
			{
				GameEvents.EnemyKilled -= OnEnemyKilled;
				GameEvents.PlayerDamaged -= OnPlayerDamaged;
				GameEvents.PickupCollected -= OnPickupCollected;
				GameEvents.ScoreChanged -= OnExternalScore;
			}
		}

		void Update()
		{
			if (Combo <= 0) return;

			comboTimer -= Time.deltaTime;
			if (comboTimer <= 0f) ResetCombo();
		}

		void OnEnemyKilled(EnemyKind kind, Vector3 position, GameObject killer, int baseScore)
		{
			RegisterKill(baseScore);
		}

		/// <summary>登记一次击杀（同时用于自检 / 脚本调用）。</summary>
		public void RegisterKill(int baseScore)
		{
			TotalKills++;
			Combo++;
			comboTimer = comboWindow;
			BestCombo = Mathf.Max(BestCombo, Combo);

			AddScore(baseScore > 0 ? baseScore : 10);
			if (ComboChanged != null) ComboChanged(Combo);

			var save = SaveSystem.Data;
			save.totalKills++;
			save.bestCombo = Mathf.Max(save.bestCombo, BestCombo);
			SaveSystem.MarkDirty();
		}

		void OnPlayerDamaged(int current, int max, DamageInfo info)
		{
			ResetCombo();
		}

		void OnPickupCollected(string what, float amount)
		{
			if (amount > 0f) AddScore(Mathf.RoundToInt(amount));
		}

		/// <summary>外部（如波次时间奖励）通过事件总线加的分，避免与自身事件形成递归。</summary>
		void OnExternalScore(int total, int delta)
		{
			if (delta <= 0) return;
			Score += delta;
			if (ScoreChanged != null) ScoreChanged(Score, delta, Combo);
			AddCurrency(Mathf.Max(1, delta / Mathf.Max(1, scorePerCurrency)));
		}

		/// <summary>加分（自动套用连击倍率）。</summary>
		public int AddScore(int basePoints)
		{
			if (basePoints <= 0) return 0;

			int gain = Mathf.Max(1, Mathf.RoundToInt(basePoints * Multiplier));
			Score += gain;

			if (ScoreChanged != null) ScoreChanged(Score, gain, Combo);
			AddCurrency(Mathf.Max(1, gain / Mathf.Max(1, scorePerCurrency)));

			var save = SaveSystem.Data;
			if (Score > save.bestScore) save.bestScore = Score;
			SaveSystem.MarkDirty();
			return gain;
		}

		public void AddCurrency(int amount)
		{
			if (amount == 0) return;
			Currency = Mathf.Max(0, Currency + amount);
			if (CurrencyChanged != null) CurrencyChanged(Currency);

			SaveSystem.Data.currency = Currency;
			SaveSystem.MarkDirty();
		}

		public bool CanAfford(int cost)
		{
			return cost <= 0 || Currency >= cost;
		}

		public bool TrySpend(int cost)
		{
			if (cost <= 0) return true;
			if (Currency < cost) return false;
			AddCurrency(-cost);
			return true;
		}

		public void ResetCombo()
		{
			if (Combo == 0) return;
			Combo = 0;
			comboTimer = 0f;
			if (ComboChanged != null) ComboChanged(0);
		}

		/// <summary>关卡结束时把成绩写回存档。</summary>
		public void Persist()
		{
			var save = SaveSystem.Data;
			save.currency = Currency;
			save.bestScore = Mathf.Max(save.bestScore, Score);
			save.bestCombo = Mathf.Max(save.bestCombo, BestCombo);
			save.totalScore += Score;
			SaveSystem.RecordRun(Score);
			SaveSystem.Save();
		}

		void RaiseAll()
		{
			if (ScoreChanged != null) ScoreChanged(Score, 0, Combo);
			if (ComboChanged != null) ComboChanged(Combo);
			if (CurrencyChanged != null) CurrencyChanged(Currency);
		}

		public static ScoreSystem EnsureInstance()
		{
			if (Instance != null) return Instance;
			var existing = FindObjectOfType<ScoreSystem>();
			if (existing != null) return existing;
			var go = new GameObject("[ScoreSystem]");
			return go.AddComponent<ScoreSystem>();
		}
	}
}