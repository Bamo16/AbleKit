using AbleKit.Tags;

namespace AbleKit.Tests.Tags;

public sealed class KeyTagsTests
{
    [Fact]
    public void The_tonics_are_the_twelve_notes_with_sharps_spelled_with_the_sharp_sign()
    {
        string[] expected = ["A", "A♯", "B", "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯"];

        Assert.Equal(expected, KeyTags.Tonics.Order(StringComparer.Ordinal));
        Assert.All(
            KeyTags.Tonics.Where(tonic => tonic.Length > 1),
            tonic => Assert.Equal('♯', tonic[1])
        );
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("D♭")]
    [InlineData("c")]
    public void A_tonic_spelled_another_way_is_not_one_of_Lives(string spelling) =>
        Assert.DoesNotContain(spelling, KeyTags.Tonics);

    [Fact]
    public void The_modes_are_Major_and_Minor_and_case_matters()
    {
        Assert.Equal(["Major", "Minor"], KeyTags.Modes.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("major", KeyTags.Modes);
    }
}
