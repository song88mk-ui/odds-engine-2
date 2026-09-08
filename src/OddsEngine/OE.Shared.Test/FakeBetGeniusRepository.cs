using OE.Shared.Models.Provider.BetGenius;
using OE.Shared.Models.Responses;
using OE.Shared.Repositories;

namespace OE.Shared.Test
{
    // Configurable per-test stand-in for IBetGeniusRepository — set only the responders a given
    // test needs; the rest throw if invoked, so an unexpected call fails loudly instead of NREing.
    public class FakeBetGeniusRepository : IBetGeniusRepository
    {
        public Func<Task<BaseResponseDTO<BGAuth>>>? AuthResponder { get; set; }
        public Func<string, string, Task<BaseResponseDTO<BGEvent>>>? FixtureResponder { get; set; }
        public Func<string, Task<BaseResponseDTO<BGEvents>>>? FixturesResponder { get; set; }

        public Task<BaseResponseDTO<BGAuth>> GetBetGeniusAuthorizationToken() =>
            (AuthResponder ?? throw new InvalidOperationException("AuthResponder not configured"))();

        public Task<BaseResponseDTO<BGEvent>> GetBetGeniusFixture(string token, string fixtureId) =>
            (FixtureResponder ?? throw new InvalidOperationException("FixtureResponder not configured"))(token, fixtureId);

        public Task<BaseResponseDTO<BGEvents>> GetBetGeniusFixtures(string token) =>
            (FixturesResponder ?? throw new InvalidOperationException("FixturesResponder not configured"))(token);
    }
}
