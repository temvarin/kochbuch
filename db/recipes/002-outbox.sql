-- The Recipes service's outbox: events saved in the same transaction as the documents that caused them, published to
-- Service Bus afterwards by the service's relay (DDIA ch12 p.507-508; docs/adr/0008). Its rows are the published
-- contract (Koch.Recipes.Events), not the documents' internal shape.
-- Idempotent, like every script in db/recipes.
IF OBJECT_ID(N'recipes.Outbox') IS NULL
CREATE TABLE recipes.Outbox
(
    -- Increasing, with gaps; the consumer applies an event only if its sequence is higher than the last one it applied
    -- for the same photo (DDIA ch9 p.375-376, fencing tokens).
    Sequence bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_RecipesOutbox PRIMARY KEY,
    Type varchar(100) NOT NULL,
    Subject nvarchar(200) NOT NULL,
    Data nvarchar(max) NOT NULL CONSTRAINT CK_RecipesOutbox_Data CHECK (ISJSON(Data) = 1),
    OccurredAt datetime2 NOT NULL CONSTRAINT DF_RecipesOutbox_OccurredAt DEFAULT SYSUTCDATETIME(),
    -- Set by the relay once Service Bus has the event. Published rows stay, as a record of what was sent when.
    SentAt datetime2 NULL
);

-- The relay asks for the unsent events only; this keeps that cheap however many sent rows pile up.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecipesOutbox_Unsent' AND object_id = OBJECT_ID(N'recipes.Outbox'))
CREATE INDEX IX_RecipesOutbox_Unsent ON recipes.Outbox (Sequence) WHERE SentAt IS NULL;
