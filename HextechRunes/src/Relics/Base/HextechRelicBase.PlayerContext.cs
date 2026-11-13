namespace HextechRunes;

public abstract partial class HextechRelicBase
{
	public static bool IsNetworkMultiplayerRun()
	{
		return HextechPlayerContextHelper.IsNetworkMultiplayerRun();
	}

	protected static bool IsNetworkMultiplayer()
	{
		return IsNetworkMultiplayerRun();
	}

	protected int GetPlayerActNumberForScaling()
	{
		return HextechPlayerContextHelper.GetActNumberForScaling(Owner);
	}

	protected bool IsDefectPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsDefectPlayer(player);
	}

	protected bool IsDefectOwner => IsDefectPlayer(Owner);

	protected bool IsIroncladPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsIroncladPlayer(player);
	}

	protected bool IsSilentPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsSilentPlayer(player);
	}

	protected bool IsRegentPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsRegentPlayer(player);
	}

	protected bool IsRegentOwner => IsRegentPlayer(Owner);

	protected bool IsNecrobinderPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsNecrobinderPlayer(player);
	}

	protected bool IsFoolPlayer(Player player)
	{
		return HextechPlayerContextHelper.IsFoolPlayer(player);
	}

	// "战斗第一回合"类效果按持有者自己的回合数判定，不看 RoundNumber：额外回合不推进回合号，
	// 持有者在第 1 回合拿到额外回合时（佩尔之眼等）RoundNumber 仍为 1，会让开局效果再结算一次。
	// 原版 TurnNumber 从 1 开始，只在该玩家开始新回合（含额外回合）时递增。
	protected bool IsOwnersFirstTurn => Owner.PlayerCombatState?.TurnNumber == 1;

	// 与原版 Player.GetRelic<T>() 同口径：同类符文重复持有时只认第一件。治疗系数一直按"是否持有"只乘一次，
	// 改由各符文实现 IHextechHealingMultiplierProvider 后用它保持原结果。
	protected bool IsFirstOwnedInstance(Player player)
	{
		return ReferenceEquals(player.Relics.FirstOrDefault(relic => relic.GetType() == GetType()), this);
	}
}
