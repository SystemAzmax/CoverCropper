using System.Text.Json;
using WebDeza.Imaging.Parameters;
using WebDeza.Services;

namespace WebDeza.Tests.Services;

public class ParameterValuesJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ParameterValuesJsonConverter() } };

    [Fact]
    public void Write_IsPlainObjectInKeyOrder()
    {
        var values = ParameterValues.Empty.With("quality", 85).With("interlace", false).With("amount", 0.25).With("mode", "zip");

        var json = JsonSerializer.Serialize(values, Options);

        Assert.Equal("""{"amount":0.25,"interlace":false,"mode":"zip","quality":85}""", json);
    }

    [Fact]
    public void Read_NumbersBoolsAndStrings()
    {
        var values = JsonSerializer.Deserialize<ParameterValues>("""{"quality":85,"amount":0.25,"big":1e3,"on":true,"mode":"zip"}""", Options)!;

        Assert.IsType<long>(values["quality"]);
        Assert.IsType<double>(values["amount"]);
        Assert.Equal(1000, values.Get<int>("big"));
        Assert.True(values.Get<bool>("on"));
        Assert.Equal("zip", values.Get<string>("mode"));
    }

    [Fact]
    public void Read_ValuesThatCannotBeParameters_AreSkipped()
    {
        var values = JsonSerializer.Deserialize<ParameterValues>("""{"a":null,"b":[1,{"x":2}],"c":{"d":[3]},"e":1}""", Options)!;

        Assert.Equal(ParameterValues.Empty.With("e", 1), values);
    }

    [Fact]
    public void RoundTrip_KeepsEqualValues()
    {
        var values = ParameterValues.Empty.With("quality", 85).With("scale", 1.0).With("ratio", 1.5).With("on", true).With("name", "表紙");

        var back = JsonSerializer.Deserialize<ParameterValues>(JsonSerializer.Serialize(values, Options), Options);

        Assert.Equal(values, back); // 1.0 が 1 になっても同じ値
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    [InlineData("{\"a\":")]
    public void Read_NotAnObject_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ParameterValues>(json, Options));
    }

    [Fact]
    public void Read_Null_IsNull()
    {
        Assert.Null(JsonSerializer.Deserialize<ParameterValues>("null", Options));
    }
}
