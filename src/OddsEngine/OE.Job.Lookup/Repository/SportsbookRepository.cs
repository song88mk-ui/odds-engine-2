using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace OE.Job.Lookup.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        private readonly IMongoDatabase _db;
        private readonly IConfiguration _configuration;

        private IMongoCollection<SportLookup> sportLookupCollection;
        private IMongoCollection<SegmentLookup> segmentLookupCollection;
        private IMongoCollection<LeagueLookup> leagueLookupCollection;
        private IMongoCollection<MarketLookup> marketLookupCollection;
        private IMongoCollection<SelectionLookup> selectionLookupCollection;

        public SportsbookRepository(IConfiguration configuration)
        {
            _configuration = configuration;

            var client = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");

            sportLookupCollection = _db.GetCollection<SportLookup>("SportLookup");
            segmentLookupCollection = _db.GetCollection<SegmentLookup>("SegmentLookup");
            leagueLookupCollection = _db.GetCollection<LeagueLookup>("LeagueLookup");
            marketLookupCollection = _db.GetCollection<MarketLookup>("MarketLookup");
            selectionLookupCollection = _db.GetCollection<SelectionLookup>("SelectionLookup");

        }
        public void ProcessOnOddsChangeMessage(OnMarketChangeMessage message)
        {
            var sportArray = ToSportLookup(message);

            if (sportArray.Length > 0)
                BulkWrite(sportLookupCollection, sportArray);

            var segmentArray = ToSegmentLookup(message);

            if (segmentArray.Length > 0)
                BulkWrite(segmentLookupCollection, segmentArray);

            var leagueArray = ToLeagueLookup(message);

            if (leagueArray.Length > 0)
                BulkWrite(leagueLookupCollection, leagueArray);

            var marketArray = ToMarketLookup(message);

            if (marketArray.Length > 0)
                BulkWrite(marketLookupCollection, marketArray);

            var selectionArray = ToSelectionLookup(message);

            if (selectionArray.Length > 0)
                BulkWrite(selectionLookupCollection, selectionArray);

        }
        private WriteModel<SportLookup>[] ToSportLookup(OnMarketChangeMessage message)
        {
            var result = new List<WriteModel<SportLookup>>
            {
                new UpdateOneModel<SportLookup>(Builders<SportLookup>.Filter.Eq("SpId", message.SpId),
                Builders<SportLookup>.Update
                    .Set(s => s.SpId, message.SpId)
                    .Set(s => s.SpNa, ConvertToPascalCase(message.SpNa))
                    .SetOnInsert(s => s.IsMap, false))
                { IsUpsert = true }
            };

            return result.ToArray();
        }
        private WriteModel<SegmentLookup>[] ToSegmentLookup(OnMarketChangeMessage message)
        {
            var id = $"{message.SpId}:{message.SegId}";

            var result = new List<WriteModel<SegmentLookup>>
            {
                new UpdateOneModel<SegmentLookup>(Builders<SegmentLookup>.Filter.Eq("SeLoId", id),
                Builders<SegmentLookup>.Update
                    .Set(s => s.SeLoId, id)
                    .Set(s => s.SpId, message.SpId)
                    .Set(s => s.SegId, message.SegId)
                    .Set(s => s.SegNa, ConvertToPascalCase(message.SegNa))
                    .SetOnInsert(s => s.IsMap, false))
                { IsUpsert = true }
            };

            return result.ToArray();
        }
        private WriteModel<LeagueLookup>[] ToLeagueLookup(OnMarketChangeMessage message)
        {
            var id = $"{message.SpId}:{message.SegId}:{message.LeId}";

            var result = new List<WriteModel<LeagueLookup>>
            {
                new UpdateOneModel<LeagueLookup>(Builders<LeagueLookup>.Filter.Eq("LeLoId", id),
                Builders<LeagueLookup>.Update
                    .Set(s => s.LeLoId, id)
                    .Set(s => s.SpId, message.SpId)
                    .Set(s => s.SegId, message.SegId)
                    .Set(s => s.LeId, message.LeId)
                    .Set(s => s.LeNa, ConvertToPascalCase(message.LeNa))
                    .SetOnInsert(s => s.IsMap, false))
                { IsUpsert = true }
            };

            return result.ToArray();
        }

        private WriteModel<MarketLookup>[] ToMarketLookup(OnMarketChangeMessage message)
        {
            var markets = new List<WriteModel<MarketLookup>>();

            foreach (var market in message.Ma)
            {
                var marketLookupId = $"{message.SpId}:{market.MaTeId}";

                if (message.FiTyId == (int)FixtureType.Outright)
                {
                    markets.Add(new UpdateOneModel<MarketLookup>(Builders<MarketLookup>.Filter.Eq("MaLoId", marketLookupId),
                        Builders<MarketLookup>.Update
                            .Set(s => s.MaLoId, marketLookupId)
                            .Set(s => s.SpId, message.SpId)
                            .Set(s => s.MaTeId, market.MaTeId)
                            .Set(s => s.FiTyId, message.FiTyId)
                            .SetOnInsert(s => s.IsMap, false))
                    { IsUpsert = true });
                }
                else
                {
                    markets.Add(new UpdateOneModel<MarketLookup>(Builders<MarketLookup>.Filter.Eq("MaLoId", marketLookupId),
                        Builders<MarketLookup>.Update
                            .Set(s => s.MaLoId, marketLookupId)
                            .Set(s => s.SpId, message.SpId)
                            .Set(s => s.MaTeId, market.MaTeId)
                            .Set(s => s.MaTeNa, ConvertToPascalCase(market.MaTeNa))
                            .Set(s => s.FiTyId, message.FiTyId)
                            .SetOnInsert(s => s.IsMap, false))
                    { IsUpsert = true });
                }
            }

            return markets.ToArray();
        }

        private WriteModel<SelectionLookup>[] ToSelectionLookup(OnMarketChangeMessage message)
        {
            var selections = new List<WriteModel<SelectionLookup>>();

            foreach (var market in message.Ma)
            {
                foreach(var selection in market.Se)
                {
                    var selectionLookupId = $"{message.SpId}:{market.MaTeId}:{selection.SeTeId}";

                    if (message.FiTyId == (int)FixtureType.Outright)
                    {
                        selections.Add(new UpdateOneModel<SelectionLookup>(Builders<SelectionLookup>.Filter.Eq("SeLoId", selectionLookupId),
                            Builders<SelectionLookup>.Update
                                .Set(s => s.SeLoId, selectionLookupId)
                                .Set(s => s.SpId, message.SpId)
                                .Set(s => s.MaTeId, market.MaTeId)
                                .Set(s => s.SeTeId, selection.SeTeId)
                                .Set(s => s.SeTeNa, ConvertToPascalCase(selection.SeNa))
                                .Set(s => s.FiTyId, message.FiTyId)
                                .SetOnInsert(s => s.IsMap, false))
                        { IsUpsert = true });
                    }
                    else
                    {
                        selections.Add(new UpdateOneModel<SelectionLookup>(Builders<SelectionLookup>.Filter.Eq("SeLoId", selectionLookupId),
                            Builders<SelectionLookup>.Update
                                .Set(s => s.SeLoId, selectionLookupId)
                                .Set(s => s.SpId, message.SpId)
                                .Set(s => s.MaTeId, market.MaTeId)
                                .Set(s => s.SeTeId, selection.SeTeId)
                                .Set(s => s.SeTeNa, ConvertToPascalCase(selection.SeNa))
                                .Set(s => s.FiTyId, message.FiTyId)
                                .SetOnInsert(s => s.IsMap, false))
                        { IsUpsert = true });
                    }

                }
            }

            return selections.ToArray();
        }

        private void BulkWrite<T>(IMongoCollection<T> collection, WriteModel<T>[] data)
        {
            collection.BulkWrite(data, new BulkWriteOptions { IsOrdered = false, BypassDocumentValidation = false });
        }
        private string ConvertToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(value)) return string.Empty;

            var list = new List<string>();
            var words = value.Trim().Split(new[] { ' ' }).Where(c => !string.IsNullOrEmpty(c) && !string.IsNullOrWhiteSpace(c)).ToList();

            foreach (var item in words)
            {
                if (char.IsUpper(item, 0))
                    list.Add(item);
                else
                    list.Add($"{item.Substring(0, 1).ToUpper()}{item.Substring(1)}");
            }

            var result = string.Empty;

            foreach (var item in list)
                result += $"{item} ";

            return result.TrimEnd(' ');
        }
    }

}
