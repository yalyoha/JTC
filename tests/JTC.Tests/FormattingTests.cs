using JTC.Helpers;

namespace JTC.Tests;

public class FormattingTests
{
    // BytesToHuman

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.00 KB")]
    [InlineData(1536L, "1.50 KB")]
    [InlineData(1048576L, "1.00 MB")]
    [InlineData(1073741824L, "1.00 GB")]
    [InlineData(5_726_623_060L, "5.33 GB")]
    [InlineData(1_099_511_627_776L, "1.00 TB")]
    public void BytesToHuman_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, Formatting.BytesToHuman(bytes));
    }

    // RateToHuman

    [Fact]
    public void RateToHuman_Zero_ReturnsDash()
    {
        Assert.Equal("—", Formatting.RateToHuman(0));
    }

    [Theory]
    [InlineData(1L, "1 B/s")]
    [InlineData(2048L, "2.00 KB/s")]
    [InlineData(4_400_000L, "4.20 MB/s")]
    public void RateToHuman_FormatsCorrectly(long bytesPerSec, string expected)
    {
        Assert.Equal(expected, Formatting.RateToHuman(bytesPerSec));
    }

    // EtaToHuman

    [Fact]
    public void EtaToHuman_ZeroOrNegative_ReturnsDash()
    {
        Assert.Equal("—", Formatting.EtaToHuman(TimeSpan.Zero));
        Assert.Equal("—", Formatting.EtaToHuman(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void EtaToHuman_UnderOneDay_FormatsHhMmSs()
    {
        Assert.Equal("00:00:05", Formatting.EtaToHuman(TimeSpan.FromSeconds(5)));
        Assert.Equal("00:14:22", Formatting.EtaToHuman(new TimeSpan(0, 14, 22)));
        Assert.Equal("23:59:59", Formatting.EtaToHuman(new TimeSpan(23, 59, 59)));
    }

    [Fact]
    public void EtaToHuman_OverOneDay_FormatsWithDays()
    {
        Assert.Equal("1d 02:03:04", Formatting.EtaToHuman(new TimeSpan(1, 2, 3, 4)));
        Assert.Equal("10d 00:00:00", Formatting.EtaToHuman(TimeSpan.FromDays(10)));
    }

    [Fact]
    public void EtaToHuman_Infinity_ReturnsInfinitySymbol()
    {
        Assert.Equal("∞", Formatting.EtaToHuman(TimeSpan.MaxValue));
    }
}
