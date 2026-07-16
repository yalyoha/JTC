using JTC.Services;

namespace JTC.Tests;

public class TorrentRestartPolicyTests
{
    [Fact]
    public void FreshPolicy_TryReserve_Returns5sAndIncrements()
    {
        var p = new TorrentRestartPolicy();
        var ok = p.TryReserveNextAttempt(out var delay);

        Assert.True(ok);
        Assert.Equal(TimeSpan.FromSeconds(5), delay);
        Assert.Equal(1, p.AttemptsUsed);
        Assert.False(p.IsExhausted);
    }

    [Fact]
    public void BackoffSchedule_Escalates_5s_15s_60s_60s_60s()
    {
        var p = new TorrentRestartPolicy();
        var expected = new[]
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
        };

        foreach (var e in expected)
        {
            var ok = p.TryReserveNextAttempt(out var delay);
            Assert.True(ok);
            Assert.Equal(e, delay);
        }

        Assert.Equal(TorrentRestartPolicy.MaxAttempts, p.AttemptsUsed);
        Assert.True(p.IsExhausted);
    }

    [Fact]
    public void AfterMaxAttempts_TryReserve_ReturnsFalseAndZero()
    {
        var p = new TorrentRestartPolicy();
        for (int i = 0; i < TorrentRestartPolicy.MaxAttempts; i++)
            p.TryReserveNextAttempt(out _);

        var ok = p.TryReserveNextAttempt(out var delay);

        Assert.False(ok);
        Assert.Equal(TimeSpan.Zero, delay);
        Assert.True(p.IsExhausted);
    }

    [Fact]
    public void RecordSuccess_ResetsCounterAndClearsFatal()
    {
        var p = new TorrentRestartPolicy();
        p.TryReserveNextAttempt(out _);
        p.TryReserveNextAttempt(out _);
        p.MarkFatal();

        Assert.True(p.IsExhausted);

        p.RecordSuccess();

        Assert.Equal(0, p.AttemptsUsed);
        Assert.False(p.IsExhausted);
        Assert.True(p.TryReserveNextAttempt(out var delay));
        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void MarkFatal_ExhaustsRegardlessOfBudget()
    {
        var p = new TorrentRestartPolicy();
        p.MarkFatal();

        Assert.True(p.IsExhausted);
        Assert.False(p.TryReserveNextAttempt(out var delay));
        Assert.Equal(TimeSpan.Zero, delay);
        Assert.Equal(0, p.AttemptsUsed); // no attempt consumed on refused reservation
    }

    [Fact]
    public void IsFatalException_Null_ReturnsFalse()
    {
        Assert.False(TorrentRestartPolicy.IsFatalException(null));
    }

    [Fact]
    public void IsFatalException_UnauthorizedAccess_ReturnsTrue()
    {
        Assert.True(TorrentRestartPolicy.IsFatalException(new UnauthorizedAccessException("no")));
    }

    [Fact]
    public void IsFatalException_DirectoryNotFound_ReturnsTrue()
    {
        Assert.True(TorrentRestartPolicy.IsFatalException(new DirectoryNotFoundException("missing")));
    }

    [Fact]
    public void IsFatalException_DiskFullByHResult_ReturnsTrue()
    {
        var ex = new IOException("not enough space on the disk")
        {
            HResult = unchecked((int)0x80070070),
        };
        Assert.True(TorrentRestartPolicy.IsFatalException(ex));
    }

    [Fact]
    public void IsFatalException_DiskFullByMessage_ReturnsTrue()
    {
        var ex = new IOException("There is not enough space on the disk.");
        Assert.True(TorrentRestartPolicy.IsFatalException(ex));
    }

    [Fact]
    public void IsFatalException_PlainIOException_ReturnsFalse()
    {
        var ex = new IOException("connection reset by peer");
        Assert.False(TorrentRestartPolicy.IsFatalException(ex));
    }

    [Fact]
    public void IsFatalException_GenericException_ReturnsFalse()
    {
        Assert.False(TorrentRestartPolicy.IsFatalException(new InvalidOperationException("foo")));
    }
}
