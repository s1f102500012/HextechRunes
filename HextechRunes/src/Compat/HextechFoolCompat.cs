using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes;

/// <summary>
/// 共享代码对愚者（0.113.0 新角色）的唯一入口：三个变体都编译的代码要问"这名玩家是不是愚者""本变体有没有愚者"时经这里，
/// 不引用 0.113.0 专属类型。愚者角色类型按名字在原版程序集里查找，旧变体上找不到，于是
/// <see cref="IsFoolPlayer"/> 恒为 false、愚者专属海克斯拿不到、界面不列出愚者分组。
/// 各海克斯自己的 <c>src/Runes/Fool/*.V113.cs</c> 只在 0.113.0 编译，直接调用原版类型，不经这里。
/// </summary>
internal static class HextechFoolCompat
{
	// 与 0.113.0 的 MegaCrit.Sts2.Core.Models.Characters.Fool 同名；测试 FoolCompatResolvesVanillaFoolOn113 核对两者一致。
	private const string FoolCharacterTypeName = "MegaCrit.Sts2.Core.Models.Characters.Fool";

	private static readonly Type? FoolCharacterType = typeof(Ironclad).Assembly.GetType(FoolCharacterTypeName);

	internal static bool IsFoolCharacterAvailable => FoolCharacterType != null;

	internal static ModelId? FoolCharacterId => FoolCharacterType == null ? null : ModelDb.GetId(FoolCharacterType);

	internal static bool IsFoolPlayer(Player player)
	{
		return FoolCharacterType != null && player.Character.Id == ModelDb.GetId(FoolCharacterType);
	}
}
