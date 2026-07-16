using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Models;
using System;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnFixtureChangeMessage : OutgoingAdapterMessageBase
    {
        public string ClPr { get; set; }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public string FiNa { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public string FiStNa { get; set; }
        public string HoId { get; set; }
        public string AwId { get; set; }
        public string HoNa { get; set; } // Home team name — source text for translation lookup
        public string AwNa { get; set; } // Away team name — source text for translation lookup
        public string Sc { get; set; }
        public bool IsOfLi { get; set; }
        public int HoReCa { get; set; }
        public int AwReCa { get; set; }
        public int CuPe { get; set; }
        public int CuMi { get; set; }
        public int FiTyId { get; set; }
        public string FiTyNa { get; set; }
        public int ToTi { get; set; }
        public string Fa { get; set; }
        public int SoOr { get; set; }
        public string ClCo { get; set; } = string.Empty;
        public string CoId { get; set; }
        public bool IsSu { get; set; }
        public bool? IsBo { get; set; }
        public int CuSe { get; set; }
        public int CuSer { get; set; }

        public string StUr { get; set; }

        // Translation lists (2026-07 translation-flow redesign): populated by OE.Adapter.Service
        // from its TranslationCache2 preload, keyed SpId:/SegId:/LeId:/Te:{HoId}/Te:{AwId}.
        // Null/empty means no translation exists (or, on the OE.Sportsbook -> OE.Websocket hop,
        // unchanged since last publish) — consumers fall back to the English *Na source.
        public List<TranslationMessage> SpTr { get; set; }
        public List<TranslationMessage> SegTr { get; set; }
        public List<TranslationMessage> LeTr { get; set; }
        public List<TranslationMessage> HoTr { get; set; }
        public List<TranslationMessage> AwTr { get; set; }

        // Player roster for per-player prop markets (2026-07 player-markets feature): flattened
        // by the adapter (bet-genius) from the vendor fixture's nested CompetitorType=Person
        // entries. Joined against MarketMessage.CompId to label player markets. Null = this
        // payload carries no roster information — consumers keep the roster they already have.
        // Property names (Pl/PlId/PlNa) are the wire contract shared with bet-genius's
        // OnInternalFixtureChangeMessage — never rename.
        public List<PlayerMessage> Pl { get; set; }
    }

    // One roster entry. PlId is the vendor competitor id — the same id space as
    // MarketMessage.CompId and SelectionMessage.CompId. Deliberately NOT named CoId: that name is
    // the bet-builder correlation id on platform entities.
    public class PlayerMessage
    {
        public int PlId { get; set; }
        public string PlNa { get; set; }
        // Translation of PlNa (TranslationCache2 key "Pl:{PlId}"); populated by
        // OE.Adapter.Service, null/empty -> fall back to PlNa. See the translation lists above.
        public List<TranslationMessage> Tr { get; set; }
    }
}
