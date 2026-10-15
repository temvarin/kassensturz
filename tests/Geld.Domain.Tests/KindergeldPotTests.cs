namespace Geld.Domain.Tests;

public class KindergeldPotTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Fact]
    public void One_month_of_Kindergeld_is_259_euros_per_child()
    {
        var pot = new KindergeldPot();
        pot.Configure(children: 2, endDigit: 4);

        Assert.Equal(518m, pot.QuickAmount);
    }

    [Fact]
    public void The_balance_is_what_came_in_minus_what_went_out()
    {
        var pot = new KindergeldPot();
        pot.Receive(Today, 518);
        pot.Spend(Today, 49.99m, "Kleidung");
        pot.Spend(Today, 85, "Freizeit & Kurse");

        Assert.Equal((518m, 134.99m, 383.01m), (pot.Received, pot.Spent, pot.Balance));
    }

    [Fact]
    public void The_pot_can_go_negative()
    {
        var pot = new KindergeldPot();
        pot.Spend(Today, 120, "Kita & Schule");

        Assert.Equal(-120m, pot.Balance);
    }

    [Fact]
    public void Entries_are_newest_first_and_the_last_added_leads_within_a_day()
    {
        var pot = new KindergeldPot();
        var older = pot.Spend(Today.AddDays(-3), 10, "Spielzeug");
        var first = pot.Spend(Today, 20, "Spielzeug");
        var second = pot.Receive(Today, 518);

        Assert.Equal([second.Id, first.Id, older.Id], pot.Entries.Select(e => e.Id));
    }

    [Fact]
    public void A_spending_without_a_category_counts_as_other()
    {
        var pot = new KindergeldPot();

        Assert.Equal(KindergeldPot.DefaultCategory, pot.Spend(Today, 5, "  ").Category);
    }

    [Fact]
    public void An_entry_can_be_corrected_and_moves_to_its_new_date()
    {
        var pot = new KindergeldPot();
        var jacket = pot.Spend(Today.AddDays(-1), 49.99m, "Kleidung", "Winterjacke");
        var trip = pot.Spend(Today, 12, "Kita & Schule");

        pot.Update(jacket with { Amount = 59.99m, Date = Today.AddDays(1) });

        Assert.Equal([jacket.Id, trip.Id], pot.Entries.Select(e => e.Id));
        Assert.Equal(71.99m, pot.Spent);
    }

    [Fact]
    public void An_entry_can_be_removed()
    {
        var pot = new KindergeldPot();
        var mistake = pot.Receive(Today, 518);

        pot.Remove(mistake.Id);

        Assert.Empty(pot.Entries);
    }

    [Fact]
    public void Amounts_are_positive_the_kind_gives_the_direction() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new KindergeldPot().Spend(Today, -5, "Spielzeug"));

    [Fact]
    public void Spending_by_category_covers_the_period_and_ranks_the_largest_first()
    {
        var pot = new KindergeldPot();
        pot.Spend(new DateOnly(2026, 9, 30), 500, "Kleidung"); // before the period
        pot.Spend(new DateOnly(2026, 10, 1), 30, "Kleidung");
        pot.Spend(new DateOnly(2026, 10, 2), 85, "Freizeit & Kurse");
        pot.Spend(new DateOnly(2026, 10, 31), 30, "Kleidung");
        pot.Receive(new DateOnly(2026, 10, 12), 518); // money in is not spending

        var byCategory = pot.SpendingByCategory(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.Equal([new("Freizeit & Kurse", 85m), new CategoryTotal("Kleidung", 60m)], byCategory);
    }

    [Fact]
    public void Monthly_totals_cover_the_last_months_oldest_first_including_empty_ones()
    {
        var pot = new KindergeldPot();
        pot.Receive(new DateOnly(2026, 8, 12), 518);
        pot.Spend(new DateOnly(2026, 8, 20), 100, "Kleidung");
        pot.Receive(new DateOnly(2026, 10, 12), 518);

        var totals = pot.MonthlyTotals(Today, months: 3);

        Assert.Equal(
            [new(2026, 8, 518m, 100m), new(2026, 9, 0m, 0m), new MonthTotals(2026, 10, 518m, 0m)],
            totals);
    }

    [Fact]
    public void Monthly_totals_reach_back_across_the_new_year() =>
        Assert.Equal((2025, 11), new KindergeldPot().MonthlyTotals(new DateOnly(2026, 1, 5), 3).Select(t => (t.Year, t.Month)).First());

    [Fact]
    public void Suggests_the_preset_categories_then_the_users_own()
    {
        var pot = new KindergeldPot();
        pot.Spend(Today, 10, "Zoo");
        pot.Spend(Today, 10, "Bücher");
        pot.Spend(Today, 10, "Kleidung");

        Assert.Equal([.. KindergeldPot.PresetCategories, "Bücher", "Zoo"], pot.CategorySuggestions());
    }

    [Fact]
    public void The_next_payment_needs_the_endziffer()
    {
        var pot = new KindergeldPot();
        Assert.Null(pot.NextPayment(Today));

        pot.Configure(children: 1, endDigit: 4);
        Assert.Equal(new DateOnly(2026, 11, 11), pot.NextPayment(Today));
    }
}
