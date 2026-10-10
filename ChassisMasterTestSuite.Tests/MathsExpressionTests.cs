using Chassis_Master_Test_Suite.Analysis;

namespace ChassisMasterTestSuite.Tests;

public class MathsExpressionTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("10/2-1", 4)]
    [InlineData("-3+5", 2)]
    [InlineData("2*(3+4)/2", 7)]
    public void Evaluate_Arithmetic(string expr, double expected)
    {
        var v = MathsExpression.Evaluate(expr, _ => 0);
        Assert.Equal(expected, v, 9);
    }

    [Fact]
    public void Evaluate_ChannelNames()
    {
        double Get(string id) => id.ToLowerInvariant() switch
        {
            "velocity" => 100,
            "longacc" => 2,
            _ => 0
        };

        Assert.Equal(200, MathsExpression.Evaluate("velocity * Longacc", Get), 9);
        Assert.Equal(50, MathsExpression.Evaluate("(velocity + 0) / 2", Get), 9);
    }

    [Fact]
    public void ExtractChannelNames_Order()
    {
        var names = MathsExpression.ExtractChannelNames("velocity + Latacc * velocity");
        Assert.Equal(new[] { "velocity", "Latacc" }, names);
    }

    [Fact]
    public void DivideByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() =>
            MathsExpression.Evaluate("1/0", _ => 0));
    }

    [Fact]
    public void TryAdd_Store()
    {
        var store = new MathsChannelStore();
        Assert.True(store.TryAdd(new MathsChannelDefinition
        {
            Id = "v2",
            Expression = "velocity * 2",
            DisplayName = "V*2"
        }, out _));
        Assert.False(store.TryAdd(new MathsChannelDefinition
        {
            Id = "v2",
            Expression = "1"
        }, out var err));
        Assert.Contains("already exists", err);
    }

    [Fact]
    public void TryReplaceAll_And_SanitizeId()
    {
        var store = new MathsChannelStore();
        Assert.Equal("Speed", MathsChannelStore.SanitizeId("Speed."));
        Assert.Equal("V_2", MathsChannelStore.SanitizeId("V 2"));
        Assert.True(store.TryReplaceAll(new[]
        {
            new MathsChannelDefinition
            {
                Id = "Speed",
                Expression = "velocity * 1.16",
                DisplayName = "Speed.",
                Unit = "km/h"
            }
        }, out _));
        Assert.Single(store.Definitions);
        Assert.Equal("km/h", store.Definitions[0].Unit);
        Assert.True(store.TryReplaceAll(Array.Empty<MathsChannelDefinition>(), out _));
        Assert.Empty(store.Definitions);
    }

    [Fact]
    public void AllocateUniqueId_AvoidsCollision()
    {
        var id = MathsChannelStore.AllocateUniqueId("Maths", new[] { "Maths", "Maths_2" });
        Assert.Equal("Maths_3", id);
    }
}
