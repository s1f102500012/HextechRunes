using System.Text;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 只校验结构：汉字片段按“CP437 字节 → 减去序号 → UTF-8”还原成 7 个汉字，防止误改其中某个符号。
	[HextechTest]
	private static void SignalNoiseHanFragmentsDecodeToSevenHanCharacters()
	{
		Encoding cp437 = GetCp437();
		Equal(12, HextechSignalNoise.HanFragments.Length, "han fragment count");
		for (int i = 0; i < HextechSignalNoise.HanFragments.Length; i++)
		{
			string fragment = HextechSignalNoise.HanFragments[i];
			Equal(21, fragment.Length, $"han fragment {i + 1} symbol count");
			byte[] bytes = cp437.GetBytes(fragment);
			Expect(bytes.All(static b => b >= 0x80), $"han fragment {i + 1} stays in the CP437 upper half");
			Expect(cp437.GetString(bytes) == fragment, $"han fragment {i + 1} round-trips through CP437");
			byte[] shifted = bytes.Select(b => (byte)(b - (i + 1))).ToArray();
			string decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(shifted);
			Equal(7, decoded.Length, $"han fragment {i + 1} decoded length");
			Expect(decoded.All(static c => c >= '\u4E00' && c <= '\u9FFF'), $"han fragment {i + 1} decodes to Han characters");
		}
	}

	// 只校验结构：拉丁片段落在 CP437 的 0x80–0xDF（全是可见符号），按“减 0x80、在 96 个可打印 ASCII 里回退序号位”还原成可读文本。
	[HextechTest]
	private static void SignalNoiseLatinFragmentsDecodeToPrintableText()
	{
		Encoding cp437 = GetCp437();
		Equal(12, HextechSignalNoise.LatinFragments.Length, "latin fragment count");
		for (int i = 0; i < HextechSignalNoise.LatinFragments.Length; i++)
		{
			string fragment = HextechSignalNoise.LatinFragments[i];
			byte[] bytes = cp437.GetBytes(fragment);
			Expect(cp437.GetString(bytes) == fragment, $"latin fragment {i + 1} round-trips through CP437");
			Expect(bytes.All(static b => b >= 0x80 && b <= 0xDF), $"latin fragment {i + 1} stays in the visible CP437 range");
			int shift = i + 1;
			string decoded = new string(bytes.Select(b => (char)(((b - 0x80 - shift) % 96 + 96) % 96 + 0x20)).ToArray());
			Expect(decoded.Length is >= 30 and <= 60, $"latin fragment {i + 1} length");
			Expect(char.IsAsciiLetterUpper(decoded[0]), $"latin fragment {i + 1} starts with a capital letter");
			Expect(decoded.Count(static c => c == ' ') >= 5, $"latin fragment {i + 1} decodes to spaced words");
			Expect(decoded.All(static c => char.IsAsciiLetter(c) || c is ' ' or ',' or '\'' or '-' or '?'), $"latin fragment {i + 1} decodes to plain text");
		}
	}

	private static Encoding GetCp437()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		return Encoding.GetEncoding(437);
	}

	[HextechTest]
	private static void SignalNoiseEchoIgnoresOrdinaryClipboardContent()
	{
		Expect(!HextechSignalNoise.TryEcho(null), "null clipboard");
		Expect(!HextechSignalNoise.TryEcho("   "), "blank clipboard");
		Expect(!HextechSignalNoise.TryEcho("HX1:not-a-real-share-code"), "share-code-like text");
		Expect(!HextechSignalNoise.TryEcho(new string('a', 65)), "overlong text");
		Expect(!HextechSignalNoise.TryEcho("HEXCFG?:Ⅶ:" + HextechSignalNoise.HanFragments[6]), "copied noise fragment");
	}
}
