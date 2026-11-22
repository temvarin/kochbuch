-- The Recipes service's tables, in their own schema inside the kassensturz database (docs/adr/0003).
-- Idempotent: CI runs every script in db/recipes on each deploy; running one again changes nothing.
IF SCHEMA_ID(N'recipes') IS NULL
    EXEC (N'CREATE SCHEMA recipes');

-- One row per synced document: a recipe ('recipe:<id>') or an ingredient of the catalog ('ingredient:<id>'). Read
-- and written whole, like kassensturz's dbo.Documents.
IF OBJECT_ID(N'recipes.Documents') IS NULL
CREATE TABLE recipes.Documents
(
    Id nvarchar(200) NOT NULL CONSTRAINT PK_RecipesDocuments PRIMARY KEY,
    Json nvarchar(max) NOT NULL CONSTRAINT CK_RecipesDocuments_Json CHECK (ISJSON(Json) = 1),
    -- Raised by one with every write that changes the row; a write of the same JSON leaves it (docs/adr/0004).
    Version int NOT NULL,
    -- Kept by SQL Server: each write moves the previous version into recipes.DocumentsHistory, so mistakes can be
    -- undone. The history is deleted after 6 months.
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = recipes.DocumentsHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));
