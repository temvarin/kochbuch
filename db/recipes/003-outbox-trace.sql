-- Where in a trace each outbox event was saved, as a W3C traceparent (55 characters: 00-<trace>-<span>-<flags>). The
-- relay passes it on with the event, so Media's work shows up in the same trace as the sync request that caused it
-- (docs/adr/0009). NULL for events saved outside a trace, and for those saved before this script.
-- Idempotent, like every script in db/recipes.
IF COL_LENGTH(N'recipes.Outbox', N'TraceParent') IS NULL
ALTER TABLE recipes.Outbox ADD TraceParent varchar(55) NULL;
