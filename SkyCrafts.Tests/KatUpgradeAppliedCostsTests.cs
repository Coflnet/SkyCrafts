using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SkyCrafts.Tests
{
    public class KatUpgradeAppliedCostsTests
    {
        private static KatUpgradeService CreateService()
        {
            var initial = new System.Collections.Generic.Dictionary<string, string?> { { "API_BASE_URL", "https://sky.coflnet.com" } };
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(initial).Build();
            return new KatUpgradeService(cfg, NullLogger<KatUpgradeService>.Instance);
        }

        [Fact]
        public async Task GetKatUpgradeCostsWithAppliedMultipliers_AppliesMultiplier_WhenAuraActive()
        {
            var svc = CreateService();
            var baseCosts = (await svc.GetKatUpgradeCosts()).ToList();
            var testDate = new DateTime(2025, 12, 1); // within Aura period
            var modified = (await svc.GetKatUpgradeCostsWithAppliedMultipliers(testDate)).ToList();

            Assert.Equal(baseCosts.Count, modified.Count);
            for (int i = 0; i < baseCosts.Count; i++)
            {
                var expected = (int)Math.Round(baseCosts[i].Cost * 1.5);
                Assert.Equal(expected, modified[i].Cost);
            }
        }

        [Fact]
        public async Task GetKatUpgradeCostsWithAppliedMultipliers_NoChange_WhenAuraInactive()
        {
            var svc = CreateService();
            var baseCosts = (await svc.GetKatUpgradeCosts()).ToList();
            var testDate = new DateTime(DateTime.Now.Year, 1, 1); // outside Aura period
            var modified = (await svc.GetKatUpgradeCostsWithAppliedMultipliers(testDate)).ToList();

            Assert.Equal(baseCosts.Count, modified.Count);
            for (int i = 0; i < baseCosts.Count; i++)
            {
                Assert.Equal(baseCosts[i].Cost, modified[i].Cost);
            }
        }

        [Fact]
        public async Task GetKatUpgradeCostsWithAppliedMultipliers_KeepsScathaBaseCost_AfterAuraEnded()
        {
            var svc = CreateService();
            var reportDate = new DateTime(2026, 11, 21);

            var scatha = (await svc.GetKatUpgradeCostsWithAppliedMultipliers(reportDate))
                .Where(c => c.Name == "Scatha")
                .ToDictionary(c => c.BaseRarity, c => c.Cost);

            // A level 100 pet gets 30% off in game: 87.5m for rare to epic, 175m for epic to legendary.
            Assert.Equal(125_000_000, scatha[Coflnet.Sky.Api.Client.Model.Tier.RARE]);
            Assert.Equal(250_000_000, scatha[Coflnet.Sky.Api.Client.Model.Tier.EPIC]);
        }
    }
}
