-- Run ONCE BY HAND, as the database's Entra admin, in the kassensturz database (not in master), after
-- `az deployment group create` has created the id-kochbuch-api managed identity. Running it again is harmless.

IF SCHEMA_ID(N'recipes') IS NULL
    EXEC (N'CREATE SCHEMA recipes AUTHORIZATION dbo');
IF SCHEMA_ID(N'shopping') IS NULL
    EXEC (N'CREATE SCHEMA shopping AUTHORIZATION dbo');

-- Koch.Api's managed identity: read and write rows in recipes and shopping only; denied dbo (kassensturz).
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-api') IS NULL
    CREATE USER [id-kochbuch-api] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::recipes TO [id-kochbuch-api];
GRANT SELECT, INSERT, UPDATE ON SCHEMA::shopping TO [id-kochbuch-api];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [id-kochbuch-api];

-- Retired identities from the earlier multi-service / dbdeploy layout.
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-recipes') IS NOT NULL
    DROP USER [id-kochbuch-recipes];
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-shopping') IS NOT NULL
    DROP USER [id-kochbuch-shopping];
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-dbdeploy') IS NOT NULL
    DROP USER [id-kochbuch-dbdeploy];
IF DATABASE_PRINCIPAL_ID(N'kochbuch-deploy') IS NOT NULL
    DROP USER [kochbuch-deploy];
