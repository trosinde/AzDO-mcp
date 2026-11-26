namespace AzdoMcp.Core.Connections;

/// <summary>Ampel: Ok = green, Warning = yellow, Error = red.</summary>
public enum StatusLevel
{
    Ok,
    Warning,
    Error,
}

/// <param name="Level">Ampel colour.</param>
/// <param name="ExpiryText">Text for "PAT läuft ab", e.g. "in 4 Tagen" or "abgelaufen".</param>
/// <param name="StatusText">Text for "Status", e.g. "OK", "PAT erneuern", "401".</param>
public sealed record ConnectionStatus(StatusLevel Level, string ExpiryText, string StatusText);

public static class ExpiryClassifier
{
    public static ConnectionStatus Classify(Connection c, DateOnly today, bool patReadable = true)
    {
        if (!patReadable)
        {
            return new(StatusLevel.Error, ExpiryText(c, today), SecretUnreadableException.ShortText);
        }

        if (c.PatExpires is { } expires && expires < today)
        {
            return new(StatusLevel.Error, "abgelaufen", c.LastAuthFailure is null ? "abgelaufen" : "401");
        }

        if (c.LastAuthFailure is not null)
        {
            return new(StatusLevel.Error, ExpiryText(c, today), "401");
        }

        if (c.PatExpires is { } exp && exp.DayNumber - today.DayNumber <= c.ReminderDays)
        {
            return new(StatusLevel.Warning, ExpiryText(c, today), "PAT erneuern");
        }

        return new(StatusLevel.Ok, ExpiryText(c, today), "OK");
    }

    public static string ExpiryText(Connection c, DateOnly today)
    {
        if (c.PatExpires is not { } expires)
        {
            return "unbekannt";
        }

        var days = expires.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => "abgelaufen",
            0 => "heute",
            1 => "in 1 Tag",
            _ => $"in {days} Tagen",
        };
    }

    /// <summary>Days until expiry, or null when unknown.</summary>
    public static int? DaysLeft(Connection c, DateOnly today) =>
        c.PatExpires is { } e ? e.DayNumber - today.DayNumber : null;
}
