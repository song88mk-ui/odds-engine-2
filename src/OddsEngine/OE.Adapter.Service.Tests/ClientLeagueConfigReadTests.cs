using OE.Adapter.Service.ClientVisibility;
using OE.Shared.Data.Trading.Entities;

namespace OE.Adapter.Service.Tests
{
    // Trading.dbo.ClientLeagueConfig as it really is: every column NULL-able and IsActive a smallint (BO-151 found a bool
    // entity failing on every row on DEV). In-memory EF cannot show that, so the entity's types are pinned here, and the
    // rule for what counts as a restriction is checked on rows as the reader gets them.
    public class ClientLeagueConfigReadTests
    {
        private static ClientLeagueConfig Row(string? client, int? sport, int? segment, int? league, short? isActive) => new()
        {
            ClientCode = client!,
            SportId = sport,
            SegmentId = segment,
            LeagueId = league,
            IsActive = isActive
        };

        [Fact]
        public void TheEntityMirrorsTheColumnTypes_SoEfCanReadEveryRow()
        {
            var entity = typeof(ClientLeagueConfig);

            Assert.Equal(typeof(short?), entity.GetProperty(nameof(ClientLeagueConfig.IsActive))!.PropertyType);
            Assert.Equal(typeof(int?), entity.GetProperty(nameof(ClientLeagueConfig.SportId))!.PropertyType);
            Assert.Equal(typeof(int?), entity.GetProperty(nameof(ClientLeagueConfig.SegmentId))!.PropertyType);
            Assert.Equal(typeof(int?), entity.GetProperty(nameof(ClientLeagueConfig.LeagueId))!.PropertyType);
        }

        [Fact]
        public void OnlyIsActiveZeroIsARestriction()
        {
            var restrictions = ClientVisibilitySource.RestrictionsFrom(new[]
            {
                Row("S88", 1, 2, 3, 0),
                Row("TSO", 1, 2, 3, 1),
                Row("G01", 1, 2, 3, null)
            });

            var restriction = Assert.Single(restrictions);
            Assert.Equal("S88", restriction.ClientCode);
            Assert.Equal(new LeagueKey(1, 2, 3), restriction.League);
        }

        [Fact]
        public void ARowWithoutAClientOrALeague_IsNotARestriction_AndDoesNotBreakTheRead()
        {
            var restrictions = ClientVisibilitySource.RestrictionsFrom(new[]
            {
                Row(null, 1, 2, 3, 0),
                Row("  ", 1, 2, 3, 0),
                Row("S88", null, 2, 3, 0),
                Row("S88", 1, null, 3, 0),
                Row("S88", 1, 2, null, 0),
                Row("S88", 1, 2, 4, 0)
            });

            var restriction = Assert.Single(restrictions);
            Assert.Equal(new LeagueKey(1, 2, 4), restriction.League);
        }

        [Fact]
        public void NoRows_NoRestrictions()
        {
            Assert.Empty(ClientVisibilitySource.RestrictionsFrom(new List<ClientLeagueConfig>()));
        }
    }
}
