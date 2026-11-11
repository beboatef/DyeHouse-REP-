# Core workflows — endpoints and ledger effects

How the five central flows move through the API: which endpoints the client calls,
in what order, and which append-only ledger rows each step writes. Balances are
never stored — every quantity below is a row in `InventoryTransaction`
(fabric), `MaterialTransaction` (chemicals), `CustomerLedgerEntry`,
`SupplierLedgerEntry` or `TreasuryTransaction`; corrections are linked reversal
rows, never edits.

Conventions used in every diagram:

- `API` is `api/...` under the same host; all calls carry the session (cookies or
  `Authorization: Bearer`) and the permission named in the endpoint's summary.
- `409 CONCURRENT_UPDATE` means "reload and retry"; `409 LOCK_TIMEOUT` means
  "someone else holds the stock lock — retry in a moment". Both are normal, not
  server faults (see `docs/api/errors.md`).

## 1. Issue raw material to a job order

The user picks the exact raw message(s) and quantities — there is no FIFO and
nothing is auto-picked. The issue moves fabric from the raw lot into production
(WIP) on the ledger.

```mermaid
sequenceDiagram
    participant U as User (production.create)
    participant API as POST api/production-orders/issue
    participant L as InventoryTransaction ledger
    U->>API: IssueRawMaterial (order, message lines, KG/Meter)
    API->>API: WarehouseGuard (active, RawMaterial kind)<br/>period open, sp_getapplock on stock key
    API->>L: OUT row per message line (raw lot decreases)
    API->>L: IN row for the job order (WIP increases)
    API-->>U: 200 issue result (409 LOCK_TIMEOUT → retry)
```

Net effect: the raw message's live remaining drops; the order's WIP rises by the
same KG/Meter. (Chemical/material issues are reversed with
`POST api/material-issues/{id}/cancel`, which posts a compensating IN row and
keeps the original OUT row — permission `production.issue_material_cancel`.)

## 2. Stage transfer to ready goods

Closing a stage execution moves finished output into the ready-goods store. The
transfer is the ONLY production transaction: every fact (input, output, waste,
separates, second grade, machine, shift) belongs to the operation being closed.

```mermaid
sequenceDiagram
    participant U as User (production.execute_stage)
    participant API as POST api/production-orders/stage-executions/{id}/transfer
    participant L as InventoryTransaction ledger
    U->>API: TransferStage (output KG/Meter, separates,<br/>second grade, machine, shift, notes)
    API->>API: execution open, quantities > 0,<br/>stage approved where required
    API->>L: OUT row (WIP of the closed operation decreases)
    API->>L: IN row, ReadyGoods warehouse (ready stock increases)
    API->>L: IN rows for separates / second grade lots
    API-->>U: 200 transfer result
```

Ready-goods balance per job order is a live sum over these rows
(`GET api/ready-goods/balance`). `POST api/ready-goods/transfers` moves stock
between ready-goods lots without touching production.

## 3. Delivery and invoice

Deliveries run Draft → Prepared → Delivered; delivering deducts ready goods.
Invoices post to the customer ledger on issue; cancelling either posts a linked
reversal referencing the original.

```mermaid
sequenceDiagram
    participant U as User (ready.deliver + invoices.*)
    participant D as api/deliveries
    participant I as api/invoices
    participant L as Ledgers
    U->>D: POST api/deliveries (ready-goods lines)
    D-->>U: 201 Draft delivery
    U->>D: POST api/deliveries/{id}/prepare
    U->>D: POST api/deliveries/{id}/deliver
    D->>L: InventoryTransaction OUT rows (ready goods decreases)
    U->>I: POST api/invoices (delivery / manual lines)
    I-->>U: 201 Draft invoice
    U->>I: POST api/invoices/{id}/issue
    I->>L: CustomerLedgerEntry rows (receivable increases)
    I-->>U: 200 issued invoice
```

Cancellation (`POST .../{id}/cancel` on either) never deletes: it posts the
mirror rows and locks both documents.

## 4. External processing round trip

Customer-owned fabric leaves for a third-party processor and comes back. Each
leg names the source message — ownership stays on the ledger, never by editing
the original message.

```mermaid
sequenceDiagram
    participant U as User (raw.external_release)
    participant API as api/raw-external-releases
    participant L as InventoryTransaction ledger
    U->>API: POST api/raw-external-releases<br/>(message, KG/Meter, party, stage, expected return)
    API->>L: OUT row (mill stock decreases, with-party increases)
    API-->>U: 201 release (expected-return tracked)
    U->>API: POST api/raw-external-releases/{id}/return<br/>(actual KG/Meter, partial or full)
    API->>L: IN row (mill stock increases, with-party decreases)
    API-->>U: 200 return receipt
```

`PUT .../{id}/external-processing` corrects the release terms;
`POST .../{id}/cancel` reverses an unreturned release. A raw sale and a return
to the customer are the same endpoint with a different release reason.

## 5. Approval with separation of duties

Sensitive operations (negative-stock override, cost approval, reprint-after-issue,
...) do not execute directly: they file a request, and a different user decides.
The requester can never approve their own request — enforced in
`ApprovalRequestGuard`, not in the UI.

```mermaid
sequenceDiagram
    participant R as Requester
    participant A as Approver (approvals.view + type right)
    participant API as api/approvals
    R->>API: POST api/approvals (type, payload, reason)
    API-->>R: 201 pending request
    A->>API: GET api/approvals (read-only inbox)
    A->>API: POST api/approvals/{id}/approve
    API->>API: guard: pending + decider ≠ requester<br/>+ decider holds the action permission
    API->>API: execute the approved action, write audit entry
    API-->>A: 200 approval consumed
```

`POST api/approvals/{id}/reject` closes the request without executing.
Approval rights are per action type (e.g. `inventory.approve_negative_stock`),
so the night-shift supervisor can approve stock overrides without holding the
keys to payroll.
