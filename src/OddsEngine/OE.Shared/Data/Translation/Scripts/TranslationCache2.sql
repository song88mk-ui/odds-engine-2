-- ============================================================================
-- TranslationCache2 — the single flat translation store (bet-genius
-- docs/TranslationTemplateGlossary.md, 2026-07 translation-flow redesign; the retired
-- SportsbookDynamic/SportsbookGlossary tables are gone).
--
-- One row per STRING_ID (MaTe:/SeTe:/Te:/SpId:/SegId:/LeId: keys), fixed column per target
-- language. Ownership is split by column:
--   * SOURCE_LANGUAGE / SOURCE_TEXT      — seeded by OE.Translation.Service (insert, update on
--                                          genuine source-name change, liveness touch on
--                                          UPDATED_AT; targets are never touched).
--   * TARGET_TEXT_ZH / _ES / _FR         — written by the external translation pipeline only.
--                                          Empty string means "not translated yet" (readers fall
--                                          back to SOURCE_TEXT); it doubles as the pipeline's
--                                          worklist.
--   * HAS_BET_PLACEMENT                  — flagged by OE.Translation.Job so retention never
--                                          removes rows a bet history references.
-- OE.Adapter.Service preloads the whole table into memory at startup, so adding a target
-- language is a schema change here plus code changes in the adapter and the external script.
--
-- Schema is managed externally (no in-repo EF migrations) — apply via change control.
-- Idempotent: guarded by IF NOT EXISTS. If the table exists in the RETIRED row-per-language shape
-- (STRING_ID, TARGET_LANGUAGE, TARGET_TEXT), this script fails loudly instead of leaving the wrong
-- schema in place — migrate/drop via change control first.
-- ============================================================================

IF COL_LENGTH(N'dbo.TranslationCache2', N'TARGET_LANGUAGE') IS NOT NULL
BEGIN
    RAISERROR(N'dbo.TranslationCache2 exists with the retired row-per-language schema (TARGET_LANGUAGE column). Migrate or drop it via change control before applying the flat schema.', 16, 1);
END;
GO

IF OBJECT_ID(N'dbo.TranslationCache2', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TranslationCache2
    (
        ID                 BIGINT IDENTITY(1,1) NOT NULL,
        STRING_ID          NVARCHAR(200)  NOT NULL,
        SOURCE_LANGUAGE    VARCHAR(10)    NOT NULL,
        SOURCE_TEXT        NVARCHAR(1000) NOT NULL,
        TARGET_TEXT_ZH     NVARCHAR(1000) NOT NULL CONSTRAINT DF_TC2_ZH DEFAULT (N''),
        TARGET_TEXT_ES     NVARCHAR(1000) NOT NULL CONSTRAINT DF_TC2_ES DEFAULT (N''),
        TARGET_TEXT_FR     NVARCHAR(1000) NOT NULL CONSTRAINT DF_TC2_FR DEFAULT (N''),
        HAS_BET_PLACEMENT  BIT            NOT NULL CONSTRAINT DF_TC2_HBP DEFAULT (0),
        CREATED_AT         DATETIME2(7)   NULL,
        GLOSSARY_VERSION   INT            NULL,
        UPDATED_AT         DATETIME2(7)   NULL,
        CONSTRAINT PK_TranslationCache2 PRIMARY KEY CLUSTERED (ID)
    );
END;
GO

-- Upsert key: one row per String_ID.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_TC2_STRING' AND object_id = OBJECT_ID(N'dbo.TranslationCache2'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_TC2_STRING
        ON dbo.TranslationCache2 (STRING_ID);
END;
GO
