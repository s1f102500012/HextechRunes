using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

/// <summary>
/// 给「灼烧」(<see cref="HextechBurnPower"/>) 加上和原版「中毒」一样的血条预测渲染：
/// 在生命值末端画出下次结算会损失的血量。橙色的烧条与绿色的毒条按各自实际结算先后排列
/// （先结算的贴最右、后结算的在其左侧），并把灼烧伤害纳入「灾厄」致死判定。
///
/// 原版 <c>NHealthBar.RefreshForeground</c> 硬编码只认 Poison/Doom，且场景里只有
/// <c>_poisonForeground</c>/<c>_doomForeground</c> 两个覆盖节点，模组无法改场景新增节点，
/// 因此在运行时克隆一个橙色 burn 前景节点，并整段前缀替换该方法以容纳三段几何。
/// </summary>
internal static class HextechBurnHealthBarHooks
{
	private static readonly Color BurnForegroundColor = new(1f, 0.65f, 0.08f);

	// 灼烧斩杀时血条数字的字体色/描边色（与毒的绿 #76FF40、灾厄的紫 #FB8DFF 并列的琥珀黄）。
	private static readonly Color BurnLethalFontColor = new("FFC233");
	private static readonly Color BurnLethalOutlineColor = new("3A1E00");
	private static readonly Color DoomLethalFontColor = new("FB8DFF");
	private static readonly Color DoomLethalOutlineColor = new("2D1263");

	private static readonly StringName FontColorOverride = "font_color";
	private static readonly StringName FontOutlineColorOverride = "font_outline_color";

	private static readonly ConditionalWeakTable<NHealthBar, Control> BurnForegrounds = new();

	// 两个补丁都依赖的原版私有成员;任一缺失时两个补丁都不安装(缺失项已进启动摘要)。
	private static readonly HealthBarMembers? Members = HealthBarMembers.TryResolve();

	/// <summary>
	/// 原版 <c>NHealthBar</c> 的私有成员(0.107.1 / 0.111.0 同名同签名):字段 <c>_creature</c>、
	/// <c>_hpForeground</c>、<c>_poisonForeground</c>、<c>_doomForeground</c>、<c>_hpLabel</c>;
	/// 私有属性 <c>MaxFgWidth</c> 的 getter 与私有方法 <c>GetFgWidth(int)</c> 预先绑定成委托,调用时不走反射。
	/// </summary>
	private sealed record HealthBarMembers(
		FieldInfo Creature,
		FieldInfo HpForeground,
		FieldInfo PoisonForeground,
		FieldInfo DoomForeground,
		FieldInfo HpLabel,
		Func<NHealthBar, float> MaxFgWidth,
		Func<NHealthBar, int, float> GetFgWidth)
	{
		internal static HealthBarMembers? TryResolve()
		{
			const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			Type type = typeof(NHealthBar);
			FieldInfo? creature = HextechHookReflection.TryGetField(type, "_creature");
			FieldInfo? hpForeground = HextechHookReflection.TryGetField(type, "_hpForeground");
			FieldInfo? poisonForeground = HextechHookReflection.TryGetField(type, "_poisonForeground");
			FieldInfo? doomForeground = HextechHookReflection.TryGetField(type, "_doomForeground");
			FieldInfo? hpLabel = HextechHookReflection.TryGetField(type, "_hpLabel");
			MethodInfo? maxFgWidthGetter = HextechHookReflection.TryGetProperty(type, "MaxFgWidth", InstanceFlags)?.GetMethod;
			MethodInfo? getFgWidth = HextechHookReflection.TryGetMethod(type, "GetFgWidth", InstanceFlags, typeof(int));
			if (creature == null
				|| hpForeground == null
				|| poisonForeground == null
				|| doomForeground == null
				|| hpLabel == null
				|| maxFgWidthGetter == null
				|| getFgWidth == null)
			{
				return null;
			}

			return new HealthBarMembers(
				creature,
				hpForeground,
				poisonForeground,
				doomForeground,
				hpLabel,
				maxFgWidthGetter.CreateDelegate<Func<NHealthBar, float>>(),
				getFgWidth.CreateDelegate<Func<NHealthBar, int, float>>());
		}
	}

