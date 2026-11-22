-- The Shopping service's tables, in their own schema inside the kassensturz database (docs/adr/0003).
-- Idempotent: CI runs every script in db/shopping on each deploy; running one again changes nothing.
IF SCHEMA_ID(N'shopping') IS NULL
    EXEC (N'CREATE SCHEMA shopping');

-- One row per synced document: a list ('list:<id>') or one contribution to a list ('item:<id>'). Read
-- and written whole, like kassensturz's dbo.Documents.
IF OBJECT_ID(N'shopping.Documents') IS NULL
CREATE TABLE shopping.Documents
(
    Id nvarchar(200) NOT NULL CONSTRAINT PK_ShoppingDocuments PRIMARY KEY,
    Json nvarchar(max) NOT NULL CONSTRAINT CK_ShoppingDocuments_Json CHECK (ISJSON(Json) = 1),
    -- Raised by one with every write that changes the row; a write of the same JSON leaves it (docs/adr/0004).
    Version int NOT NULL,
    -- Kept by SQL Server: each write moves the previous version into shopping.DocumentsHistory, so mistakes can be
    -- undone. The history is deleted after 6 months.
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = shopping.DocumentsHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));
