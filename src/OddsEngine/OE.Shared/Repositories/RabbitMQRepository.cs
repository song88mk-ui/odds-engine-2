using OE.Shared.Enums;
using OE.Shared.Models;
using OE.Shared.Models.Responses;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OE.Shared.Repositories
{
    public class RabbitMQRepository : IRabbitMQRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;

        public RabbitMQRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public BaseResponseDTO<MessageResponseDTO> PublishViaHttp(MessageRequestDTO request)
        {
            try
            {
                var response = _httpClient.PostAsync($"v1/broker/publish", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")).Result;

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<MessageResponseDTO>>(response.Content.ReadAsStringAsync().Result, JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<MessageResponseDTO>>(response.Content.ReadAsStringAsync().Result, JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<MessageResponseDTO>()
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<MessageResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error} ({ex.Message}) StackTrace:{ex.StackTrace}"
                };
            }
        }

        public async Task<BaseResponseDTO<MessageResponseDTO>> PublishViaHttpAsync(MessageRequestDTO request)
        {
            try
            {
                var response = await _httpClient.PostAsync($"v1/broker/publish", new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<MessageResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<MessageResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<MessageResponseDTO>()
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<MessageResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error} ({ex.Message}) StackTrace:{ex.StackTrace}"
                };
            }
        }
    }
}
