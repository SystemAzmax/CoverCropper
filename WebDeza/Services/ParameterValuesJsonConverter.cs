using System.Text.Json;
using System.Text.Json.Serialization;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Services;

/// <summary>
/// ParameterValues を「キー → 数値・真偽・文字列」の JSON オブジェクトで読み書きする（設計書 F-12）。
/// 例: { "quality": 85, "interlace": false }
/// </summary>
/// <remarks>
/// 読むときは、小数部の無い数値を整数（long）、それ以外の数値を実数（double）にする。
/// 値にできないもの（配列・オブジェクト・null）はそのキーごと捨てる（その項目は検証で既定値になる）。
/// </remarks>
public sealed class ParameterValuesJsonConverter : JsonConverter<ParameterValues>
{
    public override ParameterValues? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("パラメーターの値は JSON オブジェクトで書きます。");

        var values = ParameterValues.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return values;

            string key = reader.GetString() ?? "";
            reader.Read();
            object? value = reader.TokenType switch
            {
                JsonTokenType.Number when reader.TryGetInt64(out long l) => l,
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String => reader.GetString(),
                _ => null,
            };
            if (value is null)
            {
                reader.Skip(); // 配列・オブジェクトは中身ごと読み飛ばす
                continue;
            }
            if (key.Length > 0) values = values.With(key, value);
        }
        throw new JsonException("パラメーターの値の JSON が途中で終わっています。");
    }

    public override void Write(Utf8JsonWriter writer, ParameterValues value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var key in value.Keys) // 文字コード順なので、書くたびに順番がぶれない
        {
            switch (value[key])
            {
                case long l: writer.WriteNumber(key, l); break;
                case double d: writer.WriteNumber(key, d); break;
                case bool b: writer.WriteBoolean(key, b); break;
                case string s: writer.WriteString(key, s); break;
            }
        }
        writer.WriteEndObject();
    }
}
