-- Run ONCE BY HAND, as the database's Entra admin, in the kassensturz database (not in master), after the first
-- `az deployment group create` has created the managed identities (docs/deployment.md). CI never runs this file:
-- the deploy identity must not be able to grant itself rights in a database kassensturz lives in (docs/adr/0003).
-- Running it again changes nothing.

-- 1. The schemas, owned by dbo. CI fills them (db/<service>/*.sql) but may not create schemas itself.
IF SCHEMA_ID(N'recipes') IS NULL
    EXEC (N'CREATE SCHEMA recipes AUTHORIZATION dbo');
IF SCHEMA_ID(N'shopping') IS NULL
    EXEC (N'CREATE SCHEMA shopping AUTHORIZATION dbo');

-- 2. The Recipes service's identity: read and write rows in its own schema, nothing else. The user name is the
--    managed identity's name (infra/modules/identities.bicep).
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-recipes') IS NULL
    CREATE USER [id-kochbuch-recipes] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::recipes TO [id-kochbuch-recipes];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [id-kochbuch-recipes];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::shopping TO [id-kochbuch-recipes];

-- 2b. The Shopping service's identity, the same way: its own schema only (docs/adr/0005).
IF DATABASE_PRINCIPAL_ID(N'id-kochbuch-shopping') IS NULL
    CREATE USER [id-kochbuch-shopping] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE ON SCHEMA::shopping TO [id-kochbuch-shopping];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [id-kochbuch-shopping];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::recipes TO [id-kochbuch-shopping];

-- 3. The deploy identity (GitHub Actions, OIDC): may create and change tables in the kochbuch schemas only, so CI can
--    apply db/<service>/*.sql. No rights on rows, none on dbo. The user name is the deploy app registration's name.
IF DATABASE_PRINCIPAL_ID(N'kochbuch-deploy') IS NULL
    CREATE USER [kochbuch-deploy] FROM EXTERNAL PROVIDER;
GRANT CREATE TABLE TO [kochbuch-deploy];
GRANT ALTER ON SCHEMA::recipes TO [kochbuch-deploy];
GRANT ALTER ON SCHEMA::shopping TO [kochbuch-deploy];
DENY ALTER, SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [kochbuch-deploy];
