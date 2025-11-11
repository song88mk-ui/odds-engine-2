using Microsoft.EntityFrameworkCore;
using OE.Shared.Data.Trading;
using OE.Shared.Models;

namespace OE.UOF.Client.Repository
{
    public class TradingRepository : ITradingRepository
    {
        private readonly IConfiguration _configuration;

        public TradingRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public List<ClientConfigurationModel> GetClientConfigurations()
        {
            var result = new List<ClientConfigurationModel>();

            var optionsBuilder = new DbContextOptionsBuilder<TradingContext>().UseSqlServer(_configuration["Customer:Trading"]);

            using (var context = new TradingContext(optionsBuilder.Options))
            {
                var clientId = Guid.Parse(_configuration["Custom:ClientId"]);

                var configurations = context.ClientOddsFeedConfiguration.Where(c => c.ClientId == clientId).ToList();

                if (configurations.Any())
                {
                    foreach (var item in configurations)
                    {
                        result.Add(new ClientConfigurationModel
                        {
                            SportId = item.SportId,
                            LiveOdds = item.LiveOdds,
                            PrematchOdds = item.PrematchOdds,
                            MaxFixtures = item.MaxFixtures
                        });
                    }
                }
            }

            return result;
        }

        public List<ClientLiveFixtureModel> GetClientLiveFixtures()
        {
            var result = new List<ClientLiveFixtureModel>();

            var optionsBuilder = new DbContextOptionsBuilder<TradingContext>().UseSqlServer(_configuration["Custom:Trading"]);

            using (var context = new TradingContext(optionsBuilder.Options))
            {
                var clientId = Guid.Parse(_configuration["Custom:ClientId"]);

                var configurations = context.ClientOddsFeedConfiguration.Where(c => c.ClientId == clientId && c.MaxFixtures.HasValue).ToList();

                if (configurations.Any())
                {
                    var currentDate = DateTime.UtcNow;

                    var startDate = new DateTime(currentDate.Year, currentDate.Month, 1);
                    var endDate = startDate.AddMonths(1).AddSeconds(-1);

                    foreach (var item in configurations)
                    {
                        var liveFixtures = context.ClientOddsFeedLiveFixture.Where(c => c.ClientId == item.ClientId && c.SportId == item.SportId && c.FixtureStartDate >= startDate && c.FixtureStartDate <= endDate).ToList();

                        if (liveFixtures.Any())
                        {
                            result.Add(new ClientLiveFixtureModel
                            {
                                ClientId = item.ClientId.ToString(),
                                SportId = item.SportId,
                                Fixtures = liveFixtures.Select(s => new FixtureModel
                                {
                                    FixtureSpecifier = s.FixtureSpecifier,
                                    FixtureStartDate = s.FixtureStartDate,
                                }).ToList()
                            });
                        }
                    }
                }
            }

            return result;

        }
    }
}
