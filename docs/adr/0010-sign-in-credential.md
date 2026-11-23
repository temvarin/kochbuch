# ADR-0010: How Easy Auth proves itself to Entra: a federated managed identity, a client secret as fallback, no Key Vault

**Status:** Proposed (2026-11-22). Decided on the first deployment, when step 2.4 of `docs/deployment.md` shows whether
the managed identity works.

## Context
The gateway's built-in authentication (Easy Auth, ADR-0002) signs people in with the Entra app registration `kochbuch`.
To redeem a sign-in it must prove to Entra that it is that app. Two ways exist:
- **A client secret** on the app registration. It expires (at most two years) and must be renewed by hand; whoever
  holds it can act as the app.
- **A federated credential** that trusts the gateway's user-assigned managed identity (`id-kochbuch-gateway`). Container
  Apps' Easy Auth uses it when the secret it is pointed at is named `override-use-mi-fic-assertion-client-id` and holds
  the identity's client id. Nothing secret exists anywhere, and nothing expires.

The second is newer, and whether it works for Container Apps in this tenant can only be seen by signing in once.

## Decision
- **Default: the federated managed identity.** `authCredential = managedIdentity` (infra/main.bicep). The only
  "secret" Bicep stores is the identity's client id, which is not confidential.
- **Fallback: a client secret.** If the sign-in fails with the federated credential, set
  `KOCH_AUTH_CREDENTIAL=clientSecret` and the GitHub secret `KOCH_SIGNIN_CLIENT_SECRET`. CI passes it to Bicep as a
  `@secure()` parameter, and Bicep stores it as a Container App secret. To rotate it:
  1. create a new secret on the app registration;
  2. update the GitHub secret;
  3. re-run the deployment;
  4. delete the old secret.
- **No Key Vault, in either case.**
  - With the managed identity there is nothing to keep.
  - With one secret whose source is GitHub, Key Vault would be a second copy with its own role assignment and access
    path, and would add no protection that the Container App secret doesn't already give: encrypted at rest, never
    shown after it is set, readable only by the app.
  - Revisit if a second secret appears, or if rotation should happen without a deployment.

## Consequences
- When this ADR is accepted, its status line records which credential the first deployment confirmed. If it was the
  secret, the expiry date goes in the calendar.
- The gateway's identity is trusted for sign-in by the app registration. Anyone who can run code as that identity can
  sign people in as the app, which is the same trust the identity already has for reading photos.

## Compliance
- `infra/main.bicep`: `authCredential` allows only the two values; the secret parameter is `@secure()`.
- The CI smoke checks see the sign-in only indirectly (401 without it). Signing in on a phone once is the real check,
  listed in `docs/deployment.md`.
