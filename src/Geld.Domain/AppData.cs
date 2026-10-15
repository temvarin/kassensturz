using System.Text.Json.Serialization;

namespace Geld.Domain;

/// <summary>Everything the user entered. Stored as one document, so a backup is one file. Persisted by <see cref="DataStore"/>.</summary>
public sealed class AppData
{
    /// <summary>How long a backup counts as recent; after that the app reminds the user to make one.</summary>
    public const int BackupReminderDays = 30;

    [JsonInclude] public KindergeldPot Kindergeld { get; internal set; } = new();
    [JsonInclude] public Budget Budget { get; internal set; } = new();

    /// <summary>When the user last exported a backup; null if never.</summary>
    [JsonInclude] public DateOnly? LastBackup { get; internal set; }

    [JsonIgnore]
    public bool IsEmpty => Kindergeld.Entries.Count == 0 && Budget.NetSalary == 0
        && Budget.Deductions.Count == 0 && Budget.Planned.Count == 0;

    /// <summary>
    /// Whether data exists only in the browser's storage, which the browser may delete without warning.
    /// The first backup is due as soon as there is data.
    /// </summary>
    public bool NeedsBackup(DateOnly today) =>
        !IsEmpty && (LastBackup is not { } last || today.DayNumber - last.DayNumber >= BackupReminderDays);

    public void MarkBackedUp(DateOnly today) => LastBackup = today;
}
