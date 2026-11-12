namespace HextechRunes;

internal sealed partial class HextechMayhemCombatTrackingState
{
	public string Serialize()
	{
		return HextechMayhemCombatTrackingSerializer.Serialize(this);
	}

	public void Restore(string? json)
	{
		WarnIfCombatInProgress("restore");
		HextechMayhemCombatTrackingSerializer.Clear(this);
		if (string.IsNullOrWhiteSpace(json))
		{
			return;
		}

		try
		{
			HextechMayhemCombatTrackingSerializer.Restore(this, json);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Failed to restore combat tracking snapshot: {ex}");
			HextechMayhemCombatTrackingSerializer.Clear(this);
		}
	}
}
