import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Check,
  CheckDirection,
  ChecksApi,
  CustomersApi,
  SuppliersApi,
  TreasuryAccountsApi
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ChecksExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/**
 * Checks register (spec sections 37-40).
 *
 * A check is ONE financial instrument for its whole life: receiving it from a
 * customer and endorsing it onward to a supplier appends a movement and changes
 * the holder - it never creates cash and never creates a second check. The only
 * transition that touches the bank balance is confirming that it cleared.
 */

const statusTone: Record<Check["status"], "gray" | "yellow" | "blue" | "green" | "red"> = {
  Received: "yellow",
  InHand: "blue",
  Deposited: "blue",
  Endorsed: "blue",
  Cleared: "green",
  Bounced: "red",
  Cancelled: "gray"
};

const today = () => new Date().toISOString().slice(0, 10);

export default function ChecksPage() {
  const { t } = useI18n();
  const qc = useQueryClient();

  const [direction, setDirection] = useState<CheckDirection | "">("");
  const [status, setStatus] = useState<Check["status"] | "">("");
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [incomingForm, setIncomingForm] = useState(false);
  const [outgoingForm, setOutgoingForm] = useState(false);
  const [customerForm, setCustomerForm] = useState({
    checkNumber: "", bankName: "", amount: "", issueDate: today(), dueDate: today(),
    issuer: "", customerId: "", customerReference: "", notes: ""
  });
  const [supplierForm, setSupplierForm] = useState({
    checkNumber: "", bankName: "", amount: "", issueDate: today(), dueDate: today(),
    issuer: "", supplierId: "", treasuryAccountId: "", notes: "", handOverNow: true
  });
  const [actionState, setActionState] = useState<{ id: string | null; supplierId: string; accountId: string; reason: string }>({
    id: null, supplierId: "", accountId: "", reason: ""
  });

  const { data: checks, isLoading } = useQuery({
    queryKey: ["checks", direction, status, overdueOnly, search],
    queryFn: () =>
      ChecksApi.list({
        direction: direction || undefined,
        status: status || undefined,
        overdueOnly: overdueOnly || undefined,
        search: search || undefined
      })
  });

  const { data: summary } = useQuery({ queryKey: ["checks-summary"], queryFn: () => ChecksApi.summary() });
  const { data: customers } = useQuery({ queryKey: ["customers"], queryFn: () => CustomersApi.list() });
  const { data: suppliers } = useQuery({ queryKey: ["suppliers"], queryFn: () => SuppliersApi.list() });
  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });

  const onError = (err: any) =>
    setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"));

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["checks"] });
    qc.invalidateQueries({ queryKey: ["checks-summary"] });
  };

  const registerCustomer = useMutation({
    mutationFn: () =>
      ChecksApi.registerCustomerCheck({
        checkNumber: customerForm.checkNumber,
        bankName: customerForm.bankName,
        amount: Number(customerForm.amount),
        issueDate: customerForm.issueDate,
        dueDate: customerForm.dueDate,
        issuer: customerForm.issuer || customerForm.customerId,
        customerId: customerForm.customerId,
        customerReference: customerForm.customerReference || undefined,
        notes: customerForm.notes || undefined,
        confirmInHandNow: true
      }),
    onSuccess: () => {
      refresh();
      setIncomingForm(false);
      setCustomerForm({ checkNumber: "", bankName: "", amount: "", issueDate: today(), dueDate: today(), issuer: "", customerId: "", customerReference: "", notes: "" });
      setError(null);
    },
    onError
  });

  const registerSupplier = useMutation({
    mutationFn: () =>
      ChecksApi.registerSupplierCheck({
        checkNumber: supplierForm.checkNumber,
        bankName: supplierForm.bankName,
        amount: Number(supplierForm.amount),
        issueDate: supplierForm.issueDate,
        dueDate: supplierForm.dueDate,
        issuer: supplierForm.issuer,
        supplierId: supplierForm.supplierId,
        treasuryAccountId: supplierForm.treasuryAccountId || undefined,
        notes: supplierForm.notes || undefined,
        handOverNow: supplierForm.handOverNow
      }),
    onSuccess: () => {
      refresh();
      setOutgoingForm(false);
      setSupplierForm({ checkNumber: "", bankName: "", amount: "", issueDate: today(), dueDate: today(), issuer: "", supplierId: "", treasuryAccountId: "", notes: "", handOverNow: true });
      setError(null);
    },
    onError
  });

  const movementMutation = useMutation({
    mutationFn: async (vars: { action: string; id: string }) => {
      switch (vars.action) {
        case "confirm":
          return ChecksApi.confirmReceipt(vars.id);
        case "endorse":
          return ChecksApi.endorse(vars.id, { supplierId: actionState.supplierId, reason: actionState.reason || undefined });
        case "deposit":
          return ChecksApi.deposit(vars.id, { treasuryAccountId: actionState.accountId });
        case "clear":
          return ChecksApi.clear(vars.id);
        case "bounce":
          return ChecksApi.bounce(vars.id, actionState.reason || "Bounced by bank");
        case "return":
          return ChecksApi.returnToCompany(vars.id, actionState.reason || undefined);
        case "cancel":
          return ChecksApi.cancel(vars.id, actionState.reason || "Cancelled");
        default:
          throw new Error("Unknown action");
      }
    },
    onSuccess: () => {
      refresh();
      setActionState({ id: null, supplierId: "", accountId: "", reason: "" });
      setError(null);
    },
    onError
  });

  const openActions = (id: string) =>
    setActionState((prev) => ({ ...prev, id: prev.id === id ? null : id, supplierId: "", accountId: "", reason: "" }));

  return (
    <>
      <PageHeader
        title={t("checks.title")}
        subtitle={t("checks.subtitle")}
        action={
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => ChecksApi.exportExcel({ direction: direction || undefined })}>
              {t("common.excel")}
            </Button>
            <Button variant="secondary" onClick={() => ChecksApi.exportPdf({ direction: direction || undefined })}>
              {t("common.pdf")}
            </Button>
            <Button onClick={() => setIncomingForm((s) => !s)}>{t("checks.newIncoming")}</Button>
            <Button variant="secondary" onClick={() => setOutgoingForm((s) => !s)}>{t("checks.newOutgoing")}</Button>
          </div>
        }
      />

      <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-3 mb-6">
        <Kpi label={t("checks.inHand")} value={summary?.inHandAmount ?? 0} />
        <Kpi label={t("checks.deposited")} value={summary?.depositedAmount ?? 0} />
        <Kpi label={t("checks.endorsed")} value={summary?.endorsedAmount ?? 0} />
        <Kpi label={t("checks.cleared")} value={summary?.clearedAmount ?? 0} />
        <Kpi label={t("checks.bounced")} value={summary?.bouncedAmount ?? 0} />
        <Kpi label={t("checks.dueSoon")} value={summary?.dueSoonAmount ?? 0} tone="amber" />
        <Kpi label={t("checks.overdue")} value={summary?.overdueAmount ?? 0} tone="red" />
      </div>

      {incomingForm && (
        <Card className="p-5 mb-6">
          <h3 className="text-sm font-bold text-slate-800 mb-3">{t("checks.newIncoming")}</h3>
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
            <Field label={t("checks.number")}>
              <Input value={customerForm.checkNumber} onChange={(e) => setCustomerForm({ ...customerForm, checkNumber: e.target.value })} />
            </Field>
            <Field label={t("checks.bank")}>
              <Input value={customerForm.bankName} onChange={(e) => setCustomerForm({ ...customerForm, bankName: e.target.value })} />
            </Field>
            <Field label={t("checks.amount")}>
              <Input type="number" value={customerForm.amount} onChange={(e) => setCustomerForm({ ...customerForm, amount: e.target.value })} />
            </Field>
            <Field label={t("checks.issuer")}>
              <Input value={customerForm.issuer} onChange={(e) => setCustomerForm({ ...customerForm, issuer: e.target.value })} />
            </Field>
            <Field label={t("common.customer")}>
              <Select value={customerForm.customerId} onChange={(e) => setCustomerForm({ ...customerForm, customerId: e.target.value })}>
                <option value="">—</option>
                {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
              </Select>
            </Field>
            <Field label={t("checks.issueDate")}>
              <Input type="date" value={customerForm.issueDate} onChange={(e) => setCustomerForm({ ...customerForm, issueDate: e.target.value })} />
            </Field>
            <Field label={t("checks.dueDate")}>
              <Input type="date" value={customerForm.dueDate} onChange={(e) => setCustomerForm({ ...customerForm, dueDate: e.target.value })} />
            </Field>
            <Field label={t("checks.customerReference")}>
              <Input value={customerForm.customerReference} onChange={(e) => setCustomerForm({ ...customerForm, customerReference: e.target.value })} />
            </Field>
          </div>
          <div className="mt-4">
            <Button
              onClick={() => registerCustomer.mutate()}
              disabled={registerCustomer.isPending || !customerForm.checkNumber || !customerForm.bankName || !customerForm.customerId || !customerForm.amount}
            >
              {registerCustomer.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </div>
        </Card>
      )}

      {outgoingForm && (
        <Card className="p-5 mb-6">
          <h3 className="text-sm font-bold text-slate-800 mb-3">{t("checks.newOutgoing")}</h3>
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
            <Field label={t("checks.number")}>
              <Input value={supplierForm.checkNumber} onChange={(e) => setSupplierForm({ ...supplierForm, checkNumber: e.target.value })} />
            </Field>
            <Field label={t("checks.bank")}>
              <Input value={supplierForm.bankName} onChange={(e) => setSupplierForm({ ...supplierForm, bankName: e.target.value })} />
            </Field>
            <Field label={t("checks.amount")}>
              <Input type="number" value={supplierForm.amount} onChange={(e) => setSupplierForm({ ...supplierForm, amount: e.target.value })} />
            </Field>
            <Field label={t("checks.issuer")}>
              <Input value={supplierForm.issuer} onChange={(e) => setSupplierForm({ ...supplierForm, issuer: e.target.value })} />
            </Field>
            <Field label={t("common.supplier")}>
              <Select value={supplierForm.supplierId} onChange={(e) => setSupplierForm({ ...supplierForm, supplierId: e.target.value })}>
                <option value="">—</option>
                {suppliers?.map((s) => <option key={s.id} value={s.id}>{s.code} - {s.name}</option>)}
              </Select>
            </Field>
            <Field label={t("checks.selectAccount")}>
              <Select value={supplierForm.treasuryAccountId} onChange={(e) => setSupplierForm({ ...supplierForm, treasuryAccountId: e.target.value })}>
                <option value="">—</option>
                {accounts?.map((a) => <option key={a.id} value={a.id}>{a.code} - {a.name}</option>)}
              </Select>
            </Field>
            <Field label={t("checks.issueDate")}>
              <Input type="date" value={supplierForm.issueDate} onChange={(e) => setSupplierForm({ ...supplierForm, issueDate: e.target.value })} />
            </Field>
            <Field label={t("checks.dueDate")}>
              <Input type="date" value={supplierForm.dueDate} onChange={(e) => setSupplierForm({ ...supplierForm, dueDate: e.target.value })} />
            </Field>
          </div>
          <div className="mt-4">
            <Button
              onClick={() => registerSupplier.mutate()}
              disabled={registerSupplier.isPending || !supplierForm.checkNumber || !supplierForm.bankName || !supplierForm.supplierId || !supplierForm.amount || !supplierForm.issuer}
            >
              {registerSupplier.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </div>
        </Card>
      )}

      <Card className="p-4 mb-4 grid grid-cols-1 sm:grid-cols-4 gap-3 items-center">
        <Select value={direction} onChange={(e) => setDirection(e.target.value as CheckDirection | "")}>
          <option value="">{t("common.all")}</option>
          <option value="CustomerCheck">{t("checks.direction.CustomerCheck")}</option>
          <option value="SupplierCheck">{t("checks.direction.SupplierCheck")}</option>
        </Select>
        <Select value={status} onChange={(e) => setStatus(e.target.value as Check["status"] | "")}>
          <option value="">{t("common.all")} - {t("common.status")}</option>
          {(["Received", "InHand", "Deposited", "Endorsed", "Cleared", "Bounced", "Cancelled"] as Check["status"][]).map((s) => (
            <option key={s} value={s}>{t(`checks.status.${s}`)}</option>
          ))}
        </Select>
        <Input placeholder={t("common.searchPlaceholder")} value={search} onChange={(e) => setSearch(e.target.value)} />
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={overdueOnly} onChange={(e) => setOverdueOnly(e.target.checked)} />
          {t("checks.overdueOnly")}
        </label>
      </Card>

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>}
        {!isLoading && checks?.length === 0 && <Card className="p-6 text-center text-gray-400">{t("common.empty")}</Card>}
        {checks?.map((check) => (
          <Card key={check.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="text-sm">
                <span className="ltr-nums font-bold">{check.checkNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-gray-600">{check.bankName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="ltr-nums font-medium">{check.amount} {check.currency}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-gray-600">
                  {t("checks.dueDate")}: <span className="ltr-nums">{check.dueDate.slice(0, 10)}</span>
                  {check.daysToDueDate !== null && (
                    <span className={check.daysToDueDate < 0 ? "text-red-600 ms-1" : "text-gray-400 ms-1"}>
                      ({check.daysToDueDate} {t("checks.daysToDue")})
                    </span>
                  )}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <Badge tone={check.direction === "CustomerCheck" ? "blue" : "yellow"}>
                  {t(`checks.direction.${check.direction}`)}
                </Badge>
                <Badge tone={statusTone[check.status]}>{t(`checks.status.${check.status}`)}</Badge>
                <button className="btn-link" onClick={() => openActions(check.id)}>
                  {t("common.actions")}
                </button>
                <button
                  className="btn-link"
                  onClick={ChecksExports.documentPdf(check.id)}
                >
                  {t("common.pdf")}
                </button>
                <button className="btn-link" onClick={() => setExpanded(expanded === check.id ? null : check.id)}>
                  {t("checks.history")}
                </button>
              </div>
            </div>

            <div className="text-[11px] text-slate-500 mt-1.5">
              {t("checks.originalHolder")}: {check.originalHolder} → {t("checks.currentHolder")}: {check.currentHolder}
              {check.customerCode && <> · {t("common.customer")}: <span className="ltr-nums">{check.customerCode}</span></>}
              {check.supplierCode && <> · {t("common.supplier")}: <span className="ltr-nums">{check.supplierCode}</span></>}
            </div>

            {actionState.id === check.id && (
              <div className="mt-3 pt-3 border-t border-slate-100 flex flex-wrap gap-2 items-center">
                {check.status === "Received" && (
                  <Button variant="secondary" onClick={() => movementMutation.mutate({ action: "confirm", id: check.id })}>
                    {t("checks.action.confirmReceipt")}
                  </Button>
                )}
                {(check.status === "Received" || check.status === "InHand" || check.status === "Bounced") && (
                  <>
                    <Select
                      value={actionState.supplierId}
                      onChange={(e) => setActionState({ ...actionState, supplierId: e.target.value })}
                      className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                    >
                      <option value="">{t("checks.selectSupplier")}</option>
                      {suppliers?.map((s) => <option key={s.id} value={s.id}>{s.code} - {s.name}</option>)}
                    </Select>
                    <Button
                      variant="secondary"
                      disabled={!actionState.supplierId}
                      onClick={() => movementMutation.mutate({ action: "endorse", id: check.id })}
                    >
                      {t("checks.action.endorse")}
                    </Button>
                    <Select
                      value={actionState.accountId}
                      onChange={(e) => setActionState({ ...actionState, accountId: e.target.value })}
                      className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                    >
                      <option value="">{t("checks.selectAccount")}</option>
                      {accounts?.map((a) => <option key={a.id} value={a.id}>{a.code} - {a.name}</option>)}
                    </Select>
                    <Button
                      variant="secondary"
                      disabled={!actionState.accountId}
                      onClick={() => movementMutation.mutate({ action: "deposit", id: check.id })}
                    >
                      {t("checks.action.deposit")}
                    </Button>
                  </>
                )}
                {(check.status === "Deposited" || check.status === "Endorsed") && (
                  <>
                    <Button variant="secondary" onClick={() => movementMutation.mutate({ action: "clear", id: check.id })}>
                      {t("checks.action.clear")}
                    </Button>
                    <Input
                      placeholder={t("common.reason")}
                      value={actionState.reason}
                      onChange={(e) => setActionState({ ...actionState, reason: e.target.value })}
                      className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                    />
                    <Button variant="secondary" onClick={() => movementMutation.mutate({ action: "bounce", id: check.id })}>
                      {t("checks.action.bounce")}
                    </Button>
                    <Button variant="ghost" onClick={() => movementMutation.mutate({ action: "return", id: check.id })}>
                      {t("checks.action.return")}
                    </Button>
                  </>
                )}
                {check.status !== "Cleared" && check.status !== "Cancelled" && (
                  <>
                    <Input
                      placeholder={t("common.reason")}
                      value={actionState.reason}
                      onChange={(e) => setActionState({ ...actionState, reason: e.target.value })}
                      className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                    />
                    <Button variant="ghost" onClick={() => movementMutation.mutate({ action: "cancel", id: check.id })}>
                      {t("checks.action.cancel")}
                    </Button>
                  </>
                )}
                <span className="text-[11px] text-slate-400 w-full mt-1">{t("checks.transferNote")} · {t("checks.clearNote")}</span>
              </div>
            )}

            {expanded === check.id && (
              <div className="mt-3 pt-3 border-t border-slate-100 space-y-2">
                {check.movements.length === 0 && <p className="text-xs text-slate-400">{t("common.empty")}</p>}
                {check.movements.map((m) => (
                  <div key={m.id} className="flex flex-wrap items-center gap-2 text-xs text-slate-600">
                    <span className="w-2 h-2 rounded-full bg-brand-500" />
                    <span className="font-semibold">{t(`checks.movement.${m.movementType}`)}</span>
                    <span className="ltr-nums">{m.movementDate.slice(0, 10)}</span>
                    <span>{m.fromHolder} → {m.toHolder}</span>
                    {m.reason && <span className="text-slate-400">({m.reason})</span>}
                    <span className="text-slate-400">{m.createdBy}</span>
                  </div>
                ))}
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}

function Kpi({ label, value, tone = "slate" }: { label: string; value: number; tone?: "slate" | "amber" | "red" }) {
  const color =
    tone === "red" ? "text-red-600" : tone === "amber" ? "text-amber-600" : "text-slate-800";
  return (
    <Card className="p-3">
      <div className="text-[11px] text-slate-500 mb-1 leading-tight">{label}</div>
      <div className={`text-lg font-bold ltr-nums ${color}`}>{value.toLocaleString()}</div>
    </Card>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[11px] font-medium text-gray-600 mb-1">{label}</label>
      {children}
    </div>
  );
}
