namespace HextechRunes;

public sealed class GoldrendRune : HextechRelicBase
{
	// 原版 PlayerCmd.GainGold 对本机玩家直接播放 event:/sfx/ui/gold/gold_1..3,没有静音参数。
	private const string GoldSfxPrefix = "event:/sfx/ui/gold/";

	// 按命中发钱的海克斯在多段攻击时金币音效会连响;只在本次发钱的异步作用域内静音,别处的金币音效不受影响。
	private static readonly AsyncLocal<bool> SuppressGoldSfx = new();

	private bool _grantingGold;
	// 本回合是否已经响过一次金币音效;纯本地表现,不进存档、不参与联机。
	private bool _goldSfxPlayedThisTurn;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("CountPerHit", 10m)
	];

	// 旧版本存档兼容占位：原为待发放的战后金币计数，金币已改为触发时立即发放；名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedCountThisCombat
	{
		get => 0;
		set { }
	}

	public override Task BeforeCombatStart()
	{
		_goldSfxPlayedThisTurn = false;
		return Task.CompletedTask;
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (_grantingGold || target.Side != CombatSide.Enemy || result.TotalDamage <= 0 || !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		// 纯本地表现:延后一帧播放、自带异常隔离,不影响下面的共享结算。
		HextechCombatVfx.CoinBurst(target);

		// 每回合只响第一次金币音效。
		EnsureTurnScopedStateCurrent(() => _goldSfxPlayedThisTurn = false);
		bool muteGoldSfx = _goldSfxPlayedThisTurn;
		_goldSfxPlayedThisTurn = true;

		// 金币可经鲜血神像触发受伤及反击，不能让这条反馈链再次触发自身。
		_grantingGold = true;
		try
		{
			await GainGoldAsync(DynamicVars["CountPerHit"].IntValue, Owner, muteGoldSfx);
		}
		finally
		{
			_grantingGold = false;
		}
	}

	/// <summary>
	/// 战斗内按命中发金币的共用入口（夺金、升级：我的小钱钱）：<paramref name="muteGoldSfx"/> 为 true 时，
	/// 只在这次发钱的异步作用域内静音原版金币音效，多段命中不会连响。静音是纯本地表现，发钱本身各端照常结算。
	/// </summary>
	internal static async Task GainGoldAsync(decimal amount, Player player, bool muteGoldSfx)
	{
		bool previousSuppress = SuppressGoldSfx.Value;
		SuppressGoldSfx.Value = muteGoldSfx;
		try
		{
			await PlayerCmd.GainGold(amount, player);
		}
		finally
		{
			SuppressGoldSfx.Value = previousSuppress;
		}
	}

	internal static bool ShouldSuppressSfx(string? sfx)
	{
		return SuppressGoldSfx.Value && sfx != null && sfx.StartsWith(GoldSfxPrefix, StringComparison.Ordinal);
	}

	// 只在 GainGoldAsync 要求静音的发钱作用域里、且是金币音效时跳过;其余音效与其他来源的金币音效照常播放。
	[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), typeof(string), typeof(float))]
	[HextechPatch("rune.goldrend.gold-sfx", "夺金", Rune = typeof(GoldrendRune))]
	private static class GoldSfxPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(string sfx)
		{
			return !ShouldSuppressSfx(sfx);
		}
	}
}
