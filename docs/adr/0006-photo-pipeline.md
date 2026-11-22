# ADR-0006: Photos: valet-key upload, Event Grid → Service Bus → Function, served by the gateway

**Status:** Accepted (2026-11-22)

## Context
A recipe can have a photo of the dish, taken on a phone: 2–8 MB of JPEG, with EXIF metadata that often includes the
GPS position of the kitchen. Phones show photos in lists (small) and in the cook view (full width), offline too.

The work differs from everything else in the system. It is asynchronous, CPU- and memory-heavy, and bursty, and it
happens a few times a month. That is the boundary ADR-0001 gives Media.

## Decision
1. **Upload with a valet key.** The gateway (`POST /api/photos/upload-slot`) issues a **user delegation SAS**: signed with
   a key Entra gives the gateway's managed identity, so the storage account has no keys at all
   (`allowSharedKeyAccess: false`).
   - The link may create and write one blob, `uploads/{new guid}`, for 10 minutes (`sp=cw`). The phone PUTs the file
     directly.
   - Storage CORS allows PUT from the app's origin only, and the CSP's `connect-src` names only the storage origin.
   - A SAS can't limit size, so Media checks it.
2. **Events, not polling.** BlobCreated is delivered as follows:
   - It goes to an Event Grid **system topic** with a filter: event type BlobCreated, subject beginning with
     `…/containers/uploads/blobs/`.
   - From there it goes to a **Service Bus queue** `photo-uploads`, delivered under Event Grid's own identity, with
     retries and dead-lettering to a blob.
   - Media, a **Flex Consumption Function**, triggers on the queue.
   - Why a queue and not Event Grid's webhook straight to the Function: the queue buffers while Media is cold or
     failing, gives a dead-letter queue to alert on, and needs no public webhook. It can also be fed by hand in tests.
3. **Processing** (`Koch.Media.Imaging`, SkiaSharp, MIT licence):
   - Accept JPEG, PNG and WebP up to 15 MB and 50 megapixels. The pixel limit is checked from the header, before
     decoding, as a guard against decompression bombs.
   - Apply the EXIF orientation to the pixels, then encode WebP renditions at 400 and 1200 px (never larger than the
     original).
   - Because everything is encoded anew from pixels, **no metadata survives, GPS included**.
   - The upload is deleted only after the renditions are written. Renditions are only created, never overwritten
     (since ADR-0008, which keeps their tags on them); repeating the work gives the same bytes, so
     at-least-once delivery is harmless (DDIA ch12 p.493).
   - A file that isn't a usable photo is dropped and not retried. Storage errors throw, so Service Bus delivers again
     and finally dead-letters.
4. **Serving.** `GET /photos/{id}/{width}.webp` is answered by the gateway from `photos/`, for members only, with
   `Cache-Control: private, max-age=31536000, immutable`.
   - Same origin, so the CSP keeps `img-src 'self'` and the service worker can cache photos (its own `photos` cache,
     which survives releases).
   - The blob layout is Media's published contract (`Koch.Media.Contracts.PhotoPaths`). Media is the only writer; the
     gateway only reads (Newman ch4 p.101-103, communication through common data, one way).
5. **Recipes reference photos by id** (`Recipe.Photo`). That changed the synced item, so the sync protocol went to 2
   and the local schema to 2 (contract samples v2; v1 samples still read).
6. **Least privilege by role and scope** (`infra/modules/photos.bicep`, `messaging.bicep`):

   | Identity | Rights |
   |---|---|
   | Gateway | delegator on the account; contributor on `uploads/` only; reader on `photos/` only |
   | Media | blob data owner, for the Functions host's own state and deployment package; receiver on the queue |
   | Event Grid | sender on the queue; contributor on `deadletter/` |

## Consequences
- Uploading a photo needs a connection; the recipe itself still saves offline. Between upload and processing, which
  includes a cold start, the app shows the local preview, then a placeholder until the rendition exists.
- Photos removed from recipes are deleted 30 days later, through outbox events and blob tags (ADR-0008).
- Local development has no Event Grid or Functions host. `tools/Koch.DevPhotos` runs Media's own `PhotoProcessor`
  against Azurite instead.
- Bicep now creates role assignments, so the deploy identity needs Role Based Access Control Administrator, limited to
  these data roles (docs/deployment.md).

## Compliance
- `Koch.Media.Tests`:
  - a fixture JPEG with EXIF description and GPS section: none of it may appear in any rendition;
  - orientation; no upscaling; refusal of non-images, decompression bombs and oversize files;
  - the processor's order (write, then delete), duplicate delivery and refusal;
  - BlobCreated parsing, refusing other containers, other events, path tricks and garbage.
- `Koch.Gateway.Tests`: a slot for members only; renditions for members only and immutable; unknown widths and
  unprocessed photos return 404; the CSP's `connect-src`.
- `Koch.App.Tests`: the upload goes to the slot URL with `x-ms-blob-type: BlockBlob`; offline gives a reason and the
  recipe still saves; the placeholder; removing a photo.
- `Koch.Architecture.Tests`: Media references only its contracts and imaging; the gateway and app only
  `Koch.Media.Contracts`.
