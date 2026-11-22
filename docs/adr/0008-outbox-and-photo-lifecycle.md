# ADR-0008: Photo clean-up: transactional outbox, ordered events, tags and a daily sweep

**Status:** Accepted (2026-11-22)

## Context
A recipe refers to its photo by id (ADR-0006). When a recipe gets another photo, loses it, or is deleted, the old
renditions stay in `photos/`. Only Recipes knows whether a photo is still used, and only Media may write and delete
photos.

What makes this harder than it looks:
- **Never delete a photo that is still shown.** Losing a photo is worse than keeping one: a few hundred KB cost
  nothing, a lost photo can't be brought back.
- **Dual writes.** Saving the recipe and then telling Media are two systems. Either can fail after the other has
  succeeded, and concurrent writers can interleave differently in each (DDIA ch12 p.501-502).
- **Order is not guaranteed.** Service Bus delivers at least once (DDIA ch12 p.493). A message that failed comes back
  after newer ones, and Media handles two messages at once. "Detached" followed by a late "attached" from before it
  must not resurrect or doom a photo.
- **The event can come before the photo.** The phone saves the recipe right after uploading, so the photo event can
  reach Media before the BlobCreated of the upload has been processed.
- **"Deleted" must be undoable** for a while: a phone that was offline, or a mistaken removal.

## Decision
1. **Transactional outbox in Recipes** (DDIA ch12 p.507-508).
   - `SqlDocumentRows` (Koch.ServiceDefaults) reads each row under an update lock, writes it if it really changed, and
     asks the service which events the change causes (`RowEvents`). The events go into `recipes.Outbox` **in the same
     transaction**: no event for a change that wasn't saved, no change saved without its event.
   - Recipes' rule is `Rows.PhotoEvents`: a recipe that shows another photo, or none, or is deleted, causes
     `photo.detached` for the old photo and `photo.attached` for the new one.
   - The outbox's rows are the published contract (`Koch.Recipes.Events`), not the documents' shape (p.507).
2. **The relay** (`OutboxRelay` in Recipes) publishes unsent rows in sequence order:
   - after every sync request, at start, and every 5 minutes;
   - marks only the rows Service Bus has accepted, by their numbers, so a row whose transaction committed late is sent
     next round;
   - a crash between sending and marking sends again: at least once.
   - Sent rows stay, with `SentAt`, as a record (a few a month).
   - The service scales to zero, so events saved just before it stops wait for the next request. That is fine for
     clean-up with a 30-day grace period.
3. **CloudEvents** (structured JSON) on the Service Bus Basic queue `photo-lifecycle`, one consumer (ADR-0007).
   - `id` and the CloudEvents `sequence` extension are the outbox row's number, padded to 20 digits so that strings
     compare like numbers.
   - The data carries the photo and the recipe: everything Media needs, nothing to call back for (Newman ch4 p.113).
4. **Media applies an event only if it is newer** than what the photo has: fencing by sequence (DDIA ch9 p.375-376).
   - Each rendition carries blob index tags `state` (new / attached / detached), `seq` and `since`.
   - Tags are set with the condition `"seq" < '<this event's seq>'`. Blob Storage checks and writes in one step
     (conditional headers, DDIA p.376), so neither a late event nor two at once can undo a newer one.
   - Renditions are created with `state=new, seq=0…0` and **only created, never overwritten** (`If-None-Match: *`).
     An upload processed twice, or processed by a photo event that came first, therefore can't reset the tags.
   - If an event finds no renditions, Media processes the upload right there and then tags.
   - Why the ordering holds: two events about the same photo come from the same recipe row. That row stays locked to the
     end of the transaction, so the second transaction's events get higher numbers.
5. **Deleting: a daily timer in Media** (03:30 UTC) finds renditions with `state = 'detached' AND since < today − 30
   days` (Find Blobs by Tags) and deletes each **with the same condition**. A photo attached again between finding and
   deleting stays.
   - Why not a storage lifecycle rule: lifecycle filters test tags only for equality, and setting tags doesn't change a
     blob's modification time. So a rule can say "detached", but not "detached for 30 days".
   - The lifecycle rule still deletes stale `uploads/` after a day and Event Grid's dead letters after 30 days
     (ADR-0006).
6. **Never-attached photos are kept.** An upload whose recipe was never saved stays `new`. Deleting those would also
   delete the photo of a recipe saved offline for weeks; they cost a few hundred KB each. Photos from before this ADR
   are `new` too, or untagged (development data only), and are kept for the same reason.

## Consequences
- Cost: one more Basic queue (cents), one timer run a day (free grant), a few outbox rows a month.
- Clean-up runs 30 days after the detach, or later if Recipes was asleep when it happened.
- The database collation ignores case, and the old `Json <> @Json` in SQL treated "salz" → "Salz" as no change. The
  comparison is now ordinal, in C#. A contract test covers both stores.
- Locally there is no Service Bus: the relay is off and events stay unsent in `recipes.Outbox`, which is where to look.
  `tools/Koch.DevPhotos` processes uploads only.
- Grants are unchanged: the relay needs only SELECT and UPDATE on `recipes`, and both are granted.
- **A second consumer** of photo events, for example search, would need fan-out, i.e. a Standard topic or an Event
  Grid custom topic (ADR-0007), and maybe a replay from the kept outbox rows.

## Compliance
- `Koch.Recipes.Tests`:
  - row store contract on SQL and memory: events saved with their row, nothing for an unchanged row or a no-op
    removal, nothing kept when deriving or saving an event fails or when the rows fail after their events, marking
    sent marks only what was named, case-only changes saved;
  - `PhotoEventTests` for every kind of photo change;
  - relay: everything in order, retried after Service Bus failed, sent again when the marking was lost, woken by a
    ring;
  - `EventContractTests`: Recipes publishes exactly the newest sample.
- `Koch.Media.Tests`:
  - every published sample is read, and malformed events are refused;
  - an older event or a duplicate changes nothing;
  - an event before the upload was processed;
  - processing again keeps the state;
  - the sweep deletes only photos detached more than 30 days ago, and not one attached again mid-sweep;
  - the exact tag conditions.
- `Koch.Architecture.Tests`: `Koch.Recipes.Events` depends on nothing of ours, and Media sees no other Recipes code.
