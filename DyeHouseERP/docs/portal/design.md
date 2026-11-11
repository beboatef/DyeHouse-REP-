# Customer Portal — Security Design (Phase 1)

Status: **DESIGN ONLY — not approved for build.** Phase 2 must not start until a human
writes "design approved" in the task thread. The portal is read-only in this release;
there are no write endpoints by design, and nothing here weakens any staff-side guard.

## 0. Background and scope

DyeHouseERP today has a single trust domain: staff users authenticate with a JWT access
token carried in the `dyehouse_at` HttpOnly cookie (read from the cookie by the
`JwtBearer` handler in `Program.cs`), a rotating refresh token in `dyehouse_rt`
(SHA-256 hash stored server-side in `UserRefreshTokens`), and a double-submit CSRF
cookie `dyehouse_csrf`. Authorisation is a flat granular permission string carried as a
role claim (`Permission:` + value, resolved dynamically by `PermissionPolicyProvider`;
`admin` bypasses everything) and enforced server-side on every controller action. Login
is throttled per (IP, username) and per IP by `LoginThrottleMiddleware` (POST
`/api/auth/login` only, 429 with `Retry-After`, username normalisation, 4 KB body cap,
forwarded headers only from an explicit proxy allow-list).

The portal opens a **second, untrusted trust domain**: people outside the company
(customers) reading their own commercial documents over the internet. The governing
principle is **deny by default, scope by server-side claim, and never confirm or deny
the existence of another customer's data** (404, never 403, for cross-customer IDs).

### Endpoint inventory (all read-only)

| # | Method & path | Purpose |
|---|---------------|---------|
| P1 | `POST api/portal/auth/login` | Step 1: username + password |
| P2 | `POST api/portal/auth/mfa` | Step 2: TOTP code → issues portal session |
| P3 | `POST api/portal/auth/refresh` | Rotates the portal session |
| P4 | `POST api/portal/auth/logout` | Revokes the portal session family |
| P5 | `GET api/portal/orders` | Own job orders, paged, optional status filter |
| P6 | `GET api/portal/orders/{id}` | One own job order |
| P7 | `GET api/portal/deliveries` | Own deliveries, paged |
| P8 | `GET api/portal/deliveries/{id}` | One own delivery |
| P9 | `GET api/portal/invoices` | Own invoices, paged |
| P10 | `GET api/portal/invoices/{id}` | One own invoice (JSON mirrors the PDF) |
| P11 | `GET api/portal/invoices/{id}/pdf` | Existing report-export PDF for one own invoice |
| P12 | `GET api/portal/statement` | Own customer statement (`?from&to`, plus `…/pdf`) |
| A1 | `POST api/portal-accounts` | Staff: create + link a portal account (permission `portal.manage`) |
| A2 | `POST api/portal-accounts/{id}/disable` | Staff: disable an account, revoke its sessions |
| A3 | `POST api/portal-accounts/{id}/enable` | Staff: re-enable an account |

GUIDs are the resource identifiers in URLs (as on the staff API). Human document numbers
(`OrderNumber`, `DeliveryNumber`, `InvoiceNumber`) are display fields, never lookup keys.

---

## 1. Threat model (STRIDE per endpoint group)

### P1/P2 — portal login + MFA (highest-risk surface)

- **Spoofing.** Attacker impersonates a customer with stolen credentials. Mitigations:
  mandatory TOTP as a second factor (Section 4); PBKDF2 password storage reused from
  staff auth; identical error shape and timing for unknown username vs wrong password
  (decoy-hash pattern already used by staff login); account lockout via the same
  per-(IP, username) and per-IP throttle dimensions as `LoginThrottleMiddleware`.
- **Tampering.** Credential stuffing at scale. Mitigations: 429 with `Retry-After` on
  the login path (same envelope shape as staff, revealing nothing about which half
  failed); 4 KB body cap; no user enumeration in messages or timing.
- **Repudiation.** "I never logged in." Mitigations: every P1/P2 attempt (success and
  failure) is an `AuditLogEntry` (Login action) with account id, IP, user agent and
  outcome — the existing interceptor path is reused so coverage cannot be forgotten.
- **Information disclosure.** Username enumeration, TOTP-secret leakage. Mitigations:
  P1 never confirms whether a username exists (it only answers "proceed to MFA step"
  vs the generic failure, and the "proceed" answer is also given for disabled accounts
  up to the same throttle budget — see Session fixation below); TOTP secrets are
  generated server-side, shown once at enrolment, stored encrypted at rest, never
  returned again.
