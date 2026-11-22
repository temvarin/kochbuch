# ADR-0007: Service Bus Basic queues, no topics; Event Grid for storage events

**Status:** Accepted (2026-11-22)

## Context
Photo processing needs a durable buffer between "a blob was created" and the Function (ADR-0006). Phase 5 adds photo
lifecycle events from Recipes to Media. The budget is the Azure for Students credit minus the database: well under a
dollar a month for everything else.

The options, by tier:
- **Standard** (~$10 a month) has topics and subscriptions with filters, sessions and duplicate detection.
- **Basic** has queues only, with dead-letter queues, at a few cents per million operations.

DDIA ch12 p.492-493: use fan-out (topics) when several independent consumers need the same messages, and a queue when
there is one.

## Decision
- **Service Bus Basic**, one queue per consumer. Today that is `photo-uploads` (from Event Grid); phase 5 adds
  `photo-lifecycle` (from the Recipes outbox, ADR-0008). Each has exactly one consumer, Media. Local auth (SAS keys) is off; only
  managed identities.
- **Event Grid** (system topic on the storage account) for storage events, because storage can't send to Service Bus
  itself. Its subject filter and retries do the routing, so Service Bus gets only what Media needs.
- No Event Grid custom topic in front of Service Bus for application events: with one consumer it would be a second
  broker that buys nothing.

## Consequences
- Basic has no duplicate detection, so consumers must be idempotent (Media is: ADR-0006).
- When a second consumer of the same events appears, move to a Standard topic with filtered subscriptions (about $10 a
  month), or add an Event Grid custom topic for the fan-out. Revisit this ADR then.
- The dead-letter queue is the place to look when a photo never appears. Phase 6 adds an alert on its count.
