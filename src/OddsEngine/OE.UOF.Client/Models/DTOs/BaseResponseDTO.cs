using OE.Shared.Enums;

namespace OE.UOF.Client.Models
{
    public class BaseResponseDTO<T>
    {
        public StatusCode StatusCode { get; set; }
        public string Message { get; set; }
        public T Result { get; set; }
    }
}
