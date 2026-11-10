namespace HextechRunes;

public sealed class NearDeathFeastRune : HextechRelicBase
{
	private const int DeathNegativeMaxHpDivisor = 2;
	private bool _nearDeathActive;
	private int _nearDeathDebt;
	private int _nearDeathStrengthBonus;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedNearDeathActive
	{
		get => _nearDeathActive;
		set => _nearDeathActive = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedNearDeathDebt
	{
		get => _nearDeathDebt;
		set => _nearDeathDebt = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedNearDeathStrengthBonus
	{
		get => _nearDeathStrengthBonus;
		set => _nearDeathStrengthBonus = Math.Max(0, value);
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("StrengthPerNegativeHp", 1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsIroncladPlayer(player);
	}

	// 规范模型上读 Owner 会触发 AssertMutable(RelicModel.Owner 三版同契约),必须先判 IsCanonical。
	public override bool ShowCounter => !IsCanonical && Owner != null;

	public override int DisplayAmount => !IsCanonical && Owner != null ? GetDeathNegativeHpLimit(Owner.Creature) : 0;

	internal static bool HasDyingState(Creature creature)
	{
		return GetRune(creature) != null;
	}

	internal static bool IsDyingButAlive(Creature creature)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		return rune != null
			&& rune._nearDeathActive
			&& creature.CurrentHp > 0
			&& rune._nearDeathDebt < GetDeathNegativeHpLimit(creature);
	}

	/// <summary>
	/// 纯只读:供特效层轮询「濒死狂宴」是否激活及其强度(负血债务 / 死亡上限,0..1)。
	/// 不修改任何状态,仅反映已同步的运行状态,各端轮询结果一致。
	/// </summary>
	internal static bool TryGetFeastIntensity(Creature creature, out float intensity)
	{
		intensity = 0f;
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune == null || !rune._nearDeathActive || creature.CurrentHp < 1)
		{
			return false;
		}

		int limit = GetDeathNegativeHpLimit(creature);
		intensity = limit > 0 ? Math.Clamp(rune._nearDeathDebt / (float)limit, 0f, 1f) : 0f;
		return true;
	}

	internal static bool ShouldInterceptLoseHp(Creature creature, decimal amount)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune == null || amount <= 0m)
		{
			return false;
		}

		return rune._nearDeathActive || creature.CurrentHp - HextechNearDeathHpLoss.ToHpLoss(amount) < 1;
	}

	// 在 Creature.LoseHpInternal 的同步前缀里运行：这里只改生命与负血债务，不发命令。
	// 力量补差放在 AfterCurrentHpChanged：原版 CreatureCmd.Damage 只要 UnblockedDamage > 0 就会派发它
	// （不看生命是否真的变化，0.107.1 / 0.110.0 / 0.111.0 相同），本方法返回的 UnblockedDamage 就是本次失血，
	// 所以"生命钉在 1、只有债务增加"时同样会走到那里并被等待。
	internal static DamageResult LoseHpAllowingDying(Creature creature, decimal amount, ValueProp props)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune == null || amount <= 0m)
		{
			return new DamageResult(creature, props);
		}

		HextechNearDeathHpLoss outcome = HextechNearDeathHpLoss.Resolve(
			rune._nearDeathActive,
			rune._nearDeathDebt,
			creature.CurrentHp,
			amount,
			GetDeathNegativeHpLimit(creature));
		rune._nearDeathActive = outcome.Dying;
		rune._nearDeathDebt = outcome.Debt;
		creature.SetCurrentHpInternal(outcome.CurrentHp);
		return outcome.ToDamageResult(creature, props);
	}

	internal static void ForceDeathThresholdForKill(Creature creature)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune != null)
		{
			rune._nearDeathActive = false;
			rune._nearDeathDebt = GetDeathNegativeHpLimit(creature);
			creature.SetCurrentHpInternal(0);
		}
	}

	internal static void PreserveNegativeHpAsDyingState(Creature creature, int requestedHp)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune == null)
		{
			creature.SetCurrentHpInternal(Math.Max(0, requestedHp));
			return;
		}

		int deathLimit = GetDeathNegativeHpLimit(creature);
		int debt = Math.Max(0, -requestedHp);
		if (debt >= deathLimit)
		{
			rune._nearDeathActive = false;
			rune._nearDeathDebt = deathLimit;
			creature.SetCurrentHpInternal(0);
			return;
		}

		// 力量补差同样交给 AfterCurrentHpChanged：负值来自 CreatureCmd.SetCurrentHp 时，请求值与旧生命必然不同，
		// 原版会在写入后派发该 Hook。
		rune._nearDeathActive = true;
		rune._nearDeathDebt = debt;
		creature.SetCurrentHpInternal(1);
	}

	internal static int GetDeathNegativeHpLimit(Creature creature)
	{
		return Math.Max(1, FloorToInt(creature.MaxHp / (decimal)DeathNegativeMaxHpDivisor));
	}

	internal static bool TryGetDisplayedHp(Creature creature, out int displayedHp)
	{
		NearDeathFeastRune? rune = GetRune(creature);
		if (rune != null && rune._nearDeathActive)
		{
			displayedHp = -rune._nearDeathDebt;
			return true;
		}

		displayedHp = 0;
		return false;
	}

	internal void RefreshDeathLimitDisplay()
	{
		InvokeDisplayAmountChanged();
	}

	public override Task AfterObtained()
	{
		RefreshDeathLimitDisplay();
		return Task.CompletedTask;
	}

	public override Task AfterRoomEntered(AbstractRoom room)
	{
		RefreshDeathLimitDisplay();
		return Task.CompletedTask;
	}

	public override Task BeforeCombatStart()
	{
		ResetNearDeathState();
		RefreshDeathLimitDisplay();
		return Task.CompletedTask;
	}

	public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
	{
		if (creature != Owner.Creature || !_nearDeathActive)
		{
			return;
		}

		await SyncNearDeathStrength();
	}

	public override Task AfterCombatVictory(CombatRoom room)
	{
		if (_nearDeathActive || Owner.Creature.CurrentHp < 1)
		{
			Flash(Array.Empty<Creature>());
			Owner.Creature.SetCurrentHpInternal(1);
		}

		ResetNearDeathState();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetNearDeathState();
		return Task.CompletedTask;
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner.Creature && IsDyingButAlive(target) ? 0m : 1m;
	}

	// 先预留目标加成再发命令：命令链里若再次触发失血（重入本方法），只会补新的差额。
	private async Task SyncNearDeathStrength()
	{
		int desiredBonus = _nearDeathActive
			? _nearDeathDebt * (int)DynamicVars["StrengthPerNegativeHp"].BaseValue
			: 0;
		int previousBonus = _nearDeathStrengthBonus;
		int delta = desiredBonus - previousBonus;
		_nearDeathStrengthBonus = desiredBonus;
		if (delta <= 0)
		{
			return;
		}

		try
		{
			Flash();
			await PowerCmd.Apply<StrengthPower>(Owner.Creature, delta, Owner.Creature, null);
		}
		catch
		{
			if (_nearDeathStrengthBonus == desiredBonus)
			{
				_nearDeathStrengthBonus = previousBonus;
			}

			throw;
		}
	}

	private void ResetNearDeathState()
	{
		_nearDeathActive = false;
		_nearDeathDebt = 0;
		_nearDeathStrengthBonus = 0;
	}

	private static NearDeathFeastRune? GetRune(Creature creature)
	{
		return creature.Player?.GetRelic<NearDeathFeastRune>();
	}
}