	private static bool TryRenderForeground(HealthBarMembers members, NHealthBar instance)
	{
		if (members.Creature.GetValue(instance) is not Creature creature)
		{
			return false;
		}

		int currentHp = creature.CurrentHp;
		int burn = PredictBurnDamage(creature, currentHp);
		if (burn <= 0)
		{
			if (BurnForegrounds.TryGetValue(instance, out Control? existingBurnForeground)
				&& GodotObject.IsInstanceValid(existingBurnForeground))
			{
				existingBurnForeground.Visible = false;
			}

			return false;
		}

		if (members.HpForeground.GetValue(instance) is not Control hpForeground
			|| members.PoisonForeground.GetValue(instance) is not Control poisonForeground
			|| members.DoomForeground.GetValue(instance) is not Control doomForeground)
		{
			return false;
		}

		Control burnForeground = GetOrCreateBurnForeground(instance, poisonForeground);

		if (currentHp <= 0)
		{
			poisonForeground.Visible = false;
			doomForeground.Visible = false;
			burnForeground.Visible = false;
			hpForeground.Visible = false;
			return true;
		}

		// 无限生命（无敌）等特殊显示交回原版，避免缺少其专用配色常量；先藏掉烧条防残留。
		if (creature.HpDisplay.IsInfinite())
		{
			burnForeground.Visible = false;
			return false;
		}

		float maxFgWidth = members.MaxFgWidth(instance);
		hpForeground.Visible = true;
		hpForeground.OffsetRight = members.GetFgWidth(instance, currentHp) - maxFgWidth;

		// 走到这里时灼烧预测一定大于 0;中毒量取自中毒 Power 本身,大于 0 即表示持有中毒。
		int poison = creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0;

		// 按实际结算先后排列：先结算的段贴最右（最先从当前生命值扣起）。
		List<(int Amount, Control Node)> segments = [];
		if (poison <= 0)
		{
			segments.Add((burn, burnForeground));
			poisonForeground.Visible = false;
			poisonForeground.OffsetLeft = 0f;
		}
		else if (PoisonResolvesBeforeBurn(creature))
		{
			segments.Add((poison, poisonForeground));
			segments.Add((burn, burnForeground));
		}
		else
		{
			segments.Add((burn, burnForeground));
			segments.Add((poison, poisonForeground));
		}

		int remainingHp = currentHp;
		foreach ((int amount, Control node) in segments)
		{
			if (remainingHp <= 0)
			{
				node.Visible = false;
				continue;
			}

			int afterHp = Math.Max(0, remainingHp - amount);
			node.Visible = true;
			if (afterHp <= 0)
			{
				// 该段吃光剩余生命（斩杀）：和原版毒/灾厄致死一样整段覆盖到血条最左，
				// 不走 nine-patch 边距，避免左侧露出一截没盖住的小条。
				node.OffsetLeft = 0f;
			}
			else
			{
				int patchMarginLeft = node is NinePatchRect ninePatch ? ninePatch.PatchMarginLeft : 0;
				node.OffsetLeft = Math.Max(0f, members.GetFgWidth(instance, afterHp) - patchMarginLeft);
			}

			node.OffsetRight = members.GetFgWidth(instance, remainingHp) - maxFgWidth;
			remainingHp = afterHp;
		}

		int totalDotDamage = poison + burn;
		hpForeground.OffsetRight = members.GetFgWidth(instance, remainingHp) - maxFgWidth;
		hpForeground.Visible = remainingHp > 0;

		RenderDoom(members, instance, creature, hpForeground, doomForeground, maxFgWidth, currentHp, totalDotDamage);
		return true;
	}

	private static void RenderDoom(
		HealthBarMembers members,
		NHealthBar instance,
		Creature creature,
		Control hpForeground,
		Control doomForeground,
		float maxFgWidth,
		int currentHp,
		int totalDotDamage)
	{
		int doom = creature.GetPowerAmount<DoomPower>();
		if (!creature.HasPower<DoomPower>() || doom <= 0)
		{
			doomForeground.Visible = false;
			return;
		}

		doomForeground.Visible = true;
		float doomWidth = members.GetFgWidth(instance, doom) - maxFgWidth;
		bool doomLethal = doom >= currentHp - totalDotDamage; // 灾厄按「持续伤害结算后」是否仍致死
		bool dotLethal = totalDotDamage >= currentHp;
		if (doomLethal)
		{
			if (!dotLethal)
			{
				doomForeground.OffsetRight = hpForeground.OffsetRight;
				hpForeground.Visible = false;
			}
			else
			{
				hpForeground.Visible = false;
				doomForeground.Visible = false;
			}
		}
		else
		{
			int patchMarginRight = doomForeground is NinePatchRect ninePatch ? ninePatch.PatchMarginRight : 0;
			doomForeground.OffsetRight = Math.Min(0f, doomWidth + patchMarginRight);
			hpForeground.Visible = true;
		}
	}

	/// <summary>下次灼烧结算的预测掉血，直接用 <see cref="HextechBurnPower.CalculateHpLoss"/>（与中毒一样忽略格挡）。</summary>
	private static int PredictBurnDamage(Creature creature, int currentHp)
	{
		int stacks = creature.GetPowerAmount<HextechBurnPower>();
		return stacks <= 0 ? 0 : HextechBurnPower.CalculateHpLoss(currentHp, stacks);
	}

	/// <summary>
	/// 玩家身上：中毒在回合开始结算、灼烧在回合结束结算 → 中毒先。
	/// 敌人身上：两者都在回合开始结算 → 按 power 施加先后（<see cref="Creature.Powers"/> 列表顺序）。
	/// </summary>
	private static bool PoisonResolvesBeforeBurn(Creature creature)
	{
		if (creature.Side == CombatSide.Player)
		{
			return true;
		}

		return IndexOfPower<PoisonPower>(creature) <= IndexOfPower<HextechBurnPower>(creature);
	}

