namespace Geld.Domain.Tests;

public class EuroTests
{
    [Theory]
    [InlineData(1351.82, "1.351,82 €")]
    [InlineData(0, "0,00 €")]
    [InlineData(518, "518,00 €")]
    [InlineData(-49.99, "−49,99 €")]
    [InlineData(1234567.5, "1.234.567,50 €")]
    public void Formats_amounts_the_German_way(decimal amount, string expected) =>
        Assert.Equal(expected, Euro.Format(amount));

    [Fact]
    public void Shows_the_direction_of_money() =>
        Assert.Equal(("+518,00 €", "−49,99 €"), (Euro.WithSign(518), Euro.WithSign(-49.99m)));

    [Theory]
    [InlineData("12,50", 12.5)]
    [InlineData("12.50", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("300", 300)]
    [InlineData(" 45 € ", 45)]
    [InlineData("0,01", 0.01)]
    public void Reads_amounts_typed_with_a_comma_or_a_dot(string text, decimal expected)
    {
        Assert.True(Euro.TryParse(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12,505")] // a third decimal is a typo, not a cent fraction
    [InlineData("1.234,56")] // thousands separators: ambiguous between comma and dot
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0,00")]
    public void Rejects_anything_else(string? text) =>
        Assert.False(Euro.TryParse(text, out _));

    [Fact]
    public void Writes_amounts_back_into_an_input_field_so_they_read_back_the_same()
    {
        var text = Euro.ToInput(1351.8m);

        Assert.Equal("1351,80", text);
        Assert.True(Euro.TryParse(text, out var amount));
        Assert.Equal(1351.8m, amount);
    }
}