- **Denial of service.** Password-hash burning (PBKDF2 at 600k iterations is CPU-heavy
  by design). Mitigations: throttle-before-hash (locked-out requests are rejected
  before any hashing, as staff does); per-IP budget; the in-memory/SQL throttle store
  decision from B3 applies here unchanged.
- **Elevation of privilege.** Reusing a portal session against staff endpoints, or a
  staff token against portal endpoints. Mitigations: separate audience
  (`DyeHouseERP.PortalClients`), separate cookie names, separate refresh table; the
  staff `JwtBearer` handler rejects the portal audience and the portal handler
  rejects the staff audience (negative tests in Phase 2 prove both directions).
- **Session fixation (named risk).** An attacker plants a session identifier in the
  victim's browser, the victim logs in, the attacker rides the now-authenticated
  session. Mitigations (all mandatory):
  1. Login (P2 success) **always mints a fresh session**: a new refresh-token family
     and a newly generated session id; any pre-login portal cookie presented at P1/P2
     is ignored, never adopted.
  2. The MFA step is bound to the P1 attempt (single-use, short-lived challenge id
     kept server-side, not in a client-readable cookie).
  3. Re-authentication (password + TOTP) is required after any privilege-relevant
     change is not applicable (read-only), but refresh rotation (P3) replaces the
     presented token on every use and reuse of a rotated token burns the family —
     the same semantics as the staff `dyehouse_rt` rotation already in production.

### P5–P12 — read endpoints (authenticated portal session)

- **Spoofing / customer-to-customer leakage through shared IDs (named risk).** The
  classic IDOR: customer A requests customer B's order by GUID. Mitigations: every
  query is filtered server-side by the customer id taken **from the validated token
  claims, never from the request** (Section 5); any id that is not the caller's own
  record returns **404, not 403**, so existence is neither confirmed nor denied; GUIDs
  are unguessable, and sequential document numbers are never accepted as lookup keys.
- **Enumeration of order/invoice numbers (named risk).** `OrderNumber`,
  `DeliveryNumber`, `InvoiceNumber` are human-sequential and therefore enumerable.
  Mitigations: numbers are display-only (no endpoint resolves them); list endpoints
  are paged and scoped to the caller; a burst of 404s from one session/IP is a
  monitored signal (Section 6) and trips the same per-IP throttle that protects
  login; PDFs carry a per-download watermark with the account identity so a leaked
  file is attributable.
- **Tampering.** There is nothing to tamper with: no POST/PUT/PATCH/DELETE exists in
  the portal tree, and the portal scheme is not authorised on any staff route, so a
  portal token cannot reach a mutating endpoint even if the path is guessed.
- **Repudiation.** "I never saw that invoice." Mitigations: `PortalAccessLog` records
  every portal data access (account, endpoint, record id, timestamp, outcome) with a
  retention window (Section 7); the log is append-only through the same interceptor
  path as `AuditLogEntry`.
- **Information disclosure.** Over-broad DTOs (the staff `InvoiceDto` carries
  `CostEntryId`; `DeliveryDto` carries internal yield/org fields). Mitigations:
  dedicated portal DTOs defined field-by-field in Section 2 — staff DTOs are never
  returned from portal endpoints, enforced by a Phase 2 security test that scans
  every portal response for the excluded field list.
- **Denial of service.** PDF generation is CPU-heavy. Mitigations: per-session rate
  limit on P11/P12-pdf (429 with `Retry-After`); PDFs are generated on demand and
  never stored; no bulk-export endpoint in this release.
- **Elevation of privilege.** A portal session used against `/api/*` staff routes.
  Mitigations: portal tokens lack every staff permission claim and carry the portal
  audience; staff authorisation requires `Permission:*` claims the portal issuer
  never mints; negative tests prove rejection in both directions.

### A1–A3 — internal account administration (staff scheme, `portal.manage`)

