# Monitoring and Alerting — Runbook

The API tells you it is sick in three ways: structured logs (Serilog, one
enricher — `RequestLogEnricher` — attaching `RequestId`, `UserName` when
authenticated, `Route` template, `StatusCode`; elapsed ms comes from Serilog's
own request-completed event), stable business-event names (below), and
OpenTelemetry meters (exported via OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT`
is set; otherwise the instruments are a no-op and nothing leaves the process).

What is deliberately NEVER logged: passwords, tokens, cookies, `Authorization`
headers, or request bodies on auth routes. The enricher only reads the endpoint
metadata, the status code and the already-authenticated `ClaimsPrincipal`; the
login request is unauthenticated, so it contributes no `UserName` at all. The
unit suite (`MonitoringTests`) pins this down — if a future change starts
logging a secret, that test fails first.

## 1. Health endpoints

| Endpoint | Auth | Meaning |
|---|---|---|
| `GET /health` | Anonymous | Liveness only: "this process answers". Runs NO checks — a dead database must never flip the liveness probe and trigger a restart loop. Platform liveness/readiness probes that cannot present a token must use this. |
| `GET /health/ready` | `users.manage` permission (admin JWT) | Readiness: database reachable AND no pending EF migration AND formation stage + the four system warehouses (`WH-RAW`, `WH-RDY`, `WH-MAT`, `WH-SUP`) present. Use for deploy verification and authenticated monitoring. |

Choice documented: `/health/ready` sits behind the **admin permission**, not an
IP allow-list, because this deployment has no fixed prober IPs (orchestrator
pods move) while the permission model (`Permission:…` policies) is already the
server-enforced gate for everything else — one mechanism, no second network
config to drift. If your platform prober cannot present a token, point the
platform probe at `/health` (liveness) and check `/health/ready` from your
deploy pipeline or monitoring agent with a service token. If you later add an
ingress IP allow-list for monitoring, keep the permission check too (defence in
depth) and note the range here.

## 2. Business events (`EventName` property — alerts match on these)

Event values are constants in `LogEvents`; renaming one is a breaking change
for every alert below, and `MonitoringTests.EventNames_Are_Stable_Constants`
fails the build if a value drifts.

| EventName | Level | Meaning | Suggested alert |
|---|---|---|---|
| `LOCK_TIMEOUT` | Warning (409) | A stock/document lock (`sp_getapplock`) timed out — someone else holds the same stock dimension. Clients retry; the server is healthy but contended. | **> 5 in 10 min** → investigate the contended resource; sustained timeouts mean two flows serialize on one lock. |
| `CONCURRENT_UPDATE` | Warning (409) | RowVersion conflict — a user worked on stale data and the second writer lost. Normal under concurrency. | **> 20 in 10 min** on one route → UX problem (form held open too long) or a retry storm. |
| `NegativeStockOverrideCreated` | Warning | A negative-stock movement posted under a consumed single-use approval (`RequestedBy` ≠ `ApprovedBy`, recorded). A guard was deliberately bypassed with authorisation. | **Any single occurrence** → notify the shift lead; review reason + approver daily. |
| `LoginThrottleTripped` | Warning (429) | The login throttle rejected an attempt. No username/IP is logged (the 429 reveals nothing, the log must not either). | **> 10 in 10 min** → possible credential-stuffing; check WAF/rate-limit upstream. Single trips are normal user error. |
| `PeriodClosed` | Information | An accounting period was closed (who + range in the event). | No alert; dashboard count per week. A close outside month-end hours deserves a glance. |
| `PeriodReopened` | Warning | A closed period was reopened — postings into sealed history are possible again. | **Any single occurrence** → page the finance owner; reopening must be rare and explained. |
| `ApprovalConsumed` | Information | A single-use approval was matched and marked Approved (document number, requester, approver). | No alert; join with `NegativeStockOverrideCreated` in reviews (every override must have exactly one consumed approval). |
| `RefreshTokenRevokedForPasswordChange` | Warning | Password changed/reset → all refresh tokens of that user revoked (`reason=PasswordChanged`). | **Burst (> 5 users in 10 min)** → possible mass-reset or compromise response; single events are routine. |
| `DatabaseConnectionFailed` | Error/Critical | The API cannot reach its database (startup probe, health check, or readiness check). Startup logs Critical and refuses to serve rather than serving 500s. | **Any occurrence** → page immediately; nothing else the system does works without the database. |

## 3. Metrics (`DyeHouseERP` meter, OTLP when configured)

| Instrument | Type | Tags | Use |
|---|---|---|---|
| `dyehouse.request.duration` (ms) | Histogram | `route` (template, e.g. `/api/customers/{id}`) | p95 latency per route; alert on 2× baseline over 15 min. |
| `dyehouse.request.5xx` | Counter | `route` | **Any sustained > 0** → page; 5xx means an unhandled failure reached the client. |
| `dyehouse.stock.lock_wait` (ms) | Histogram | `lock.mode` (`Shared`/`Exclusive`) | Rising p95 here predicts `LOCK_TIMEOUT` before it fires. |
| `dyehouse.stock.lock_timeout` | Counter | `lock.mode` | Same alert as the `LOCK_TIMEOUT` event; the counter survives log sampling. |

Tags use the matched route template, never concrete ids — cardinality stays
bounded. No backend SDK is referenced: the exporter speaks plain OTLP to
whatever collector `OTEL_EXPORTER_OTLP_ENDPOINT` points at
(e.g. `http://otel-collector:4318`); unset = local process only.

## 4. Example queries

Seq (SQL-like, properties are first-class):

```sql
-- Lock contention spike
select count(*) from stream where EventName = 'LOCK_TIMEOUT' and @Timestamp > Now() - 10m
-- Every override with its authorisation, last 24h
select RequestedBy, ApprovedBy, RawMessageId, Requested, Available from stream
 where EventName = 'NegativeStockOverrideCreated' and @Timestamp > Now() - 24h
-- Reopened periods (should usually return nothing)
select PeriodStart, PeriodEnd, ReopenedBy, Reason from stream
 where EventName = 'PeriodReopened' and @Timestamp > Now() - 7d
-- Throttle trips with request context
select count(*), Route from stream
 where EventName = 'LoginThrottleTripped' and @Timestamp > Now() - 10m group by Route
-- Slowest routes, p95
select Route, percentile(Elapsed, 0.95) from stream
 where @Timestamp > Now() - 1h group by Route order by percentile(Elapsed, 0.95) desc
```

Elasticsearch (KQL / ES|QL style, assuming ECS-ish `labels.EventName`):

```kql
labels.EventName : "LOCK_TIMEOUT" and @timestamp > now-10m
labels.EventName : "PeriodReopened" and @timestamp > now-7d
```

```sql
-- ES|QL: p95 request duration by route, last hour
FROM logs-dyehouse-*
| WHERE @timestamp > NOW() - 1 HOUR
| STATS p95(Elapsed) BY Route
| SORT p95(Elapsed) DESC
```

## 5. What to do when an alert fires

1. `LOCK_TIMEOUT` storm → find the contended resource in the event (`Method` +
   `Path` + the lock resource in the message), check for a stuck session holding
   `sp_getapplock` (`sys.dm_exec_requests`), kill or wait, then review why two
   flows serialize (see `docs/operations/backup-and-restore.md` drill for load
   context only — never restore over production to "fix" contention).
2. `DatabaseConnectionFailed` → check SQL Server reachability/credentials first
   (the startup message names the configured server, never the password), then
   recent deploys (a pending-migration `/health/ready` failure means the deploy
   forgot `dotnet ef database update`).
3. `PeriodReopened` / `NegativeStockOverrideCreated` → human review, not a
   rollback: both are audited, authorised acts. Confirm the approver and reason,
   then close the loop with the finance owner.
