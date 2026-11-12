using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

/// <summary>
/// 原版模组列表（行标题与右侧详情）直接显示 manifest 的 name/description，它们只有一个字符串、不随语言变化。
/// 这里只对本体与拓展包两个 mod id，把名称/介绍换成当前语言 <c>main_menu_ui</c> 表里的文案；
/// 拓展包的键放在拓展包自己的 main_menu_ui.json，只有拓展包已加载时原版才会合并进来。
/// 键缺失、查表失败或标签已被其他模组改写时保持原样（manifest 原文），绝不显示键名。
/// </summary>
/// <remarks>
/// 切换语言只能在主菜单的设置里进行，原版 <c>NLanguageDropdown</c> 随后调用 <c>NGame.Relocalize</c> 重建整个主菜单，
/// 模组列表界面和行会重新创建并再次经过这两个后缀，所以不订阅语言切换事件。
/// </remarks>
internal static class HextechModListLocalizationHooks
{
	private const string Tag = "ModListLoc";
	private const string Feature = "模组列表本地化";
	private const string MainMenuLocTable = "main_menu_ui";

	internal const string MainNameKey = "HEXTECH_MOD_NAME";
	internal const string MainDescriptionKey = "HEXTECH_MOD_DESCRIPTION";
	internal const string SponsorNameKey = "HEXTECH_SPONSOR_MOD_NAME";
	internal const string SponsorDescriptionKey = "HEXTECH_SPONSOR_MOD_DESCRIPTION";

	// 原版场景节点名(0.107.1 / 0.111.0 一致):NModMenuRow._Ready 取 "Title",
	// NModInfoContainer._Ready 取 "ModTitle" / "ModDescription"。用公开的节点查找，不读私有字段 _title/_description。
	private const string RowTitleNode = "Title";
	private const string InfoTitleNode = "ModTitle";
	private const string InfoDescriptionNode = "ModDescription";

	/// <summary>只认本体与拓展包；其他模组一律不处理。</summary>
	internal static bool TryGetKeys(string? modId, [NotNullWhen(true)] out string? nameKey, [NotNullWhen(true)] out string? descriptionKey)
	{
		switch (modId)
		{
			case ModInfo.Id:
				nameKey = MainNameKey;
				descriptionKey = MainDescriptionKey;
				return true;
			case HextechExternalContentRegistry.SponsorPackModId:
				nameKey = SponsorNameKey;
				descriptionKey = SponsorDescriptionKey;
				return true;
			default:
				nameKey = null;
				descriptionKey = null;
				return false;
		}
	}

	/// <summary>
	/// 标题替换：只有标签仍是原版写入的文本时才换成本地化名称。返回 null 表示保持现状。
	/// </summary>
	internal static string? ResolveTitle(string? modId, string currentText, string vanillaText, Func<string, string?> lookup)
	{
		if (!TryGetKeys(modId, out string? nameKey, out _)
			|| !string.Equals(currentText, vanillaText, StringComparison.Ordinal))
		{
			return null;
		}

		string? localized = lookup(nameKey);
		return string.IsNullOrWhiteSpace(localized) ? null : localized;
	}

	/// <summary>
	/// 详情替换：原版把作者、版本、介绍和加载错误拼成一段文本，这里只把其中的 manifest 介绍原文换成本地化介绍，
	/// 其余部分（含红色加载错误）原样保留。找不到原文（manifest 无介绍或已被改写）时返回 null。
	/// </summary>
	internal static string? ResolveDescription(string? modId, string currentText, string? manifestDescription, Func<string, string?> lookup)
	{
		if (!TryGetKeys(modId, out _, out string? descriptionKey) || string.IsNullOrEmpty(manifestDescription))
		{
			return null;
		}

		int index = currentText.IndexOf(manifestDescription, StringComparison.Ordinal);
		if (index < 0)
		{
			return null;
		}

		string? localized = lookup(descriptionKey);
		if (string.IsNullOrWhiteSpace(localized))
		{
			return null;
		}

		return string.Concat(currentText.AsSpan(0, index), localized, currentText.AsSpan(index + manifestDescription.Length));
	}

	private static string? LookupMainMenuText(string key)
	{
		return LocString.Exists(MainMenuLocTable, key) ? new LocString(MainMenuLocTable, key).GetRawText() : null;
	}

	private static void ApplyRowTitle(NModMenuRow row)
	{
		Mod? mod = row.Mod;
		ModManifest? manifest = mod?.manifest;
		if (manifest == null
			|| !TryGetKeys(manifest.id, out _, out _)
			|| row.GetNodeOrNull<MegaRichTextLabel>(RowTitleNode) is not { } title)
		{
			return;
		}

		// 与原版 NModMenuRow._Ready 的回退链一致。
		string vanillaText = manifest.name ?? manifest.id ?? "<null>";
		if (ResolveTitle(manifest.id, title.Text, vanillaText, LookupMainMenuText) is { } localized)
		{
			title.Text = localized;
		}
	}

	private static void ApplyInfo(NModInfoContainer container, Mod mod)
	{
		ModManifest? manifest = mod.manifest;
		if (manifest == null || !TryGetKeys(manifest.id, out _, out _))
		{
			return;
		}

		// 与原版 NModInfoContainer.Fill 的回退一致。
		if (container.GetNodeOrNull<MegaRichTextLabel>(InfoTitleNode) is { } title
			&& ResolveTitle(manifest.id, title.Text, manifest.name ?? "<No Name>", LookupMainMenuText) is { } localizedTitle)
		{
			title.Text = localizedTitle;
		}

		if (container.GetNodeOrNull<MegaRichTextLabel>(InfoDescriptionNode) is { } description
			&& ResolveDescription(manifest.id, description.Text, manifest.description, LookupMainMenuText) is { } localizedDescription)
		{
			description.Text = localizedDescription;
		}
	}

	private static void WarnOnce(string where, Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("ui.mod-list-loc." + where, 1))
		{
			HextechLog.Warn(Tag, $"Mod list text kept as manifest text ({where}): {ex.GetType().Name}: {ex.Message}");
		}
	}

	/// <summary>
	/// 后缀：原版 <c>NModMenuRow._Ready</c> 在 Mod 非空时写入 Title 文本（0.107.1 / 0.111.0 一致）。
	/// <see cref="Priority.Low"/>：排在其他模组的后缀之后，看到他们改过的标签就不覆盖。
	/// </summary>
	[HarmonyPatch(typeof(NModMenuRow), nameof(NModMenuRow._Ready), new Type[0])]
	[HextechPatch("ui.mod-list-loc.row", Feature, Optional = true)]
	private static class ModMenuRowReadyPatch
	{
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Low)]
		private static void Postfix(NModMenuRow __instance)
		{
			try
			{
				ApplyRowTitle(__instance);
			}
			catch (Exception ex)
			{
				WarnOnce("row", ex);
			}
		}
	}

	/// <summary>
	/// 后缀：原版 <c>NModInfoContainer.Fill(Mod)</c> 同步写入标题与详情文本（0.107.1 / 0.111.0 一致）。优先级理由同上。
	/// </summary>
	[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill), typeof(Mod))]
	[HextechPatch("ui.mod-list-loc.info", Feature, Optional = true)]
	private static class ModInfoFillPatch
	{
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Low)]
		private static void Postfix(NModInfoContainer __instance, Mod mod)
		{
			try
			{
				ApplyInfo(__instance, mod);
			}
			catch (Exception ex)
			{
				WarnOnce("info", ex);
			}
		}
	}
}
