using System.Text;
using Uberkarl.Behavior;

namespace Uberkarl.Editor;

/// <summary>Formats a playtest run's retained <see cref="QuarantinedSubject"/> records into the author-facing quarantine report block.</summary>
public static class QuarantineReportText
{
    private static readonly char[] LineBreakChars = { '\r', '\n' };

    /// <summary>The report block for <paramref name="quarantines"/>, or the empty string when there are none.</summary>
    /// <param name="quarantines">the run's retained quarantines, in the order they occurred</param>
    /// <returns>a header naming the count followed by one line per quarantine, or the empty string</returns>
    public static string Format(IReadOnlyList<QuarantinedSubject> quarantines)
    {
        if (quarantines.Count == 0)
            return string.Empty;

        StringBuilder text = new StringBuilder($"Behavior stopped ({quarantines.Count})");
        foreach (QuarantinedSubject quarantine in quarantines)
            text.Append('\n').Append(FormatLine(quarantine));

        return text.ToString();
    }

    private static string FormatLine(QuarantinedSubject quarantine)
    {
        BehaviorSubjectKind kind = BehaviorSubjectKinds.Parse(quarantine.Kind);
        string label = BehaviorSubjectLabel.Format(kind, quarantine.Name);
        string cell = kind == BehaviorSubjectKind.LevelScript ? string.Empty : $" ({quarantine.Cell.X}, {quarantine.Cell.Y})";
        string eventName = quarantine.TriggeringEvent is { } triggeringEvent ? BehaviorEventNames.ToVariableName(triggeringEvent) : "init";
        return $"{label}{cell} — {eventName}: {FirstLine(quarantine.Reason)}";
    }

    private static string FirstLine(string reason)
    {
        int lineBreak = reason.IndexOfAny(LineBreakChars);
        return lineBreak < 0 ? reason : reason[..lineBreak];
    }
}
