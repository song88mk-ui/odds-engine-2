using System;
using System.Collections.Generic;

namespace OE.Shared.Models
{
    public class ClientLiveFixtureModel
    {
        public string ClientId { get; set; }
        public int SportId { get; set; }
        public List<FixtureModel> Fixtures { get; set; } = new List<FixtureModel>();
    }
}
