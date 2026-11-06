using AtomUpdate.Core.Constants;

namespace AtomUpdate.Tests.Core;

/// <summary>展示时区解析：回落链完整、任何入参都不抛（无 tzdata 的精简镜像宿主曾让 DisplayClock 构造崩掉整条电路）。</summary>
public class DisplayTimeZoneTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("Asia/Shanghai")]
    public void Resolve_falls_back_to_east_eight_without_throwing(string? id)
    {
        Assert.Equal(TimeSpan.FromHours(8), DisplayTimeZone.Resolve(id).BaseUtcOffset);
    }

    [Fact]
    public void Resolve_uses_system_zone_when_present()
    {
        Assert.Equal(TimeSpan.Zero, DisplayTimeZone.Resolve("UTC").BaseUtcOffset);
    }

    [Fact]
    public void Resolve_handles_every_dropdown_option()
    {
        foreach (var (timeZoneId, _) in DisplayTimeZone.Common)
        {
            Assert.NotNull(DisplayTimeZone.Resolve(timeZoneId));
        }
    }

    [Fact]
    public void IsValid_rejects_blank_and_unknown_ids()
    {
        Assert.False(DisplayTimeZone.IsValid(null));
        Assert.False(DisplayTimeZone.IsValid("  "));
        Assert.False(DisplayTimeZone.IsValid("Mars/Olympus"));
    }
}
