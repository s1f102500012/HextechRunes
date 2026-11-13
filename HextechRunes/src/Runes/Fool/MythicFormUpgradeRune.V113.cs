#if STS2_113_OR_NEWER
namespace HextechRunes;

// 开局自动打出、获得时补牌、悬浮提示都由基类提供。神话形态不在 HextechFormAutoPlayHooks 的合并结算名单里，
// 多张时逐张走原版自动打出：每张各施加一层 MythicFormPower，升级版各自 IncrementUpgraded。
// 自动打出在持有者首回合 AfterPlayerTurnStartLate（手牌已抽完），所以第一张塔罗牌从第 2 回合的 BeforeHandDraw 开始，
// 与第 1 回合手动打出一致。
public sealed partial class MythicFormUpgradeRune : AutoPlayFormsAtCombatStartRuneBase<MythicForm>
{
	protected override bool IsAvailableForCharacter(Player player) => IsFoolPlayer(player);
}
#endif
