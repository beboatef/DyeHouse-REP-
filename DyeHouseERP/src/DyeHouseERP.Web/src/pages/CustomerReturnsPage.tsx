import { Fragment, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomerReturnsApi, CustomersApi, ItemsApi, RawMessagesApi, ProductionOrdersApi,
  type CustomerReturn, type CustomerReturnLineInput
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
  const [lines, setLines] = useState<LineDraft[]>([{ ...emptyLine }]);
  const [formError, setFormError] = useState<string | null>(null);

  const [filterCustomerId, setFilterCustomerId] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [search, setSearch] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

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

  const resetForm = () => {
    setShowForm(false);
    setCustomerId("");
    setReturnDate(today());
    setReason("");
    setNotes("");
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
                      <td className="ltr-nums font-medium">{ret.returnNumber}</td>
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
                      <td className="text-ink-muted">{ret.reason ?? "—"}</td>
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
