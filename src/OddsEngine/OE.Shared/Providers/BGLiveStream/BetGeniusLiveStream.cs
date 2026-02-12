using OE.Shared.Enums;
using OE.Shared.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;

namespace OE.Shared.Providers.BGLiveStream
{
    public class BetGeniusLiveStream : ILiveStreamProvider
    {
        public ChannelType ChannelType => ChannelType.BG;

        private readonly IBetGeniusRepository _bgRepo;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BetGeniusLiveStream> _logger;

        public BetGeniusLiveStream(
            IConfiguration configuration,
            ILogger<BetGeniusLiveStream> logger,
            IBetGeniusRepository bgRepo)
        {
            _configuration = configuration;
            _logger = logger;
            _bgRepo = bgRepo;
        }

        public async Task<List<string>> GetLiveStreams()
        {
            try
            {
                _logger.LogInformation($"GetLiveStreams Start");

                var token = await _bgRepo.GetBetGeniusAuthorizationToken();

                if (token.StatusCode != StatusCode.Success)
                {
                    _logger.LogError($"GetLiveStreams: GetBetGeniusAuthorizationToken Failed: {token.Message}-{JsonSerializer.Serialize(token)}");
                    return new List<string>();
                }

                var result = await _bgRepo.GetBetGeniusFixtures(token.Result.IdToken);
                if (result == null)
                {
                    _logger.LogError($"GetLiveStreams: GetBetGeniusFixtures Failed: null");
                    return new List<string>();
                }

                if (result.StatusCode == StatusCode.Error)
                {
                    _logger.LogError($"GetLiveStreams: GetBetGeniusFixtures Failed: {result.Message}-{JsonSerializer.Serialize(result)}");
                    return new List<string>();
                }

                _logger.LogInformation($"GetLiveStreams: GetBetGeniusFixtures Result : {JsonSerializer.Serialize(result)}");

                var fixtures = result.Result;
                if (fixtures.Data == null || !fixtures.Data.Any())
                {
                    _logger.LogInformation("GetLiveStreams: No LiveStreams available");
                    return new List<string>();
                }

                _logger.LogInformation($"GetLiveStreams Result: {fixtures.Data.Count}");

                var liveStreams = fixtures.Data.Select(s => s.Id.ToString()).ToList();

                return liveStreams;
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetLiveStream Error: {ex.Message}-{ex.StackTrace}");
                return new List<string>();
            }
        }

        public async Task<(bool, string)> GetLiveStream(string fixtureId)
        {
            try
            {
                _logger.LogInformation($"GetLiveStream Start : {fixtureId}");

                var token = await _bgRepo.GetBetGeniusAuthorizationToken();

                if (token.StatusCode != StatusCode.Success)
                {
                    _logger.LogError($"GetLiveStream: GetBetGeniusAuthorizationToken Failed: {token.Message}-{JsonSerializer.Serialize(token)}");
                    return (false, string.Empty);
                }

                var result = await _bgRepo.GetBetGeniusFixture(token.Result.IdToken, fixtureId);
                if (result == null)
                {
                    _logger.LogError($"GetLiveStream: GetBetGeniusFixture Failed: null");
                    return (false, string.Empty);
                }

                if (result.StatusCode == StatusCode.Error)
                {
                    _logger.LogError($"GetLiveStream: GetBetGeniusFixture Failed: {result.Message}-{JsonSerializer.Serialize(result)}");
                    return (false, string.Empty);
                }
                else if (result.StatusCode == StatusCode.Bet_Not_Found)
                {
                    _logger.LogError($"GetLiveStream: GetBetGeniusFixture Not Found: {fixtureId}");
                    return (false, string.Empty);
                }

                _logger.LogInformation($"GetLiveStream: GetBetGeniusFixture Result : {JsonSerializer.Serialize(result)}");
                
                var fixture = result.Result;
                if (fixture?.LiveStreams == null || !fixture.LiveStreams.Any())
                {
                    _logger.LogInformation("GetLiveStream: No LiveStreams available");
                    return (false, string.Empty);
                }
                    
                _logger.LogInformation(
                    $"GetLiveStream Result for {fixtureId}: {JsonSerializer.Serialize(fixture)}");

                var liveStreams = fixture.LiveStreams.Select(s => 
                    new { 
                        Id = s.Id, 
                        Pe = s.Permissions.Select(s => $"{s.Device}:{s.Region}").ToList(), 
                        Ot = s.Deliveries
                    });
                
                return (true, JsonSerializer.Serialize(liveStreams));
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetLiveStream Error: {ex.Message}-{ex.StackTrace}");
                return (false, string.Empty);
            }
        }
    }
}
