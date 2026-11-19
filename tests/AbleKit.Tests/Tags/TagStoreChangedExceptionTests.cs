using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

public sealed class TagStoreChangedExceptionTests
{
    [Fact]
    public void Without_a_message_it_says_the_store_changed_and_is_an_IOException()
    {
        var ex = new TagStoreChangedException();

        Assert.IsType<IOException>(ex, exactMatch: false);
        Assert.Equal("The tag store changed while it was being read or written.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void A_message_and_an_inner_exception_are_kept()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TagStoreChangedException("mid-write", inner);

        Assert.Equal("mid-write", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void A_message_alone_is_kept_with_no_inner_exception()
    {
        var ex = new TagStoreChangedException("gone");

        Assert.Equal("gone", ex.Message);
        Assert.Null(ex.InnerException);
    }
}
