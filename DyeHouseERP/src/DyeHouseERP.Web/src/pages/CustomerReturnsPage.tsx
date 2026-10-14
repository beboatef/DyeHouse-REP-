import { Fragment, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomerReturnsApi, CustomersApi, ItemsApi, RawMessagesApi, ProductionOrdersApi, ProductionStagesApi,
  type CustomerReturn, type CustomerReturnLineInput, type CostBearer, type ReturnOwnership
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Field, Badge } from "@/components/ui";
import { TableStateRow, errorMessage } from "@/components/ui";
import { useI18n } from "@/i18n";
import { PackageOpen, Plus, Trash2, ChevronDown, ChevronUp } from "lucide-react";

/**
 * Customer returns of processed goods (spec sections 32-33).
 *
 * Returned material goes back to the RAW MATERIAL warehouse - never Ready Goods -
 * and the document posts real IN ledger rows, which is what makes it allocatable
 * for a later Job Order.
 *
 * The rule the UI has to make impossible to get wrong: the target raw lot is
 * mandatory when the originating Job Order is unknown. When a Job Order IS given
 * the lot may be left blank, because the API derives it from that order's own
 * allocations. The client mirrors the rule for fast feedback; the API enforces it.
 */

interface LineDraft {
  productionOrderId: string;
  rawMessageId: string;
  itemId: string;
  quantityKg: string;
  quantityMeter: string;
  notes: string;
}

const emptyLine: LineDraft = {
  productionOrderId: "", rawMessageId: "", itemId: "", quantityKg: "", quantityMeter: "", notes: ""
};

const today = () => new Date().toISOString().slice(0, 10);