- Same STRIDE profile as any other staff admin endpoint: `portal.manage` permission
  enforced server-side via the existing `PermissionPolicy` mechanism; all three
  actions write `AuditLogEntry` rows (Create/Update with reason); A2 revokes all live
  portal sessions for the account immediately (disable must equal instant lockout —
  a disabled account's refresh tokens are revoked, not left to expire).
- **Separation of duties:** creating an account never sets its password. A1 creates
  the account in `MustEnrol` state and returns a single-use, short-lived enrolment
  token that staff passes to the customer out of band; the customer sets the password
  and enrols TOTP through it. Staff therefore never know customer credentials, and a
  leaked admin screen cannot yield usable logins.

### Portal frontend (separate route tree, Arabic-first)

- Spoofing (phishing): the portal lives under `/portal/*` with its own layout, no
  navigation link to or from staff pages, and no shared login screen — a staff
  credential entered in the portal can never authenticate (different scheme), and the
  portal login page states this explicitly.
- XSS/session theft: portal session cookies are HttpOnly + Secure + SameSite=Lax
  (Strict breaks nothing here — no cross-site flows — so Strict is preferred, to be
  confirmed against the SMS/email link UX in open questions); the double-submit CSRF
  pattern is reused for the state-changing auth endpoints (login/refresh/logout) even
  though data endpoints are GETs; no token is ever readable from JavaScript.
- Information disclosure: the portal bundle must not import staff modules
  (a Phase 2 sweep asserts the `src/portal` tree has no imports from staff pages or
  the staff permission list); error messages are generic; 404s carry no hints.

---

## 2. Data scope

All queries are additionally filtered to non-deleted, non-cancelled-where-applicable
rows, exactly as the staff list endpoints are. Amounts and quantities are shown in the
same units and rounding as the staff screens and PDFs — the portal never recomputes
money, it projects stored documents.

### 2.1 Job orders (P5, P6) — per order

Exposed: `OrderNumber`, `OrderDate`, `Status`, `ItemCode`, `ItemName`, `Color`,
`RequestedQuantityKg`, `RequestedQuantityMeter`, `CustomerReference`, current
production stage name (if any), and the list of its deliveries/invoices (id + number +
date + status only, linking to P8/P10).

NOT exposed: internal notes, priority, job type internals, cost fields of any kind
(estimated/approved cost, cost entries, chargeability flags), employee assignments,
machine/shift data, raw-material allocations and lot identities, rework/separate
details, approval history.

### 2.2 Deliveries (P7, P8) — per delivery

Exposed: `DeliveryNumber`, `DeliveryDate`, `Status`, lines (`ItemCode`, `ItemName`,
`Color`, `QuantityKg`, `QuantityMeter`, `PieceCount`), and the invoices raised against
it (id + number + date).

NOT exposed: `OriginalRawQuantityKg/Meter` and remaining-original-raw figures (factory
yield internals), sub-customer linkage (`PrimaryDeliveryId`, `PrimaryCustomerId` — a
portal account linked to customer X sees only documents whose `CustomerId` is X;
sub-customer permits are visible only to the sub-customer's own account), internal
notes, warehouse identity beyond its display name.

### 2.3 Invoices (P9, P10, P11) — per invoice

Exposed: `InvoiceNumber`, `InvoiceDate`, `Status`, linked delivery (number + date),
lines (`ItemCode`, `ItemName`, `Color`, `Unit`, `Quantity`, `ProcessingPrice`,
`Value`), `Discount`, `Tax`, `Total`, staff-visible `Notes` (these print on the PDF
the customer already receives, so the JSON mirrors the PDF exactly — no more, no
less).

NOT exposed: `CostEntryId` and everything behind it, cost-bearer decisions
(`IsChargeableToCustomer`), internal billing-guard diagnostics, payment allocation
state beyond what the statement shows.

### 2.4 Statement (P12)

Exposed: opening balance, period charges (per invoice: date, number, amount),
period payments received (date, payment method label, amount — **no treasury account
code or name**), closing balance. Optional `…/pdf` renders the same content.

### 2.5 Explicitly NEVER exposed through the portal

- Prices of other customers (price lists, other customers' invoice lines or totals).
- Internal costs of any kind (cost entries, estimates, approvals, labour cost,
  service-price configuration beyond the customer's own billed lines).
- Cost-bearer decisions (`IsChargeableToCustomer` and any "company-borne" flag).
- Employee data (names, attendance, payroll, advances — no HR entity is queryable).
- Treasury accounts (codes, names, balances; payments are shown without account
  identity).
- Cheque details of any party (the `Check`/`CheckMovement` entities are entirely out
  of portal scope).
- Audit log entries and portal access logs.
- Other customers' existence: names, codes, counts, and any error text that differs
  between "not yours" and "does not exist".
- Document sequence counters and numbering state.

---

## 3. Identity model

- `PortalAccount` is a **new entity, not a `User` row**: `Id`, `Username` (unique,
  case-insensitive), `PasswordHash` (PBKDF2, same hasher), `TotpSecret`
  (encrypted at rest), `CustomerId` (**required, exactly one, immutable after
  creation** — re-linking a live account is forbidden; link errors are fixed by
  disabling the account and creating a new one, so history stays attributable),
  `DisplayName` (contact person), `IsActive`, `MustEnrol`, audit fields
  (`CreatedBy/At`, `DisabledBy/At`, `DisabledReason`), row version for concurrency.
- Creation (A1) requires an internal user holding the new `portal.manage` permission
  (mirrored in the frontend permission list like every other permission). The request
  carries `CustomerId` + `Username` + `DisplayName`; the response carries a
  single-use enrolment token (15-minute expiry), never a password.
- One account per human contact; **customers never receive a shared login**. A shared
  mailbox as username is rejected at creation (the username must identify the
  contact person; whether to additionally require a verified email is open question
  Q4).
- Disable (A2) sets `IsActive = false`, records by/reason/at, and revokes every live
  portal refresh family for the account in the same transaction. Disabled accounts
  fail P1 with the generic login error (no "account disabled" signal to anonymous
  callers; the distinction is visible only to staff). Enable (A3) requires fresh
  enrolment if the TOTP secret was wiped at disable time (decision: wipe on disable —
  see Q5).
- A portal account is linked to a `Customer` with `IsActive = true` at creation; if
  the customer master is later deactivated, portal logins for its accounts are
  refused until it is reactivated (defence in depth alongside A2).

## 4. Authentication — decision: password + mandatory TOTP (no magic links)

**Decision: password plus mandatory TOTP second factor, verified at every login.**
Magic-link (passwordless email) was considered and rejected:

1. The deployment has no verified secure-mail channel: customer mailboxes in this
   market are routinely shared, forwarded, and retained by third-party providers —
   a magic link is a single-factor bearer token travelling through infrastructure
   the ERP does not control, and links leak through prefetchers, scanners and
   forwarded threads.
2. Revocation is weaker: a compromised mailbox means unlimited re-issue, whereas a
   password+TOTP compromise requires two independent secrets and TOTP cannot be
   "re-sent".
3. The server already owns a reviewed password stack (PBKDF2 with stored iteration
   count, rehash-on-login, decoy-hash for unknown users, throttle-before-hash);
   reusing it is strictly less new cryptography than a token-mailer.
4. TOTP works offline and needs no SMS gateway, no per-message cost, and no PII
   beyond what the account already holds.

- **Separate scheme, end to end.** Portal tokens use audience
  `DyeHouseERP.PortalClients` (staff uses the staff audience); cookies
  `dyehouse_portal_at` / `dyehouse_portal_rt` (staff: `dyehouse_at` / `dyehouse_rt`);
  refresh state in a new `PortalRefreshToken` table mirroring `UserRefreshTokens`
  (hash-only storage, rotation, family burn on reuse). The portal authentication
  handler is registered **only** on `api/portal/**` controllers; the staff handler
  is registered everywhere else. Cross-use is rejected by audience validation
  before any claim is read.
- **Lifetimes:** portal access 15 minutes, refresh 12 hours sliding to a 7-day
  absolute cap (shorter than staff: external devices are less trustworthy; exact
  values are deployment settings, defaults here). Enrolment token: single use,
  15 minutes. TOTP: 30-second step, ±1 step skew, 5 failed codes consume the login
  attempt budget.
- **MFA is mandatory, not opt-in.** There is no "skip MFA" path, no "remember this
  device" cookie in this release (see Q6), and TOTP enrolment is part of account
  activation — an account in `MustEnrol` state cannot call P5–P12.

## 5. Authorisation — server-side customer scoping

Every portal data endpoint resolves the customer id **exclusively from the validated
portal token's customer claim** (populated at login from `PortalAccount.CustomerId`
and re-checked against the account's live `IsActive` on each request — a disabled
account loses access mid-session). The client never supplies a customer id in any
form (route, query, or body — there is no such parameter to bind).

Example query shape (all portal reads follow this pattern; `customerId` comes from
the claim, and the id-lookup-then-scope-check is a single query so there is no
check/use gap):

```csharp
// customerId: from User.FindFirst("customer_id") — never from the route/query/body.
var order = await _db.ProductionOrders.AsNoTracking()
    .Where(o => o.Id == id && o.CustomerId == customerId && !o.IsDeleted)
    .Select(o => new PortalOrderDto { /* Section 2.1 fields only */ })
    .FirstOrDefaultAsync(cancellationToken)
    ?? throw new NotFoundException("ProductionOrder", id);   // 404 either way
```

Rules: single-query predicate (`CustomerId == customerId`), projection to a portal
DTO at the query (staff DTOs are never materialised for portal calls), 404 for
"not yours" and "does not exist" alike, no `IgnoreQueryFilters` on portal paths,
period-close rules do not apply to reads.

## 6. Rate limiting, audit, monitoring

- **Rate limiting:** P1/P2 reuse the staff throttle dimensions and response shape
  (per-(IP, account) and per-IP budgets, 429 + `Retry-After`, throttle evaluated
  before password hashing). P3/P4 are bound to a valid session cookie and additionally
  capped per account. P11/P12-pdf have a stricter per-session budget (expensive
  rendering). Login throttle store (in-memory default / SQL when multi-instance)
  covers portal logins through the same `ILoginAttemptStore` abstraction.
- **Audit (non-repudiation):** account lifecycle (A1/A2/A3), enrolment, every login
  attempt and outcome, logouts, and refresh-family burns are `AuditLogEntry` rows
  via the existing interceptor path (portal event types prefixed `Portal…`).
- **Access monitoring:** `PortalAccessLog` (append-only: account id, endpoint, record
  id, timestamp, HTTP outcome class — no document contents, no PII beyond the
  account reference) backs two alerts: (a) a burst of 404s from one session/IP
  (enumeration probing — auto-throttle the IP and page the on-call), (b) repeated
  login attempts against disabled accounts or high refresh-reuse burns (possible
  session theft). Dashboards: portal logins/day, 404-rate by endpoint, disabled-
  account attempts. Logs feed the same sink as staff audit entries (no new pipeline).

## 7. Data residency and retention

- **Residency:** no new data store. `PortalAccount`, `PortalRefreshToken` and
  `PortalAccessLog` live in the same database, same region, same backup policy as
  the ERP. TOTP secrets are encrypted at rest (DPAPI/Secrets-backed key, never in
  backups as plaintext — the key stays out of the database). PDFs are rendered on
  demand and never persisted. The portal frontend is served from the same origin as
  the staff app (no new domain, no cross-origin session dance); brutal scope
  separation is by route tree and token audience, not by host (see Q7 for the
  dedicated-subdomain option and why it is deferred).
- **Retention:** expired portal refresh families are purged 30 days after expiry
  (same job pattern as staff token cleanup); `PortalAccessLog` is retained 12 months
  rolling, then deleted (configurable; legal minimum to be confirmed in Q8);
  `AuditLogEntry` rows follow the existing staff retention unchanged. Disabled
  accounts and their history are retained (never hard-deleted — they are the audit
  trail of who saw what).

## 8. Open questions the business must answer before build

- **Q1 — Statement history depth.** How many years back may a portal account read
  invoices/statements? (Default proposed: full history — confirm.)
- **Q2 — Sub-customer visibility.** A portal account linked to a parent customer:
  parent documents only (proposed), or roll up sub-customer permits too?
- **Q3 — Who may request an account?** Self-service request with staff approval, or
  staff-created only (proposed: staff-created only for this release)?
- **Q4 — Contact email.** Is a per-contact verified email required at creation (for
  lockout/disable notifications), or is the username a plain identifier?
- **Q5 — Disable semantics.** Wipe the TOTP secret on disable (forces re-enrolment
  on enable — proposed) or keep it?
- **Q6 — "Remember this device".** Explicitly out of scope for this release — confirm
  no exception for named VIP accounts.
- **Q7 — Hosting.** Same-origin route tree (proposed) vs a dedicated `portal.*`
  subdomain with its own cookie domain (stronger cookie isolation, extra DNS/TLS
  work) — which does the business want for go-live?
- **Q8 — Access-log retention.** Is 12 months rolling acceptable to legal, or is a
  longer minimum required?
- **Q9 — PDF watermark.** Confirm every portal PDF is watermarked with the account
  identity and download timestamp (proposed: yes, non-negotiable for traceability).
- **Q10 — Price visibility on old invoices.** If a service price changes, old invoice
  PDFs/lines keep their historical figures (immutable documents — proposed: yes,
  confirm no "current price" display is wanted anywhere in the portal).
- **Q11 — Support contact.** Which phone/email is shown on the portal login page for
  locked-out customers, and in which language first?
- **Q12 — Language default.** Portal is Arabic-first; confirm Arabic is the default
  locale with English toggle (mirroring staff), and who signs off the portal string
  list for i18n parity.
