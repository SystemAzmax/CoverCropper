using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Formats;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Mcp.Tools;

/// <summary>形式 1 つ（list_formats の結果。設計書 F-14）</summary>
/// <param name="Id">形式の ID（save_image などで指定する）</param>
/// <param name="Name">表示名</param>
/// <param name="Extensions">拡張子（先頭が保存するときの拡張子）</param>
/// <param name="CanOpen">開ける</param>
/// <param name="CanSave">保存できる</param>
/// <param name="KeepsTransparency">透過を保てる（保てない形式は、背景色で塗って保存する）</param>
/// <param name="SizeTuningOption">ファイルサイズの上限を指定したときに調整するオプションのキー。上限を使えない形式は null</param>
/// <param name="Requirement">読み書きに要る OS の拡張機能など。要らなければ null</param>
/// <param name="Options">保存するときのオプション（保存できない形式は空）</param>
public sealed record FormatInfo(
    string Id, string Name, IReadOnlyList<string> Extensions, bool CanOpen, bool CanSave, bool KeepsTransparency,
    string? SizeTuningOption, string? Requirement, IReadOnlyList<ParameterInfo> Options);

/// <summary>フィルター 1 つ（list_filters の結果。設計書 F-14）</summary>
/// <param name="Id">フィルターの ID（apply_filter で指定する）</param>
/// <param name="Name">表示名</param>
/// <param name="Parameters">パラメーター（無いフィルターは空）</param>
public sealed record FilterInfo(string Id, string Name, IReadOnlyList<ParameterInfo> Parameters);

/// <summary>パラメーター 1 つ（フィルターのパラメーターと、形式のオプションで共用）</summary>
/// <param name="Key">値を渡すときのキー</param>
/// <param name="Name">表示名</param>
/// <param name="Kind">種類（integer / number / boolean / choice / color）</param>
/// <param name="Default">既定値</param>
/// <param name="Min">最小（integer・number のとき）</param>
/// <param name="Max">最大（integer・number のとき）</param>
/// <param name="Unit">単位（"px"・"%" など。無ければ null）</param>
/// <param name="Choices">選択肢（choice のとき）</param>
public sealed record ParameterInfo(
    string Key, string Name, string Kind, object Default, double? Min, double? Max, string? Unit,
    IReadOnlyList<ChoiceInfo>? Choices);

/// <summary>選択肢 1 つ</summary>
/// <param name="Value">値として渡す文字列</param>
/// <param name="Name">表示名</param>
public sealed record ChoiceInfo(string Value, string Name);

/// <summary>エンジンの記述を、MCP の結果の形にする</summary>
public static class CatalogInfo
{
    public static FormatInfo Describe(IImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var encoder = format.Capabilities.HasFlag(FormatCapabilities.Encode) ? format.Encoder : null;

        return new FormatInfo(
            format.Id,
            format.DisplayName,
            format.Extensions,
            CanOpen: format.Capabilities.HasFlag(FormatCapabilities.Decode),
            CanSave: encoder is not null,
            KeepsTransparency: format.Capabilities.HasFlag(FormatCapabilities.Alpha),
            SizeTuningOption: encoder?.SizeTuning?.ParameterKey,
            format.Requirement,
            encoder?.Options.Select(Describe).ToArray() ?? []);
    }

    public static FilterInfo Describe(IImageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new FilterInfo(filter.Id, filter.DisplayName, filter.Parameters.Select(Describe).ToArray());
    }

    public static ParameterInfo Describe(ParameterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        bool hasRange = descriptor.Kind is ParameterKind.Integer or ParameterKind.Number;

        return new ParameterInfo(
            descriptor.Key,
            descriptor.DisplayName,
            KindName(descriptor.Kind),
            descriptor.Coerce(descriptor.Default), // 型をそろえた既定値（Integer は long、Number は double）
            hasRange ? descriptor.Min : null,
            hasRange ? descriptor.Max : null,
            descriptor.Unit,
            descriptor.Kind == ParameterKind.Choice
                ? descriptor.Choices?.Select(c => new ChoiceInfo(c.Value, c.DisplayName)).ToArray() ?? []
                : null);
    }

    /// <summary>種類の名前（MCP の結果に書く形）</summary>
    public static string KindName(ParameterKind kind) => kind switch
    {
        ParameterKind.Integer => "integer",
        ParameterKind.Number => "number",
        ParameterKind.Boolean => "boolean",
        ParameterKind.Choice => "choice",
        ParameterKind.Color => "color",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
