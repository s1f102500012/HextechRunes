namespace HextechRunes;

/// <summary>
/// 其他模组登记的敌方海克斯（每个登记一个实例）：对每个敌人施加一次登记的能力，层数取当前强度档位（= 当前幕）对应的值，
/// 不大于 0 的档位不施加。效果本身完全由该能力的原版钩子实现，本类不覆写其他分发钩子。
/// 施加时机与蜂群术士的人体蜂房相同：开战时的所有敌人、战斗中新加入的敌人各一次，首领转阶段（replayOneShotPowers）重放。
/// 施加走原版 <c>PowerCmd.Apply</c>（与内置对非缩放能力的施加一致），能力自己声明的原版联机缩放照常生效，本体不再按人数缩放。
/// </summary>
internal sealed class ExternalPowerEnemyHex : HextechEnemyHexEffect
{
	private static readonly MethodInfo ApplyPowerMethod = typeof(HextechPowerCmdCompat).GetMethod(
		nameof(HextechPowerCmdCompat.Apply),
		1,
		[typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)])
		?? throw new MissingMethodException(nameof(HextechPowerCmdCompat), nameof(HextechPowerCmdCompat.Apply));

	private readonly ResolvedExternalEnemyHex _external;
	private readonly Func<Creature, decimal, Creature?, CardModel?, bool, Task> _applyPower;

	// applyPower 只给测试替换（测试进程跑不了原版施加命令）。
	internal ExternalPowerEnemyHex(
		ResolvedExternalEnemyHex external,
		Func<Creature, decimal, Creature?, CardModel?, bool, Task>? applyPower = null)
	{
		_external = external;
		// 等同内置写的 PowerCmd.Apply<TPower>(...)：能力类型在登记时才知道，按类型封闭一次泛型方法，返回的 Task<TPower?> 协变为 Task。
		_applyPower = applyPower ?? ApplyPowerMethod
			.MakeGenericMethod(external.Registration.PowerType)
			.CreateDelegate<Func<Creature, decimal, Creature?, CardModel?, bool, Task>>();
	}

	internal override MonsterHexKind Kind => _external.Kind;

	internal override async Task ApplyPersistentToEnemy(HextechEnemyHexContext context, Creature creature, int? maxHpBaseOverride, bool replayOneShotPowers)
	{
		int amount = GetAmount(context.GetStrengthTier(Kind));
		if (amount <= 0 || creature.CombatId == null)
		{
			return;
		}

		HashSet<uint> applied = GetAppliedSet(context.Tracking);
		if (!HextechCombatProcTracker.TryMarkPersistentHexApplied(applied, creature, replayOneShotPowers) || !creature.IsAlive)
		{
			return;
		}

		await _applyPower(creature, amount, creature, null, false);
	}

	internal int GetAmount(int strengthTier)
	{
		ExternalEnemyHexRegistration registration = _external.Registration;
		return strengthTier switch
		{
			<= 1 => registration.AmountAct1,
			2 => registration.AmountAct2,
			_ => registration.AmountAct3
		};
	}

	private HashSet<uint> GetAppliedSet(HextechMayhemCombatTrackingState tracking)
	{
		int key = (int)Kind;
		if (!tracking.ExternalEnemyHexApplied.TryGetValue(key, out HashSet<uint>? applied))
		{
			applied = [];
			tracking.ExternalEnemyHexApplied[key] = applied;
		}

		return applied;
	}
}