	private static int IndexOfPower<TPower>(Creature creature)
		where TPower : PowerModel
	{
		IReadOnlyList<PowerModel> powers = creature.Powers;
		for (int i = 0; i < powers.Count; i++)
		{
			if (powers[i] is TPower)
			{
				return i;
			}
		}

		return int.MaxValue;
	}

	private static Control GetOrCreateBurnForeground(NHealthBar instance, Control poisonForeground)
	{
		if (BurnForegrounds.TryGetValue(instance, out Control? existing) && GodotObject.IsInstanceValid(existing))
		{
			return existing;
		}

		Control clone = (Control)poisonForeground.Duplicate();
		clone.Name = "HextechBurnForeground";
		clone.SelfModulate = BurnForegroundColor;
		clone.Visible = false;
		poisonForeground.GetParent().AddChild(clone);
		BurnForegrounds.AddOrUpdate(instance, clone);
		return clone;
	}

	/// <summary>
	/// 整段替换 <c>RefreshForeground</c>(有灼烧预测时)。任意环节异常时返回 true 让原版方法照常执行，
	/// 保证最坏情况只是不显示烧条、绝不破坏血条。
	/// </summary>
	/// <remarks>
	/// 跳过型前缀:原版私有 <c>NHealthBar.RefreshForeground</c> 只认中毒/灾厄两段前景,没有 Hook 能插入第三段;
	/// 替换体按原版的中毒/灾厄几何与斩杀判定逐段复刻,只额外排入灼烧段。
	/// 激活条件:生物身上有灼烧且预测掉血大于 0;否则返回 true 交给原版。
	/// 版本:0.107.1 / 0.111.0 原方法一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NHealthBar), "RefreshForeground")]
	[HextechPatch("ui.burn-health-bar.foreground", "灼烧血条预测")]
	private static class RefreshForegroundPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => Members != null;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NHealthBar __instance)
		{
			try
			{
				return !TryRenderForeground(Members!, __instance);
			}
			catch (Exception ex)
			{
				HextechLog.Warn("Mayhem", $"Burn health bar render failed; falling back to vanilla: {ex.GetType().Name}: {ex.Message}");
				return true;
			}
		}
	}

	/// <summary>
	/// 在原版给血条数字上色之后，仅当「灼烧」参与斩杀时覆盖字色，做出和毒(绿)/灾厄(紫)并列的斩杀提示。
	/// 灼烧不影响结果时完全不动 vanilla 着色（含格挡色、无敌色）。
	/// </summary>
	[HarmonyPatch(typeof(NHealthBar), "RefreshText")]
	[HextechPatch("ui.burn-health-bar.text", "灼烧血条预测")]
	private static class RefreshTextPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => Members != null;

		[HarmonyPostfix]
		private static void Postfix(NHealthBar __instance)
		{
			HealthBarMembers members = Members!;
			try
			{
				if (members.Creature.GetValue(__instance) is not Creature creature)
				{
					return;
				}

				int currentHp = creature.CurrentHp;
				if (currentHp <= 0 || !creature.HpDisplay.ShowsNumbers() || creature.HpDisplay.IsInfinite())
				{
					return;
				}

				int burn = PredictBurnDamage(creature, currentHp);
				if (burn <= 0)
				{
					// 没有灼烧：毒/灾厄/默认的着色完全交给 vanilla。
					return;
				}

				int poison = creature.GetPower<PoisonPower>()?.CalculateTotalDamageNextTurn() ?? 0;
				if (poison >= currentHp)
				{
					// 毒单独已致死：保留 vanilla 的绿色斩杀。
					return;
				}

				int totalDot = poison + burn;
				Color fontColor;
				Color outlineColor;
				if (totalDot >= currentHp)
				{
					// 毒+烧合计致死：灼烧斩杀色（琥珀黄）。
					fontColor = BurnLethalFontColor;
					outlineColor = BurnLethalOutlineColor;
				}
				else
				{
					int doom = creature.GetPowerAmount<DoomPower>();
					bool doomLethalWithBurn = creature.HasPower<DoomPower>() && doom > 0 && doom >= currentHp - totalDot;
					bool doomLethalVanilla = doom >= currentHp - poison;
					if (!doomLethalWithBurn || doomLethalVanilla)
					{
						// 灼烧没有改变斩杀判定：维持 vanilla 着色。
						return;
					}

					// 灼烧把「灾厄」推成致死：补上灾厄斩杀色（紫）。
					fontColor = DoomLethalFontColor;
					outlineColor = DoomLethalOutlineColor;
				}

				if (members.HpLabel.GetValue(__instance) is not Control hpLabel)
				{
					return;
				}

				hpLabel.AddThemeColorOverride(FontColorOverride, fontColor);
				hpLabel.AddThemeColorOverride(FontOutlineColorOverride, outlineColor);
			}
			catch (Exception ex)
			{
				HextechLog.Warn("Mayhem", $"Burn health bar text recolor failed; leaving vanilla color: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
