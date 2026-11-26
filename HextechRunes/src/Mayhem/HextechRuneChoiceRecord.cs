namespace HextechRunes;

/// <summary>
/// 一次海克斯三选一的结果：哪一幕、第几位玩家的第几次选择，给出的选项、选中的那个和重随次数。
/// 这是对局事实：随存档保存（<c>SavedTelemetryChoicesJson</c>，属性名是历史遗留，不能改），
/// 读档后用来恢复已完成的阶段与选择记录；遥测只在对局结束时读取它上报。
/// 属性名与顺序就是存档里的 JSON 结构，改动等于改存档格式。
/// </summary>
internal sealed record HextechRuneChoiceRecord(
	int ActIndex,
	int PlayerSlot,
	int ChoiceOrdinal,
	string Rarity,
	IReadOnlyList<string> Options,
	string? Selected,
	int RerollCount);
