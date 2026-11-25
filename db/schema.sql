-- Idempotent schema for Kochbuch's recipes and shopping lists inside the shared Azure SQL database.
-- Run once by hand (or in tests) alongside db/manual/grants.sql.

IF SCHEMA_ID(N'recipes') IS NULL
    EXEC (N'CREATE SCHEMA recipes');

IF OBJECT_ID(N'recipes.Documents') IS NULL
CREATE TABLE recipes.Documents
(
    Id nvarchar(200) NOT NULL CONSTRAINT PK_RecipesDocuments PRIMARY KEY,
    Json nvarchar(max) NOT NULL CONSTRAINT CK_RecipesDocuments_Json CHECK (ISJSON(Json) = 1),
    Version int NOT NULL,
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = recipes.DocumentsHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));

IF SCHEMA_ID(N'shopping') IS NULL
    EXEC (N'CREATE SCHEMA shopping');

IF OBJECT_ID(N'shopping.Documents') IS NULL
CREATE TABLE shopping.Documents
(
    Id nvarchar(200) NOT NULL CONSTRAINT PK_ShoppingDocuments PRIMARY KEY,
    Json nvarchar(max) NOT NULL CONSTRAINT CK_ShoppingDocuments_Json CHECK (ISJSON(Json) = 1),
    Version int NOT NULL,
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = shopping.DocumentsHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));
