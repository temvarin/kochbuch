# ADR-0002: One gateway signs members in; services trust the user it names

**Status:** Accepted (2026-11-22)

## Context
The PWA has to load without a sign-in, because it works offline. Only the API is restricted to the two household members.

kassensturz solves this with:
- App Service Easy Auth (Microsoft Entra), whose session is a cookie;
- a Member policy over an allowlist of Entra object ids;
- a CSRF defence that relies on JSON-only requests to the same origin.

With several services, each would otherwise need its own sign-in. Newman ch11 (p.377-379, "SSO gateway") puts the identity-provider handshake in one gateway, which passes the principal on downstream.

## Decision
- **Koch.Gateway** is the only app with external ingress. It:
  - serves the PWA;
  - runs Container Apps' built-in authentication (Easy Auth, Entra, same tenant as kassensturz, so the same two object ids) with `AllowAnonymous`;
  - enforces the Member policy on everything under `/api`;
  - forwards `/api/{service}/{path}` to `http://kochbuch-{service}/{path}` with YARP.
- **Forwarding the user.** The proxy deletes every incoming `X-Koch-User`, every `X-MS-*` header and the `Cookie` header. It then sets `X-Koch-User` to the signed-in member's object id. This closes the confused-deputy hole: an authenticated user can't make the gateway speak for someone else (Newman p.380-381).
- **Trust.** Services have internal ingress only, so they are unreachable from outside the environment, which answers with a 404. They believe `X-Koch-User` without checking membership again.
  - This is *implicit trust* inside the environment, chosen on purpose for low-sensitivity data such as recipes (Newman p.366-367, trust spectrum).
  - Traffic between apps is encrypted by the environment's peer encryption.
- **The Easy Auth headers are believed only when `Koch:EasyAuth=true`,** which Bicep sets. kassensturz relies on App Service's `WEBSITE_AUTH_ENABLED`, but Container Apps isn't documented to set that variable.

## Consequences
- All authorization logic sits in one place. A service tested on its own just sends the header (`GatewayUser`, `tests/Koch.Recipes.Tests`).
- If anything else inside the environment were compromised, it could impersonate a member to the services. That is accepted at this data sensitivity.
  - Upgrade path: the gateway gets a token for each service with its managed identity, and the services validate the JWT (zero trust).
- The gateway is a single point of failure and a growing coupling point (Newman p.379). Keep it to sign-in, forwarding, static files and, later, photo SAS and photo serving. No domain logic.

## Compliance
- `tests/Koch.Gateway.Tests`:
  - a spoofed `X-Koch-User` is replaced;
  - Easy Auth headers and the cookie aren't forwarded;
  - strangers never reach a service;
  - principal headers aren't believed without `Koch:EasyAuth`.
- The replacement test was checked by mutation on 2026-11-22: removing the header-strip makes it fail.
- `infra/modules/containerApp.bicep`: `external` is true for the gateway only.
