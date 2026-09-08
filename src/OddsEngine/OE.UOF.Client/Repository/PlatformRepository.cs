using OE.UOF.Client.Models;
using OE.Shared.Enums;
using System.Net;
using System.Text.Json;
using OE.Shared.Utilities;

namespace OE.UOF.Client.Repository
{
    public class PlatformRepository : IPlatformRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IMemCache _memCache;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public PlatformRepository(IConfiguration configuration, IMemCache memCache)
        {
            _configuration = configuration;
            _memCache = memCache;

            if (_httpClient == null)
            {
                _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(_configuration["API:PlatformAPI"]),
                    Timeout = TimeSpan.FromSeconds(5)
                };

                _httpClient.DefaultRequestHeaders.Add("x-api-key", _configuration["API:APIKey"]);
                _httpClient.DefaultRequestHeaders.Add("x-client-id", string.Empty);
            }
        }

        public async void ProcessMarketTemplatePlayerProps()
        {
            try
            {
                var result = await GetMarketTemplatePlayerPropsDTO();

                var key = $"mtpp";

                _memCache.AddOrUpdateCache(key, result.Result, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception)
            {
            }
        }

        public List<MarketTemplatePlayerPropsResponseDTO> GetMarketTemplatePlayerProps()
        {
            return _memCache.GetCache<List<MarketTemplatePlayerPropsResponseDTO>>($"mtpp") ?? new List<MarketTemplatePlayerPropsResponseDTO>();
        }

        #region Private Methods

        private async Task<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>> GetMarketTemplatePlayerPropsDTO()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/client/markettemplateplayerprops");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        #endregion
    }
}
