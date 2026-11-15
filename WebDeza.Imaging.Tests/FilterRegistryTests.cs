using WebDeza.Imaging.Filters;
using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Tests;

public class FilterRegistryTests
{
    [Fact]
    public void All_IsInRegistrationOrder()
    {
        var registry = new FilterRegistry();
        var first = new FakeFilter("sharpen");
        var basic = new BasicAdjustFilter();
        var last = new FakeFilter("mono");

        registry.Register(first);
        registry.Register(basic);
        registry.Register(last);

        Assert.Equal(new IImageFilter[] { first, basic, last }, registry.All);
    }

    [Fact]
    public void Register_SameId_Throws()
    {
        var registry = new FilterRegistry();
        registry.Register(new BasicAdjustFilter());

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFilter("basic")));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Register_EmptyIdOrNull_Throws()
    {
        var registry = new FilterRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeFilter("")));
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Find_ReturnsRegisteredFilterOrNull()
    {
        var registry = new FilterRegistry();
        var basic = new BasicAdjustFilter();
        registry.Register(basic);

        Assert.Same(basic, registry.Find("basic"));
        Assert.Null(registry.Find("Basic")); // 大文字小文字を区別する
        Assert.Null(registry.Find("unknown"));
    }

    [Fact]
    public void Register_DifferentCaseId_IsAllowed()
    {
        // Id は大文字小文字を区別するので、別のフィルターとして登録できる
        var registry = new FilterRegistry();
        registry.Register(new FakeFilter("mono"));
        registry.Register(new FakeFilter("Mono"));

        Assert.Equal(2, registry.All.Count);
    }

    private sealed class FakeFilter(string id) : IImageFilter
    {
        public string Id => id;
        public string DisplayName => id;
        public IReadOnlyList<ParameterDescriptor> Parameters => Array.Empty<ParameterDescriptor>();
        public bool IsIdentity(ParameterValues values) => true;
        public ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context) => source;
    }
}
