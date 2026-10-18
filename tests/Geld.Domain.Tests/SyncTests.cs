using System.Text.Json;

namespace Geld.Domain.Tests;

public class SyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    /// <summary>What <c>POST /api/sync</c> does with the documents, minus the database. Answers through JSON, like the network.</summary>
    private sealed class Server
    {
        public KindergeldPot Kindergeld { get; } = new();
        public Dictionary<string, Budget> Budgets { get; } = [];

        public SyncResponse Sync(string user, SyncRequest request)
        {
            var budget = Budgets.TryGetValue(user, out var b) ? b : Budgets[user] = new Budget();
            Kindergeld.Apply(request.Kindergeld);
            budget.Apply(request.Budget);
            return Network(new SyncResponse(Kindergeld, budget));
        }

        public void Sync(string user, AppData phone)
        {
            var pending = phone.StartSync(user);
            phone.CompleteSync(pending, Sync(user, Network(pending.Request)), Now);
        }
    }

    private static T Network<T>(T value)
    {
        var type = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)ApiJson.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(value, type), type)!;
    }

    private static string Json(KindergeldPot pot) => JsonSerializer.Serialize(pot, ApiJson.Default.KindergeldPot);
    private static string Json(Budget budget) => JsonSerializer.Serialize(budget, ApiJson.Default.Budget);

    [Fact]
    public void The_changes_are_what_was_added_edited_and_removed_since_the_base()
    {
        var before = new KindergeldPot();
        var kept = before.Receive(Today, 518);
        var edited = before.Spend(Today, 20, "Spielzeug");
        var removed = before.Spend(Today, 30, "Kleidung");
        var after = Network(before);
        var added = after.Spend(Today, 45, "Kita & Schule");
        after.Update(edited with { Amount = 25 });
        after.Remove(removed.Id);

        var changes = after.ChangesSince(before).Entries;

        Assert.Equal([added, edited with { Amount = 25 }], changes.Changed);
        Assert.Equal([removed.Id], changes.Removed);
        Assert.DoesNotContain(kept, changes.Changed);
    }

    [Fact]
    public void Settings_are_sent_only_when_they_changed()
    {
        var before = new KindergeldPot();
        var after = Network(before);
        Assert.Null(after.ChangesSince(before).Settings);

        after.Configure(children: 2, endDigit: 4);

        Assert.Equal(new KindergeldSettings(2, 4), after.ChangesSince(before).Settings);
    }

    [Fact]
    public void Receiving_the_same_changes_twice_changes_nothing_the_second_time()
    {
        var phone = new KindergeldPot();
        phone.Receive(Today, 518);
        var changes = phone.ChangesSince(new KindergeldPot());
        var server = new KindergeldPot();

        server.Apply(changes);
        var once = Json(server);
        server.Apply(changes); // a retry after a timeout, when the first request did arrive

        Assert.Equal(once, Json(server));
    }

    [Fact]
    public void Entries_added_on_two_phones_both_end_up_in_the_shared_pot()
    {
        var server = new Server();
        var anna = new AppData();
        var ben = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        var jacket = ben.Kindergeld.Spend(Today, 49.99m, "Kleidung", "Winterjacke");

        server.Sync("anna", anna);
        server.Sync("ben", ben); // based on a pot that had nothing of Anna's
        server.Sync("anna", anna);

        Assert.Equal([jacket, payment], server.Kindergeld.Entries);
        Assert.Equal(server.Kindergeld.Entries, anna.Kindergeld.Entries);
        Assert.Equal(server.Kindergeld.Entries, ben.Kindergeld.Entries);
    }

    [Fact]
    public void When_two_phones_change_the_same_entry_the_change_the_server_receives_last_wins()
    {
        var server = new Server();
        var anna = new AppData();
        var ben = new AppData();
        var jacket = anna.Kindergeld.Spend(Today, 49.99m, "Kleidung");
        server.Sync("anna", anna);
        server.Sync("ben", ben);

        ben.Kindergeld.Update(jacket with { Amount = 59.99m }); // Ben's phone has the wrong time; that doesn't matter
        anna.Kindergeld.Update(jacket with { Amount = 39.99m });
        server.Sync("ben", ben);
        server.Sync("anna", anna);
        server.Sync("ben", ben);

        Assert.Equal(39.99m, Assert.Single(server.Kindergeld.Entries).Amount);
        Assert.Equal(39.99m, Assert.Single(ben.Kindergeld.Entries).Amount);
    }

    [Fact]
    public void An_entry_deleted_on_one_phone_comes_back_when_the_other_phone_edits_it_afterwards()
    {
        // The known gap of "last write wins": the edit arrives last, so it wins over the deletion.
        var server = new Server();
        var anna = new AppData();
        var ben = new AppData();
        var shoes = anna.Kindergeld.Spend(Today, 35, "Kleidung", "Schuhe");
        server.Sync("anna", anna);
        server.Sync("ben", ben);

        anna.Kindergeld.Remove(shoes.Id);
        ben.Kindergeld.Update(shoes with { Amount = 39 });
        server.Sync("anna", anna);
        server.Sync("ben", ben);

        Assert.Equal(39m, Assert.Single(server.Kindergeld.Entries).Amount);
    }

    [Fact]
    public void What_one_phone_removes_disappears_from_the_others()
    {
        var server = new Server();
        var anna = new AppData();
        var ben = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        var rent = anna.Budget.AddDeduction("Miete", 1150, 1);
        var gift = anna.Budget.Plan(2026, 10, "Geschenk", 30);
        server.Sync("anna", anna);
        server.Sync("ben", ben);
        var tablet = new AppData();
        server.Sync("anna", tablet);

        anna.Kindergeld.Remove(payment.Id);
        anna.Budget.RemoveDeduction(rent.Id);
        anna.Budget.RemovePlanned(gift.Id);
        server.Sync("anna", anna);
        server.Sync("ben", ben);
        server.Sync("anna", tablet);

        Assert.Empty(ben.Kindergeld.Entries);
        Assert.Empty(tablet.Kindergeld.Entries);
        Assert.Empty(tablet.Budget.Deductions);
        Assert.Empty(tablet.Budget.Planned);
    }

    [Fact]
    public void Removing_an_entry_the_server_no_longer_has_does_nothing()
    {
        var server = new KindergeldPot();
        server.Receive(Today, 518);

        server.Apply(new KindergeldChanges(null, new ItemChanges<Entry>([], [Guid.NewGuid()])));

        Assert.Single(server.Entries);
    }

    [Fact]
    public void Entries_from_the_other_phone_take_their_usual_place_newest_first()
    {
        var server = new KindergeldPot();
        var october = server.Receive(Today.AddDays(-2), 518);
        var phone = new KindergeldPot();
        var shoes = phone.Spend(Today, 35, "Kleidung");
        var september = phone.Receive(Today.AddMonths(-1), 518);

        server.Apply(phone.ChangesSince(new KindergeldPot()));

        Assert.Equal([shoes, october, september], server.Entries);
    }

    [Fact]
    public void The_server_checks_changes_with_the_same_rules_and_takes_none_of_an_invalid_request()
    {
        var server = new KindergeldPot();
        var invalid = new KindergeldChanges(new KindergeldSettings(3, null), new ItemChanges<Entry>(
            [new Entry(Guid.NewGuid(), EntryKind.Payment, Today, 518, null, null),
             new Entry(Guid.NewGuid(), EntryKind.Spending, Today, -50, "Kleidung", null)], []));

        Assert.ThrowsAny<ArgumentException>(() => server.Apply(invalid));

        Assert.Equal(Json(new KindergeldPot()), Json(server));
    }

    [Fact]
    public void Each_budget_stays_with_its_owner()
    {
        var server = new Server();
        var anna = new AppData();
        var ben = new AppData();
        anna.Budget.SetSalary(2850);
        ben.Budget.SetSalary(3100);
        anna.Kindergeld.Receive(Today, 518);

        server.Sync("anna", anna);
        server.Sync("ben", ben);

        Assert.Equal(2850m, server.Budgets["anna"].NetSalary);
        Assert.Equal(3100m, ben.Budget.NetSalary);
        Assert.Single(ben.Kindergeld.Entries);
    }

    [Fact]
    public void Budget_changes_keep_the_budget_s_order_and_send_the_salary_only_when_it_changed()
    {
        var before = new Budget();
        before.SetSalary(2850);
        var phone = Network(before);
        var gym = phone.AddDeduction("Fitnessstudio", 29.90m, 28);
        var makeUp = phone.Plan(2026, 10, "Make-up", 45);
        var server = new Budget();
        var rent = server.AddDeduction("Miete", 1150, 1);
        var gift = server.Plan(2026, 10, "Geschenk", 30);

        var changes = phone.ChangesSince(before);
        server.Apply(changes);

        Assert.Null(changes.NetSalary);
        Assert.Equal([rent, gym], server.Deductions); // by day
        Assert.Equal([gift, makeUp], server.Planned); // in the order added
    }

    [Fact]
    public void The_first_sync_of_a_phone_adds_its_data_to_the_server_without_removing_anything()
    {
        var server = new Server();
        var anna = new AppData();
        var payment = anna.Kindergeld.Receive(Today, 518);
        server.Sync("anna", anna);
        var ben = new AppData(); // used without an account until now
        var jacket = ben.Kindergeld.Spend(Today, 49.99m, "Kleidung");

        server.Sync("ben", ben);

        Assert.Equal([jacket, payment], ben.Kindergeld.Entries);
    }

    [Fact]
    public void A_change_made_while_a_sync_is_under_way_is_kept_and_sent_with_the_next_sync()
    {
        var server = new Server();
        var phone = new AppData();
        phone.Kindergeld.Receive(Today, 518);
        var pending = phone.StartSync("anna");
        var shoes = phone.Kindergeld.Spend(Today, 35, "Kleidung");

        phone.CompleteSync(pending, server.Sync("anna", pending.Request), Now);

        Assert.Contains(shoes, phone.Kindergeld.Entries);
        Assert.True(phone.HasUnsyncedChanges);
        server.Sync("anna", phone);
        Assert.Contains(shoes, server.Kindergeld.Entries);
        Assert.False(phone.HasUnsyncedChanges);
    }

    [Fact]
    public void An_edit_undone_while_a_sync_is_under_way_is_undone_on_the_server_too()
    {
        var server = new Server();
        var phone = new AppData();
        var payment = phone.Kindergeld.Receive(Today, 518);
        server.Sync("anna", phone);
        phone.Kindergeld.Update(payment with { Amount = 259 });
        var pending = phone.StartSync("anna");
        phone.Kindergeld.Update(payment);

        phone.CompleteSync(pending, server.Sync("anna", pending.Request), Now);
        server.Sync("anna", phone);

        Assert.Equal(518m, Assert.Single(server.Kindergeld.Entries).Amount);
        Assert.Equal(518m, Assert.Single(phone.Kindergeld.Entries).Amount);
    }

    [Fact]
    public void A_sync_remembers_the_account_and_when_it_happened()
    {
        var phone = new AppData();
        Assert.True(phone.HasUnsyncedChanges is false && phone.Sync is null);

        new Server().Sync("anna", phone);

        Assert.Equal(("anna", Now), (phone.Sync?.UserId, phone.Sync?.LastSync));
    }

    [Fact]
    public void Data_synced_with_one_account_is_never_synced_with_another()
    {
        var phone = new AppData();
        var server = new Server();
        server.Sync("anna", phone);

        Assert.Throws<InvalidOperationException>(() => phone.StartSync("ben"));
    }

    [Fact]
    public void A_backup_restored_after_syncing_is_added_to_the_server_instead_of_removing_what_it_lacks()
    {
        var server = new Server();
        var phone = new AppData();
        var backup = DataStore.TryRead(DataStore.Backup(phone, Today))!; // made while the pot was still empty
        var payment = phone.Kindergeld.Receive(Today, 518);
        server.Sync("anna", phone);
        var shoes = backup.Kindergeld.Spend(Today, 35, "Kleidung");

        phone.RestoreFrom(backup);
        server.Sync("anna", phone);

        Assert.Equal([shoes, payment], server.Kindergeld.Entries);
        Assert.Equal("anna", phone.Sync?.UserId);
    }

    [Fact]
    public void The_sync_state_is_stored_with_the_data_but_never_in_a_backup()
    {
        var phone = new AppData();
        phone.Kindergeld.Receive(Today, 518);
        new Server().Sync("anna", phone);

        var stored = DataStore.TryRead(DataStore.Serialize(phone));
        var backup = DataStore.TryRead(DataStore.Backup(phone, Today));

        Assert.Equal("anna", stored?.Sync?.UserId);
        Assert.False(stored?.HasUnsyncedChanges);
        Assert.Null(backup?.Sync);
        Assert.NotNull(phone.Sync);
    }

    [Fact]
    public void A_request_says_which_account_it_is_for()
    {
        Assert.Equal("anna", new AppData().StartSync("anna").Request.UserId);
    }

    [Fact]
    public void An_entry_deleted_after_a_sync_whose_answer_was_lost_stays_deleted()
    {
        // The server saved the entry, but the answer never arrived: the phone can't know whether it was saved.
        var server = new Server();
        var phone = new AppData();
        var shoes = phone.Kindergeld.Spend(Today, 35, "Kleidung");
        var lost = phone.StartSync("anna");
        server.Sync("anna", Network(lost.Request));
        phone.SyncFailed(lost);
        phone.Kindergeld.Remove(shoes.Id); // while waiting for the retry

        phone = DataStore.TryRead(DataStore.Serialize(phone))!; // the app may also have been restarted meanwhile
        server.Sync("anna", phone);

        Assert.Empty(server.Kindergeld.Entries);
        Assert.Empty(phone.Kindergeld.Entries);
        Assert.False(phone.HasUnsyncedChanges);
    }

    [Fact]
    public void Settings_and_edits_undone_after_a_lost_answer_are_undone_on_the_server_too()
    {
        var server = new Server();
        var phone = new AppData();
        var payment = phone.Kindergeld.Receive(Today, 518);
        phone.Budget.SetSalary(2850);
        server.Sync("anna", phone);
        phone.Kindergeld.Update(payment with { Amount = 259 });
        phone.Kindergeld.Configure(children: 2, endDigit: 4);
        phone.Budget.SetSalary(3000);
        var lost = phone.StartSync("anna");
        server.Sync("anna", Network(lost.Request));
        phone.SyncFailed(lost);
        phone.Kindergeld.Update(payment);
        phone.Kindergeld.Configure(children: 1, endDigit: null);
        phone.Budget.SetSalary(2850);

        server.Sync("anna", phone);

        Assert.Equal(518m, Assert.Single(server.Kindergeld.Entries).Amount);
        Assert.Equal((1, (int?)null), (server.Kindergeld.Children, server.Kindergeld.EndDigit));
        Assert.Equal(2850m, server.Budgets["anna"].NetSalary);
    }

    [Fact]
    public void A_sync_state_after_a_lost_answer_is_never_in_a_backup()
    {
        var phone = new AppData();
        phone.Kindergeld.Receive(Today, 518);
        phone.SyncFailed(phone.StartSync("anna"));

        Assert.True(phone.HasUnsyncedChanges);
        Assert.Null(DataStore.TryRead(DataStore.Backup(phone, Today))?.Unconfirmed);
    }

    [Fact]
    public void Phones_that_edit_and_sync_in_any_order_end_up_with_the_same_data_as_the_server()
    {
        // Three devices of two people edit at random and sync in random order, also editing while a sync is under way.
        // Whatever happens, once everyone has synced twice in a row, every device has exactly what the server has.
        for (var seed = 0; seed < 50; seed++)
        {
            var random = new Random(seed);
            var server = new Server();
            var devices = new[] { ("anna", new AppData()), ("anna", new AppData()), ("ben", new AppData()) };
            var inFlight = new (PendingSync Sent, SyncResponse Answer)?[devices.Length];

            for (var step = 0; step < 200; step++)
            {
                var d = random.Next(devices.Length);
                var (user, phone) = devices[d];
                switch (random.Next(3))
                {
                    case 0 when inFlight[d] is null:
                        var pending = phone.StartSync(user);
                        inFlight[d] = (pending, server.Sync(user, Network(pending.Request)));
                        break;
                    case 1 when inFlight[d] is { } sync:
                        if (random.Next(4) == 0) phone.SyncFailed(sync.Sent); // the answer got lost
                        else phone.CompleteSync(sync.Sent, sync.Answer, Now);
                        inFlight[d] = null;
                        break;
                    default:
                        Edit(phone, random);
                        break;
                }
            }
            for (var d = 0; d < devices.Length; d++)
            {
                if (inFlight[d] is { } sync) devices[d].Item2.CompleteSync(sync.Sent, sync.Answer, Now);
            }
            for (var round = 0; round < 2; round++)
            {
                foreach (var (user, phone) in devices) server.Sync(user, phone);
            }

            foreach (var (user, phone) in devices)
            {
                Assert.Equal(Json(server.Kindergeld), Json(phone.Kindergeld));
                Assert.Equal(Json(server.Budgets[user]), Json(phone.Budget));
                Assert.False(phone.HasUnsyncedChanges);
            }
        }
    }

    private static void Edit(AppData phone, Random random)
    {
        var pot = phone.Kindergeld;
        var budget = phone.Budget;
        var amount = random.Next(1, 500);
        var date = Today.AddDays(-random.Next(60));
        switch (random.Next(9))
        {
            case 0: pot.Receive(date, amount); break;
            case 1: pot.Spend(date, amount, "Kleidung"); break;
            case 2 when pot.Entries.Count > 0: pot.Update(pot.Entries[random.Next(pot.Entries.Count)] with { Amount = amount, Date = date }); break;
            case 3 when pot.Entries.Count > 0: pot.Remove(pot.Entries[random.Next(pot.Entries.Count)].Id); break;
            case 4: pot.Configure(random.Next(1, 4), random.Next(10)); break;
            case 5: budget.SetSalary(amount * 10); break;
            case 6: budget.AddDeduction("Miete", amount, random.Next(1, 32)); break;
            case 7 when budget.Deductions.Count > 0: budget.RemoveDeduction(budget.Deductions[random.Next(budget.Deductions.Count)].Id); break;
            default: budget.Plan(2026, random.Next(10, 13), "Geschenk", amount); break;
        }
    }
}
