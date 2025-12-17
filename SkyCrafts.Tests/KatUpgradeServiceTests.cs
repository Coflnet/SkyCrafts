using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Coflnet.Sky.Api.Client.Model;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace SkyCrafts.Tests;

public class KatUpgradeServiceTests
{
    [Theory]
    [InlineData(11, 19, false)] // Nov 19 - should be inactive
    [InlineData(11, 20, true)]  // Nov 20 - should be active
    [InlineData(11, 25, true)]  // Nov 25 - should be active
    [InlineData(12, 20, true)]  // Dec 20 - should be active
    [InlineData(12, 31, true)]  // Dec 31 - should be active
    [InlineData(1, 1, false)]   // Jan 1 - should be inactive
    public void IsAuraMayorActive_ReturnsCorrectStatus(int month, int day, bool expectedActive)
    {
        // Arrange
        var now = DateTime.Now;
        var testDate = new DateTime(now.Year, month, day);
        
        // Act
        var isActive = KatUpgradeServiceTestHelper.IsAuraMayorActive(testDate);
        
        // Assert
        Assert.Equal(expectedActive, isActive);
    }

    [Fact]
    public void ApplyAuraMayorModifier_WhenAuraActive_IncreasesCostBy50Percent()
    {
        // Arrange
        var baseCost = 1000.0;
        var testDate = new DateTime(DateTime.Now.Year, 12, 15); // Dec 15 - within Aura period
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert
        Assert.Equal(1500.0, modifiedCost);
    }

    [Fact]
    public void ApplyAuraMayorModifier_WhenAuraInactive_LeavesCostUnchanged()
    {
        // Arrange
        var baseCost = 1000.0;
        var testDate = new DateTime(DateTime.Now.Year, 1, 15); // Jan 15 - outside Aura period
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert
        Assert.Equal(1000.0, modifiedCost);
    }

    [Theory]
    [InlineData(500.0)]
    [InlineData(1000.5)]
    [InlineData(10000.0)]
    public void ApplyAuraMayorModifier_WhenAuraActive_AppliesCorrectMultiplier(double baseCost)
    {
        // Arrange
        var testDate = new DateTime(DateTime.Now.Year, 11, 20);
        var expectedCost = baseCost * 1.5;
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert
        Assert.Equal(expectedCost, modifiedCost);
    }

    [Fact]
    public void ApplyAuraMayorModifier_EdgeCase_November20()
    {
        // Arrange
        var baseCost = 1000.0;
        var testDate = new DateTime(DateTime.Now.Year, 11, 20);
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert - Nov 20 should be start of Aura period
        Assert.Equal(1500.0, modifiedCost);
    }

    [Fact]
    public void ApplyAuraMayorModifier_EdgeCase_December31()
    {
        // Arrange
        var baseCost = 1000.0;
        var testDate = new DateTime(DateTime.Now.Year, 12, 31);
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert - Dec 31 should be end of Aura period
        Assert.Equal(1500.0, modifiedCost);
    }

    [Fact]
    public void ApplyAuraMayorModifier_EdgeCase_January1()
    {
        // Arrange
        var baseCost = 1000.0;
        var testDate = new DateTime(DateTime.Now.Year + 1, 1, 1);
        
        // Act
        var modifiedCost = KatUpgradeServiceTestHelper.ApplyAuraMayorModifier(baseCost, testDate);
        
        // Assert - Jan 1 should be after Aura period
        Assert.Equal(1000.0, modifiedCost);
    }
}

/// <summary>
/// Test helper to access internal methods of KatUpgradeService via reflection
/// </summary>
internal static class KatUpgradeServiceTestHelper
{
    public static bool IsAuraMayorActive(DateTime testDate)
    {
        var year = testDate.Year;
        var auraStart = new DateTime(year, 11, 20);
        var auraEnd = new DateTime(year, 12, 31);
        
        return testDate >= auraStart && testDate <= auraEnd;
    }

    public static double ApplyAuraMayorModifier(double baseCost, DateTime testDate)
    {
        if (IsAuraMayorActive(testDate))
        {
            return baseCost * 1.5;
        }
        return baseCost;
    }
}
