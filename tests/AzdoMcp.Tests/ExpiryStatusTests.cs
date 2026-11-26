using AzdoMcp.Core.Connections;

namespace AzdoMcp.Tests;

public class ExpiryStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static Connection With(int? daysLeft, int lead = 7, bool authFailed = false) => new()
    {
        Name = "X",
        PatExpires = daysLeft is { } d ? Today.AddDays(d) : null,
        ReminderDays = lead,
        LastAuthFailure = authFailed ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public void Expiring_within_lead_time_is_yellow()
    {
        var s = ExpiryClassifier.Classify(With(4), Today);
        Assert.Equal(StatusLevel.Warning, s.Level);
        Assert.Equal("in 4 Tagen", s.ExpiryText);
        Assert.Equal("PAT erneuern", s.StatusText);
    }

    [Fact]
    public void Beyond_lead_time_is_green()
    {
        var s = ExpiryClassifier.Classify(With(62), Today);
        Assert.Equal(StatusLevel.Ok, s.Level);
        Assert.Equal("in 62 Tagen", s.ExpiryText);
        Assert.Equal("OK", s.StatusText);
    }

    [Fact]
    public void Past_expiry_is_red()
    {
        var s = ExpiryClassifier.Classify(With(-1), Today);
        Assert.Equal(StatusLevel.Error, s.Level);
        Assert.Equal("abgelaufen", s.ExpiryText);
    }

    [Fact]
    public void Last_401_is_red()
    {
        var s = ExpiryClassifier.Classify(With(62, authFailed: true), Today);
        Assert.Equal(StatusLevel.Error, s.Level);
        Assert.Equal("401", s.StatusText);
    }

    [Theory]
    [InlineData(0, "heute")]
    [InlineData(1, "in 1 Tag")]
    [InlineData(7, "in 7 Tagen")]
    public void Texts(int days, string text) => Assert.Equal(text, ExpiryClassifier.ExpiryText(With(days), Today));

    [Fact]
    public void Unknown_expiry_is_green()
    {
        var s = ExpiryClassifier.Classify(With(null), Today);
        Assert.Equal(StatusLevel.Ok, s.Level);
        Assert.Equal("unbekannt", s.ExpiryText);
    }
}
