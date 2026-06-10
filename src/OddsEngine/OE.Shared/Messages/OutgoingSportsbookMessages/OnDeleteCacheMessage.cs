using OE.Shared.Enums;
using System;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingSportsbookMessages
{
    public class OnDeleteCacheMessage
    {
        public Guid ClientId { get; set; }
        public string ClientCode { get; set; }
        public CacheType CacheType { get; set; }
        public string CacheKey { get; set; } = null;
        public List<string> CacheKeys { get; set; } = null;
        public bool PurgeAll { get; set; } = false;
    }
}
