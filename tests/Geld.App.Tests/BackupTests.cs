using Geld.App.Components;
using Geld.App.Pages;
using Geld.Domain;
using Geld.App.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Geld.App.Tests;

public class BackupTests : AppTestContext
{
    [Fact]
    public void The_reminder_appears_once_there_is_data_and_no_backup()
    {
        Assert.Empty(Render<BackupBanner>().FindAll(".banner"));

        Data.Kindergeld.Receive(Today, 518);

        Assert.Contains("Noch keine Sicherung", Render<BackupBanner>().Find(".banner").TextContent);
    }

    [Fact]
    public void Saving_a_backup_records_the_date_and_hides_the_reminder_for_30_days()
    {
        Data.Kindergeld.Receive(Today, 518);
        JSInterop.Setup<bool>("geld.saveFile", _ => true).SetResult(true);
        var settings = Render<Settings>();

        Button(settings, "Sicherung speichern").Click();

        Assert.Equal(Today, Stored.LastBackup);
        Assert.Empty(Render<BackupBanner>().FindAll(".banner"));
        Clock.Advance(TimeSpan.FromDays(30));
        Assert.Contains("vor 30 Tagen", Render<BackupBanner>().Find(".banner").TextContent);
    }

    [Fact]
    public void A_cancelled_backup_is_not_recorded()
    {
        Data.Kindergeld.Receive(Today, 518);
        JSInterop.Setup<bool>("geld.saveFile", _ => true).SetResult(false);

        Button(Render<Settings>(), "Sicherung speichern").Click();

        Assert.Null(Stored.LastBackup);
    }

    [Theory]
    [InlineData(true, 518)]
    [InlineData(false, 100)]
    public void Loading_a_backup_asks_before_replacing_the_data_on_this_device(bool replace, decimal expected)
    {
        Data.Kindergeld.Receive(Today, 100);
        Services.GetRequiredService<AppState>().Save();
        var backup = new AppData();
        backup.Kindergeld.Receive(Today, 518);
        var settings = Render<Settings>();
        // Answers the question as soon as it is asked, noting what was stored at that moment.
        (string Question, decimal Stored)? asked = null;
        Dialogs.Changed += () =>
        {
            if (Dialogs.Current is not { Cancel: not null } question) return;
            asked = (question.Message, Stored.Kindergeld.Received);
            Dialogs.Close(replace);
        };

        settings.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(DataStore.Serialize(backup), "backup.json"));

        Assert.Equal(("Alle Daten auf diesem Gerät durch die Sicherung ersetzen?", 100m), asked);
        Assert.Equal(expected, Stored.Kindergeld.Received);
    }

    [Fact]
    public void A_file_that_is_not_a_backup_is_refused()
    {
        Data.Kindergeld.Receive(Today, 100);
        var settings = Render<Settings>();

        settings.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("""{"photos":[]}""", "other.json"));

        Assert.Contains("keine Sicherung", Dialogs.Current?.Message);
        Assert.Equal(100m, Data.Kindergeld.Received);
    }
}