export default function CustomerReturnsPage() {
  const { t } = useI18n();
  const qc = useQueryClient();

  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [returnDate, setReturnDate] = useState(today());
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  // The document the goods come back against, and whose material they are (spec section 32).
  const [referenceDocument, setReferenceDocument] = useState("");
  const [ownership, setOwnership] = useState<ReturnOwnership>("CustomerOwned");
  const [lines, setLines] = useState<LineDraft[]>([{ ...emptyLine }]);
  const [formError, setFormError] = useState<string | null>(null);

  // ---- New Job Order from a return (spec section 33) ----
  // The return is only REFERENCED: it is never converted, and no invoice, credit note or charge is
  // touched. The account choice is mandatory and starts EMPTY on purpose - the screen must not
  // pre-answer who pays for the rework.
  const [orderFor, setOrderFor] = useState<string | null>(null);
  const [orderLine, setOrderLine] = useState({ itemId: "", rawMessageId: "", quantityKg: "", quantityMeter: "", color: "", notes: "" });
  const [orderCostBearer, setOrderCostBearer] = useState<CostBearer | "">("");
  // The stage the new Job Order actually begins at (spec section 33). Returned material is reworked
  // from the operation it failed at, so starting later than formation is the normal case. Only
  // ACTIVE stages are offered and empty keeps the formation start; the raw issue - the single stock
  // deduction - is identical either way.
  const [orderStartingStageId, setOrderStartingStageId] = useState("");
  const [orderError, setOrderError] = useState<string | null>(null);

  const [filterCustomerId, setFilterCustomerId] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [search, setSearch] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  // Correcting a return entered in error (wrong lot, quantity or customer) is a cancellation with a
  // mandatory reason - never an edit and never a delete. Refused by the backend if the returned
  // material has already been used, and it says exactly which lot and how much.
  const [cancelFor, setCancelFor] = useState<string | null>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [cancelError, setCancelError] = useState<string | null>(null);

  const { data: returns, isLoading, error } = useQuery({
    queryKey: ["customer-returns", filterCustomerId, fromDate, toDate, search],
    queryFn: () => CustomerReturnsApi.list({
      customerId: filterCustomerId || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
      search: search || undefined
    })
  });

  const { data: customers } = useQuery({
    queryKey: ["customers", "active"],
    queryFn: () => CustomersApi.list({ activeOnly: true })
  });

  const { data: items } = useQuery({
    queryKey: ["items", "active"],
    queryFn: () => ItemsApi.list({ activeOnly: true })
  });

  // Only lots that actually hold a balance can receive a return.
  const { data: lots } = useQuery({
    queryKey: ["raw-messages", "with-balance"],
    queryFn: () => RawMessagesApi.list({ onlyWithBalance: true })
  });

  const { data: orders } = useQuery({
    queryKey: ["production-orders", "for-returns"],
    queryFn: () => ProductionOrdersApi.list()
  });

  const { data: stages } = useQuery({
    queryKey: ["production-stages", "active"],
    queryFn: () => ProductionStagesApi.list({ activeOnly: true })
  });

  /** Lots and orders are narrowed to the return's customer - stock is customer-owned. */
  const customerLots = useMemo(
    () => (lots ?? []).filter((l) => !customerId || l.customerId === customerId),
    [lots, customerId]
  );
  const customerOrders = useMemo(
    () => (orders ?? []).filter((o) => !customerId || o.customerId === customerId),
    [orders, customerId]
  );

  const createMutation = useMutation({
    mutationFn: () => {
      const payload = {
        customerId,
        returnDate,
        reason: reason.trim() || null,
        notes: notes.trim() || null,
        referenceDocument: referenceDocument.trim() || null,
        ownership,
        lines: lines.map<CustomerReturnLineInput>((l) => ({
          itemId: l.itemId,
          rawMessageId: l.rawMessageId || null,
          productionOrderId: l.productionOrderId || null,
          quantityKg: l.quantityKg ? Number(l.quantityKg) : null,
          quantityMeter: l.quantityMeter ? Number(l.quantityMeter) : null,
          notes: l.notes.trim() || null
        }))
      };
      return CustomerReturnsApi.create(payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-returns"] });
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      qc.invalidateQueries({ queryKey: ["dashboard-summary"] });
      resetForm();
    },
    onError: (err) => setFormError(errorMessage(err, t("common.error")))
  });

  const cancelMutation = useMutation({
    mutationFn: (input: { id: string; reason: string }) => CustomerReturnsApi.cancel(input.id, input.reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-returns"] });
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      qc.invalidateQueries({ queryKey: ["dashboard-summary"] });
      setCancelFor(null);
      setCancelReason("");
      setCancelError(null);
    },
    onError: (err) => setCancelError(errorMessage(err, t("common.error")))
  });

  /**
   * Raises an independent Job Order from a return. Refused locally when the account is not chosen,
   * mirroring the API's rule: there is deliberately no default assumption about who pays.
   */
  const orderMutation = useMutation({
    mutationFn: (returnId: string) =>
      CustomerReturnsApi.createProductionOrder(returnId, {
        itemId: orderLine.itemId,
        rawMessageId: orderLine.rawMessageId,
        orderDate: today(),
        actualQuantityKg: orderLine.quantityKg ? Number(orderLine.quantityKg) : null,
        actualQuantityMeter: orderLine.quantityMeter ? Number(orderLine.quantityMeter) : null,
        costBearer: orderCostBearer as CostBearer,
        color: orderLine.color.trim() || null,
        notes: orderLine.notes.trim() || null,
        requestId: typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : null,
        startingStageId: orderStartingStageId || null
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-returns"] });
      qc.invalidateQueries({ queryKey: ["production-orders"] });
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      setOrderFor(null);
      setOrderLine({ itemId: "", rawMessageId: "", quantityKg: "", quantityMeter: "", color: "", notes: "" });
      setOrderCostBearer("");
      setOrderStartingStageId("");
      setOrderError(null);
    },
    onError: (err) => setOrderError(errorMessage(err, t("common.error")))
  });

  const resetForm = () => {
    setShowForm(false);
    setCustomerId("");
    setReturnDate(today());
    setReason("");
    setNotes("");
    setReferenceDocument("");
    setOwnership("CustomerOwned");
    setLines([{ ...emptyLine }]);
    setFormError(null);
  };

  const updateLine = (index: number, patch: Partial<LineDraft>) =>
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)));

  /** A lot's own item lines, so the item picker cannot name an item the lot never carried. */
  const itemOptionsFor = (line: LineDraft) => {
    const lot = lots?.find((l) => l.id === line.rawMessageId);
    if (!lot) return items ?? [];
    return lot.lines.map((lr) => ({ id: lr.itemId, code: lr.itemCode, name: lr.itemName }));
  };

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    if (!customerId) {
      setFormError(t("returns.customerRequired"));
      return;
    }
    if (lines.length === 0) {
      setFormError(t("returns.linesRequired"));
      return;
    }

    for (const line of lines) {
      if (!line.itemId) {
        setFormError(t("returns.itemRequired"));
        return;
      }
      if (!line.quantityKg && !line.quantityMeter) {
        setFormError(t("returns.quantityRequired"));
        return;
      }
      // The rule from spec section 32: no Job Order means the lot is mandatory.
      if (!line.productionOrderId && !line.rawMessageId) {
        setFormError(t("returns.lotRequiredWhenNoOrder"));
        return;
      }
    }

    createMutation.mutate();
  };

  const lineTotals = (ret: CustomerReturn) => {
    const kg = ret.lines.reduce((sum, l) => sum + (l.quantityKg ?? 0), 0);
    const meter = ret.lines.reduce((sum, l) => sum + (l.quantityMeter ?? 0), 0);
    return { kg, meter };
  };

  return (
    <>
      <PageHeader
        title={t("returns.title")}
        subtitle={t("returns.subtitle")}
        action={
          <Button onClick={() => (showForm ? resetForm() : setShowForm(true))}>
            <Plus size={16} />
            {showForm ? t("common.cancel") : t("returns.new")}
          </Button>
        }
      />

      {showForm && (
        <Card className="mb-6 p-5">
          <form onSubmit={submit}>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Field label={t("common.customer")} required>
                <Select value={customerId} onChange={(e) => {
                  setCustomerId(e.target.value);
                  // Lots and orders belong to one customer; a stale pick would be wrong.
                  setLines([{ ...emptyLine }]);
                }} required>
                  <option value="">{t("returns.selectCustomer")}</option>
                  {customers?.map((c) => (
                    <option key={c.id} value={c.id}>{c.code} — {c.name}</option>
                  ))}
                </Select>
              </Field>

              <Field label={t("common.date")} required>
                <Input type="date" value={returnDate} onChange={(e) => setReturnDate(e.target.value)} required />
              </Field>

              {/* The document the goods come back against - free text, since the goods may come back
                  against a document this system never issued (an invoice number, a delivery permit,
                  or the customer's own reference). Descriptive only: a return changes no invoice. */}
              <Field label={t("returns.referenceDocument", "المستند المرجعي")}>
                <Input value={referenceDocument} onChange={(e) => setReferenceDocument(e.target.value)}
                  maxLength={100} placeholder={t("returns.referenceDocumentHint", "فاتورة / إذن تسليم / مرجع العميل")} />
              </Field>

              <Field label={t("returns.ownership", "ملكية المادة")}>
                <Select value={ownership} onChange={(e) => setOwnership(e.target.value as ReturnOwnership)}>
                  <option value="CustomerOwned">{t("returns.customerOwned", "مادة العميل")}</option>
                  <option value="CompanyOwned">{t("returns.companyOwned", "مادة الشركة")}</option>
                </Select>
              </Field>

              <Field label={t("common.reason")}>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500}
                  placeholder={t("returns.reasonOptional")} />
              </Field>

              <Field label={t("common.notes")}>
                <Input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
              </Field>
            </div>

            {/* ---- Lines ---- */}
            <div className="mt-5 overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    <th>{t("returns.jobOrderOptional")}</th>
                    <th>{t("returns.lot")}</th>
                    <th>{t("common.item")}</th>
                    <th>{t("common.kg")}</th>
                    <th>{t("common.meter")}</th>
                    <th className="text-end">{t("common.actions")}</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((line, index) => (
                    <tr key={index}>
                      <td>
                        <Select
                          value={line.productionOrderId}
                          onChange={(e) => updateLine(index, { productionOrderId: e.target.value })}
                          disabled={!customerId}
                        >
                          <option value="">{t("returns.notKnown")}</option>
                          {customerOrders.map((o) => (
                            <option key={o.id} value={o.id}>{o.orderNumber}</option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Select
                          value={line.rawMessageId}
                          onChange={(e) => updateLine(index, { rawMessageId: e.target.value, itemId: "" })}
                          disabled={!customerId}
                        >
                          <option value="">{line.productionOrderId ? t("returns.derivedFromOrder") : t("returns.selectLot")}</option>
                          {customerLots.map((l) => (
                            <option key={l.id} value={l.id}>{l.messageNumber}</option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Select
                          value={line.itemId}
                          onChange={(e) => updateLine(index, { itemId: e.target.value })}
                          disabled={!customerId}
                        >
                          <option value="">{t("returns.selectItem")}</option>
                          {itemOptionsFor(line).map((i) => (
                            <option key={i.id} value={i.id}>{i.code} — {i.name}</option>
                          ))}
                        </Select>
                      </td>
                      <td>
                        <Input type="number" min="0" step="any" className="w-28"
                          value={line.quantityKg}
                          onChange={(e) => updateLine(index, { quantityKg: e.target.value, quantityMeter: "" })} />
                      </td>
                      <td>
                        <Input type="number" min="0" step="any" className="w-28"
                          value={line.quantityMeter}
                          onChange={(e) => updateLine(index, { quantityMeter: e.target.value, quantityKg: "" })} />
                      </td>
                      <td className="text-end">
                        <button
                          type="button"
                          className="btn-link text-danger"
                          disabled={lines.length === 1}
                          onClick={() => setLines((prev) => prev.filter((_, i) => i !== index))}
                        >
                          <Trash2 size={14} />
                          {t("common.delete")}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="mt-4 flex flex-wrap items-center gap-3">
              <Button variant="secondary" onClick={() => setLines((prev) => [...prev, { ...emptyLine }])}>
                <Plus size={14} />
                {t("returns.addLine")}
              </Button>
              <Button type="submit" disabled={createMutation.isPending}>
                {createMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              <Button variant="secondary" onClick={resetForm}>{t("common.cancel")}</Button>
            </div>

            <p className="kpi-footnote mt-3">{t("returns.lotRuleHint")}</p>
            {formError && <p className="mt-3 text-sm text-danger">{formError}</p>}
          </form>
        </Card>
      )}

      {/* ---- Filters ---- */}
      <Card className="mb-4 p-4 sm:p-5">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <Field label={t("common.customer")}>
            <Select value={filterCustomerId} onChange={(e) => setFilterCustomerId(e.target.value)}>
              <option value="">{t("common.all")}</option>
              {customers?.map((c) => (
                <option key={c.id} value={c.id}>{c.code} — {c.name}</option>
              ))}
            </Select>
          </Field>
          <Field label={t("common.from")}>
            <Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
          </Field>
          <Field label={t("common.to")}>
            <Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} />
          </Field>
          <Field label={t("app.search")}>
            <Input value={search} onChange={(e) => setSearch(e.target.value)}
              placeholder={t("returns.searchPlaceholder")} />
          </Field>
        </div>
      </Card>

      <Card>
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>{t("returns.number")}</th>
                <th>{t("common.date")}</th>
                <th>{t("common.customer")}</th>
                <th>{t("returns.quantities")}</th>
                <th>{t("common.reason")}</th>
                <th>{t("common.createdBy")}</th>
                <th className="text-end">{t("common.details")}</th>
              </tr>
            </thead>
            <tbody>
              <TableStateRow
                colSpan={7}
                loading={isLoading}
                error={error}
                isEmpty={returns?.length === 0}
                loadingText={t("common.loading")}
                emptyText={t("returns.empty")}
              />
              {returns?.map((ret) => {
                const totals = lineTotals(ret);
                const expanded = expandedId === ret.id;
                return (
                  <Fragment key={ret.id}>
                    <tr className="cursor-pointer"
                      onClick={() => setExpandedId(expanded ? null : ret.id)}>
                      <td className="ltr-nums font-medium">
                        {ret.returnNumber}
                        {ret.isCancelled && <span className="ms-2"><Badge tone="red">{t("returns.statusCancelled")}</Badge></span>}
                      </td>
                      <td className="ltr-nums text-ink-muted">
                        {new Date(ret.returnDate).toLocaleDateString("en-GB")}
                      </td>
                      <td>{ret.customerCode} — {ret.customerName}</td>
                      <td className="ltr-nums">
                        {totals.kg > 0 && <span>{totals.kg} {t("common.kg")}</span>}
                        {totals.kg > 0 && totals.meter > 0 && " · "}
                        {totals.meter > 0 && <span>{totals.meter} {t("common.meter")}</span>}
                        <div className="text-2xs text-ink-subtle">
                          {t("returns.lineCount")}: {ret.lines.length}
                        </div>
                      </td>
                      <td className="text-ink-muted">
                        {ret.reason ?? "—"}
                        <div className="text-2xs text-ink-subtle">
                          {ret.referenceDocument ? `${t("returns.referenceDocument", "المستند المرجعي")}: ${ret.referenceDocument} · ` : ""}
                          {ret.ownership === "CompanyOwned"
                            ? t("returns.companyOwned", "مادة الشركة")
                            : t("returns.customerOwned", "مادة العميل")}
                        </div>
                      </td>
                      <td className="text-ink-muted">{ret.createdBy}</td>
                      <td className="text-end">
                        <span className="btn-link">
                          {expanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                        </span>
                      </td>
                    </tr>

                    {expanded && (
                      <tr>
                        <td colSpan={7} className="bg-surface-sunken">
                          <div className="table-wrap">
                            <table className="table">
                              <thead>
                                <tr>
                                  <th>{t("returns.lot")}</th>
                                  <th>{t("common.item")}</th>
                                  <th>{t("returns.jobOrder")}</th>
                                  <th>{t("returns.basin")}</th>
                                  <th>{t("common.kg")}</th>
                                  <th>{t("common.meter")}</th>
                                </tr>
                              </thead>
                              <tbody>
                                {ret.lines.map((l) => (
                                  <tr key={l.id}>
                                    <td className="ltr-nums">{l.messageNumber}</td>
                                    <td>{l.itemCode} — {l.itemName}</td>
                                    <td className="ltr-nums">
                                      {l.productionOrderNumber ?? (
                                        <Badge tone="neutral">{t("returns.notKnown")}</Badge>
                                      )}
                                    </td>
                                    <td className="ltr-nums">
                                      {l.formationGroupNumber ?? "—"}
                                    </td>
                                    <td className="ltr-nums">{l.quantityKg ?? "—"}</td>
                                    <td className="ltr-nums">{l.quantityMeter ?? "—"}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>

                          {/* ---- Job Orders raised from this return ---- */}
                          {ret.productionOrders.length > 0 && (
                            <div className="mx-3 mb-2 rounded-lg border border-line bg-white px-4 py-2 text-sm">
                              <span className="text-ink-subtle">{t("returns.raisedOrders", "أوامر تشغيل أُنشئت من هذا المرتجع")}: </span>
                              {ret.productionOrders.map((o) => (
                                <span key={o.productionOrderId} className="ltr-nums me-3">
                                  {o.orderNumber}
                                  <span className="text-2xs text-ink-subtle">
                                    {o.costBearer === "Company"
                                      ? ` (${t("returns.onCompanyAccount", "على حساب الشركة")})`
                                      : ` (${t("returns.onCustomerAccount", "على حساب العميل")})`}
                                  </span>
                                </span>
                              ))}
                            </div>
                          )}

                          {ret.isCancelled ? (
                            <div className="mx-3 mb-3 rounded-lg border border-red-200 bg-red-50/50 px-4 py-3 text-sm text-red-900">
                              <div className="font-semibold">{t("returns.cancel.title")}</div>
                              <div className="ltr-nums text-xs text-red-800">
                                {ret.cancelledBy} · {ret.cancelledAtUtc ? new Date(ret.cancelledAtUtc).toLocaleString("en-GB") : ""}
                              </div>
                              <div className="mt-1" dir="auto">{ret.cancellationReason}</div>
                            </div>
                          ) : cancelFor === ret.id ? (
                            <div className="mx-3 mb-3 rounded-lg border border-red-200 bg-red-50/60 p-4">
                              <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
                                <span className="text-sm font-semibold text-red-900">{t("returns.cancel.title")}</span>
                                <span className="text-xs text-red-800">{t("returns.cancel.hint")}</span>
                              </div>
                              <div className="mt-3">
                                <Field label={t("returns.cancel.reason")}>
                                  <Input
                                    value={cancelReason}
                                    maxLength={1000}
                                    onChange={(e) => setCancelReason(e.target.value)}
                                  />
                                </Field>
                              </div>
                              <div className="mt-3 flex items-center gap-2">
                                <Button
                                  variant="danger"
                                  disabled={!cancelReason.trim() || cancelMutation.isPending}
                                  onClick={() => cancelMutation.mutate({ id: ret.id, reason: cancelReason.trim() })}
                                >
                                  {cancelMutation.isPending ? t("common.saving") : t("returns.cancel.submit")}
                                </Button>
                                <Button variant="ghost" onClick={() => setCancelFor(null)}>{t("common.cancel")}</Button>
                                {cancelError && <span className="form-error">{cancelError}</span>}
                              </div>
                            </div>
                          ) : (
                            <div className="flex flex-wrap items-center gap-2 px-3 pb-3">
                              <Button
                                variant="ghost"
                                onClick={() => { setCancelFor(ret.id); setCancelReason(""); setCancelError(null); }}
                              >
                                {t("returns.cancel")}
                              </Button>
                              <Button
                                variant="secondary"
                                onClick={() => {
                                  setOrderFor(orderFor === ret.id ? null : ret.id);
                                  setOrderLine({ itemId: "", rawMessageId: "", quantityKg: "", quantityMeter: "", color: "", notes: "" });
                                  setOrderCostBearer("");
                                  setOrderStartingStageId("");
                                  setOrderError(null);
                                }}
                              >
                                {t("returns.newOrderFromReturn", "أمر تشغيل جديد من المرتجع")}
                              </Button>
                            </div>
                          )}

                          {/* Raising an independent Job Order from the returned material: the SAME
                              canonical workflow (issuing the raw) creates the order and posts its real
                              stock movement. The account choice is mandatory and starts empty. */}
                          {!ret.isCancelled && orderFor === ret.id && (
                            <div className="mx-3 mb-3 rounded-lg border border-line bg-surface-sunken p-4">
                              <div className="text-sm font-semibold">{t("returns.newOrderFromReturn", "أمر تشغيل جديد من المرتجع")}</div>
                              <p className="kpi-footnote mb-3">{t("returns.newOrderHint")}</p>

                              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3 lg:grid-cols-5">
                                <Field label={t("returns.lot", "رقم الرسالة (الخام)")} required>
                                  <Select value={orderLine.rawMessageId}
                                    onChange={(e) => setOrderLine((p) => ({ ...p, rawMessageId: e.target.value, itemId: "" }))}>
                                    <option value="">{t("returns.selectLot")}</option>
                                    {Array.from(new Map(ret.lines.map((l) => [l.rawMessageId, l.messageNumber])).entries())
                                      .map(([lotId, number]) => <option key={lotId} value={lotId}>{number}</option>)}
                                  </Select>
                                </Field>

                                <Field label={t("common.item")} required>
                                  <Select value={orderLine.itemId}
                                    onChange={(e) => setOrderLine((p) => ({ ...p, itemId: e.target.value }))}>
                                    <option value="">{t("returns.selectItem")}</option>
                                    {ret.lines
                                      .filter((l) => !orderLine.rawMessageId || l.rawMessageId === orderLine.rawMessageId)
                                      .map((l) => (
                                        <option key={l.id} value={l.itemId}>{l.itemCode} — {l.itemName}</option>
                                      ))}
                                  </Select>
                                </Field>

                                <Field label={t("common.kg")}>
                                  <Input type="number" min="0" step="any" value={orderLine.quantityKg}
                                    onChange={(e) => setOrderLine((p) => ({ ...p, quantityKg: e.target.value, quantityMeter: "" }))} />
                                </Field>
                                <Field label={t("common.meter")}>
                                  <Input type="number" min="0" step="any" value={orderLine.quantityMeter}
                                    onChange={(e) => setOrderLine((p) => ({ ...p, quantityMeter: e.target.value, quantityKg: "" }))} />
                                </Field>
                                <Field label={t("returns.color", "اللون")}>
                                  <Input value={orderLine.color} onChange={(e) => setOrderLine((p) => ({ ...p, color: e.target.value }))} />
                                </Field>

                                {/* Where the rework actually starts (spec section 33). Optional: empty
                                    keeps the ordinary formation start, and the API refuses a stage that
                                    is not an existing, active one. */}
                                <Field label={t("prod.startingStage", "مرحلة البداية")}>
                                  <Select value={orderStartingStageId} onChange={(e) => setOrderStartingStageId(e.target.value)}>
                                    <option value="">{t("prod.startingStageDefault", "التشكيل (الافتراضي)")}</option>
                                    {(stages ?? []).map((st) => (
                                      <option key={st.id} value={st.id}>{st.code} - {st.name}</option>
                                    ))}
                                  </Select>
                                </Field>
                              </div>

                              <p className="kpi-footnote mt-2">{t("prod.startingStageHint")}</p>

                              {/* The account choice. No option is preselected: the user MUST say who
                                  carries the cost of the rework, and the API refuses a missing choice. */}
                              <div className="mt-3">
                                <span className="field-label">{t("returns.costBearer", "تحمّل التكلفة")} *</span>
                                <div className="mt-1 flex flex-wrap gap-4 text-sm">
                                  <label className="flex items-center gap-2">
                                    <input type="radio" name={`bearer-${ret.id}`} value="Customer"
                                      checked={orderCostBearer === "Customer"}
                                      onChange={() => setOrderCostBearer("Customer")} />
                                    {t("returns.onCustomerAccount", "على حساب العميل")}
                                  </label>
                                  <label className="flex items-center gap-2">
                                    <input type="radio" name={`bearer-${ret.id}`} value="Company"
                                      checked={orderCostBearer === "Company"}
                                      onChange={() => setOrderCostBearer("Company")} />
                                    {t("returns.onCompanyAccount", "على حساب الشركة")}
                                  </label>
                                </div>
                              </div>

                              <div className="mt-3 flex flex-wrap items-center gap-2">
                                <Button
                                  disabled={
                                    orderMutation.isPending || !orderLine.itemId || !orderLine.rawMessageId ||
                                    (!orderLine.quantityKg && !orderLine.quantityMeter) || orderCostBearer === ""
                                  }
                                  onClick={() => orderMutation.mutate(ret.id)}
                                >
                                  {orderMutation.isPending ? t("common.saving") : t("returns.createOrder", "إنشاء أمر التشغيل")}
                                </Button>
                                <Button variant="ghost" onClick={() => setOrderFor(null)}>{t("common.cancel")}</Button>
                                {orderCostBearer === "" && (
                                  <span className="text-2xs text-warning-ink">{t("returns.costBearerRequired")}</span>
                                )}
                                {orderError && <span className="form-error">{orderError}</span>}
                              </div>
                            </div>
                          )}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>

        <div className="flex items-center gap-2 border-t border-line px-4 py-3 text-2xs text-ink-subtle">
          <PackageOpen size={14} />
          {t("returns.footerNote")}
        </div>
      </Card>
    </>
  );
}
