using System.Globalization;
using System.Windows.Media;
using Legend2Tool.WPF.Converters;
using Xunit;

namespace Legend2Tool.WPF.Tests;

public sealed class MonsterCountToRedBrushConverterTests
{
    [Fact]
    public void Convert_HigherMonsterCount_ReturnsDarkerRed()
    {
        var converter = new MonsterCountToRedBrushConverter();

        var lightBrush = Assert.IsType<SolidColorBrush>(converter.Convert(
            [250, 200],
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture
        ));
        var darkBrush = Assert.IsType<SolidColorBrush>(converter.Convert(
            [400, 200],
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture
        ));

        Assert.True(darkBrush.Color.R < lightBrush.Color.R);
        Assert.True(darkBrush.Color.G < lightBrush.Color.G);
        Assert.True(darkBrush.Color.B < lightBrush.Color.B);
        Assert.True(lightBrush.Color.R > lightBrush.Color.G);
        Assert.True(darkBrush.Color.R > darkBrush.Color.G);
    }
}
