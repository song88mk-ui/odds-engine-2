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
--                                          back to SOURCE_TEXT).
--   * PROCESSED                          — the pipeline's worklist flag: 0 = row needs the
--                                          external pipeline (DeepL) pass, 1 = handled. Seeded 0
--                                          on insert, reset to 0 by OE.Translation.Service on a
--                                          genuine source-name change; set to 1 by the pipeline.
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
        SOURCE_TEXT        NVARCHAR(250)  NOT NULL,
        TARGET_TEXT_ZH     NVARCHAR(250)  NOT NULL CONSTRAINT DF_TC2_ZH DEFAULT (N''),
        TARGET_TEXT_ES     NVARCHAR(250)  NOT NULL CONSTRAINT DF_TC2_ES DEFAULT (N''),
        TARGET_TEXT_FR     NVARCHAR(250)  NOT NULL CONSTRAINT DF_TC2_FR DEFAULT (N''),
        HAS_BET_PLACEMENT  BIT            NOT NULL CONSTRAINT DF_TC2_HBP DEFAULT (0),
        CREATED_AT         DATETIME2(7)   NULL,
        PROCESSED          BIT            NOT NULL CONSTRAINT DF_TC2_PROCESSED DEFAULT (0),
        UPDATED_AT         DATETIME2(7)   NULL,
        CONSTRAINT PK_TranslationCache2 PRIMARY KEY CLUSTERED (ID)
    );
END;
GO

-- 2026-07: GLOSSARY_VERSION retired, PROCESSED is the worklist flag. Upgrade in-place tables.
IF COL_LENGTH(N'dbo.TranslationCache2', N'PROCESSED') IS NULL
BEGIN
    ALTER TABLE dbo.TranslationCache2
        ADD PROCESSED BIT NOT NULL CONSTRAINT DF_TC2_PROCESSED DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.TranslationCache2', N'GLOSSARY_VERSION') IS NOT NULL
BEGIN
    ALTER TABLE dbo.TranslationCache2 DROP COLUMN GLOSSARY_VERSION;
END;
GO

-- 2026-07: text columns shrunk NVARCHAR(1000) -> NVARCHAR(250). COL_LENGTH is bytes (250 -> 500);
-- over-length data is truncated first so the ALTERs cannot fail.
IF COL_LENGTH(N'dbo.TranslationCache2', N'SOURCE_TEXT') > 500
BEGIN
    UPDATE dbo.TranslationCache2 SET SOURCE_TEXT = LEFT(SOURCE_TEXT, 250) WHERE LEN(SOURCE_TEXT) > 250;
    ALTER TABLE dbo.TranslationCache2 ALTER COLUMN SOURCE_TEXT NVARCHAR(250) NOT NULL;
END;
GO

IF COL_LENGTH(N'dbo.TranslationCache2', N'TARGET_TEXT_ZH') > 500
BEGIN
    UPDATE dbo.TranslationCache2 SET TARGET_TEXT_ZH = LEFT(TARGET_TEXT_ZH, 250) WHERE LEN(TARGET_TEXT_ZH) > 250;
    ALTER TABLE dbo.TranslationCache2 ALTER COLUMN TARGET_TEXT_ZH NVARCHAR(250) NOT NULL;
END;
GO

IF COL_LENGTH(N'dbo.TranslationCache2', N'TARGET_TEXT_ES') > 500
BEGIN
    UPDATE dbo.TranslationCache2 SET TARGET_TEXT_ES = LEFT(TARGET_TEXT_ES, 250) WHERE LEN(TARGET_TEXT_ES) > 250;
    ALTER TABLE dbo.TranslationCache2 ALTER COLUMN TARGET_TEXT_ES NVARCHAR(250) NOT NULL;
END;
GO

IF COL_LENGTH(N'dbo.TranslationCache2', N'TARGET_TEXT_FR') > 500
BEGIN
    UPDATE dbo.TranslationCache2 SET TARGET_TEXT_FR = LEFT(TARGET_TEXT_FR, 250) WHERE LEN(TARGET_TEXT_FR) > 250;
    ALTER TABLE dbo.TranslationCache2 ALTER COLUMN TARGET_TEXT_FR NVARCHAR(250) NOT NULL;
END;
GO

-- Upsert key: one row per String_ID.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_TC2_STRING' AND object_id = OBJECT_ID(N'dbo.TranslationCache2'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_TC2_STRING
        ON dbo.TranslationCache2 (STRING_ID);
END;
GO

-- Pipeline worklist: cheap TOP-N scan of unprocessed rows in ID order.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TC2_UNPROCESSED' AND object_id = OBJECT_ID(N'dbo.TranslationCache2'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_TC2_UNPROCESSED
        ON dbo.TranslationCache2 (ID)
        WHERE (PROCESSED = 0);
END;
GO

-- 2026-07: supports update.py's same-SOURCE_TEXT propagation (once one row's text is
-- DeepL-translated, fill every other row sharing that SOURCE_TEXT/SOURCE_LANGUAGE that
-- doesn't have that language yet) — WHERE SOURCE_TEXT = ? AND SOURCE_LANGUAGE = ?.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TC2_SOURCE_TEXT' AND object_id = OBJECT_ID(N'dbo.TranslationCache2'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_TC2_SOURCE_TEXT
        ON dbo.TranslationCache2 (SOURCE_TEXT, SOURCE_LANGUAGE)
        INCLUDE (STRING_ID, TARGET_TEXT_ZH, TARGET_TEXT_ES, TARGET_TEXT_FR);
END;
GO
