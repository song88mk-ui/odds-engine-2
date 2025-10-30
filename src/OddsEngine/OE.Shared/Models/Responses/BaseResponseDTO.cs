using OE.Shared.Enums;

namespace OE.Shared.Models.Responses
{
    public class BaseResponseDTO<T>
    {
        public StatusCode StatusCode { get; set; }
        public string Message { get; set; }
        public T Result { get; set; }
    }
}
