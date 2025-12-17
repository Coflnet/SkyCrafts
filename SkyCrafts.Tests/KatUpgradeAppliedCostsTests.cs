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
            var testDate = new DateTime(DateTime.Now.Year, 12, 1); // within Aura period
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
    }
}
