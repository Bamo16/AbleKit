using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class ClipColorTests
{
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 14, 13)]
    [InlineData(2, 1, 14)]
    [InlineData(5, 14, 69)]
    public void A_cell_is_numbered_row_by_row_from_the_top_left(int row, int column, int index) =>
        Assert.Equal(index, ClipColor.At(row, column).Index);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(6, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 15)]
    public void A_cell_outside_the_palette_is_refused(int row, int column) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipColor.At(row, column));

    [Fact]
    public void Unset_is_minus_one_and_is_what_a_color_nobody_set_is() =>
        Assert.Equal(
            [-1, -1, -1],
            [ClipColor.Unset.Index, default(ClipColor).Index, new Clip().Color.Index]
        );

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(69)]
    [InlineData(70)]
    [InlineData(1000)]
    public void Any_stored_index_is_kept_as_it_was_read(int index) =>
        Assert.Equal(index, ClipColor.FromIndex(index).Index);

    [Fact]
    public void A_cell_equals_the_color_read_from_its_index() =>
        Assert.Equal(ClipColor.At(3, 5), ClipColor.FromIndex(32));
}
