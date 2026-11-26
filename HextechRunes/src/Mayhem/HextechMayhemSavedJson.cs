using System.Text.Json;

namespace HextechRunes;

/// <summary>
/// 对局中枢写进存档属性的 JSON 统一用这一份序列化选项（Web 默认：camelCase 键名、读取时不区分大小写）。
/// 存档与联机状态比对都按这些字节算，换选项等于改存档格式。
/// </summary>
internal static class HextechMayhemSavedJson
{
	internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
