using AbleKit.Analysis;

namespace AbleKit.Tests.Analysis;

public sealed class ClipTests
{
    [Fact]
    public void A_new_clip_has_the_settings_of_a_clip_Live_has_just_saved()
    {
        var clip = new Clip();

        Assert.True(clip.LoopOn);
        Assert.True(clip.Sync);
        Assert.True(clip.HiQ);
        Assert.False(clip.Fade);
        Assert.Equal(1f, clip.SampleVolume);
        Assert.Equal(0f, clip.VelocityAmount);
        Assert.Equal(0f, clip.PitchCoarse);
        Assert.Equal(0f, clip.PitchFine);
        Assert.Equal(ClipColor.Unset, clip.Color);
        Assert.Equal(0, clip.LaunchMode);
        Assert.Equal(0, clip.LaunchQuantization);
        Assert.Equal(0, clip.LoopStart);
        Assert.Equal(0, clip.LoopEnd);
    }
}
