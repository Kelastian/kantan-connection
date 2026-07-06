using KantanConnect.Core.Security;

namespace KantanConnect.Core.Tests.Security;

public class PinGeneratorTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Generate_ReturnsStringWithExactRequestedLength(int length)
    {
        var pin = PinGenerator.Generate(length);

        Assert.Equal(length, pin.Length);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void Generate_ReturnsOnlyDigits(int length)
    {
        var pin = PinGenerator.Generate(length);

        Assert.All(pin, c => Assert.True(char.IsDigit(c)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Generate_LengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PinGenerator.Generate(length));
    }

    [Fact]
    public void Generate_ManyCalls_ProducesSomeVariation()
    {
        // No es una prueba de aleatoriedad criptográfica, solo un smoke test de que
        // no siempre devuelve el mismo valor fijo (lo que indicaría un bug obvio).
        var pins = Enumerable.Range(0, 50).Select(_ => PinGenerator.Generate(6)).ToHashSet();

        Assert.True(pins.Count > 1);
    }
}
