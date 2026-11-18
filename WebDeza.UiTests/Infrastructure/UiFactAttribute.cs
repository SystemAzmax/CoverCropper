namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// UI テスト（マウスとキーボードを実際に動かすテスト）に付ける。
/// 環境変数 WEBDEZA_UITEST=1 が無ければスキップする。普段の dotnet test や CI でマウスが動かないようにするため（設計書 6.5）
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        Skip = UiTestEnvironment.SkipReason(Environment.GetEnvironmentVariable(UiTestEnvironment.VariableName));
    }
}
