import { Fragment, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomersApi,
  MaterialsApi,
  MaterialSalesApi,
  TreasuryAccountsApi,
  WarehousesApi,
  type MaterialSale,
  type MaterialSaleStatus,
  type MaterialUnit
} from "@/api/client";
import { Badge, Button, Card, Input, PageHeader, Select } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { MaterialSalesExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/**
 * Sales of FACTORY-OWNED materials/chemicals to third parties (spec section 26).
 *
 * Kept separate from job-work invoicing: these documents move factory stock and
 * raise a receivable (or take cash on delivery). Posting is the only step with
 * a stock or financial effect, and it does all three in one transaction.
 */
export default function MaterialSalesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();

  const [statusFilter, setStatusFilter] = useState<"" | MaterialSaleStatus>("");
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  const [warehouseId, setWarehouseId] = useState("");
  const [buyerName, setBuyerName] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [treasuryAccountId, setTreasuryAccountId] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("");
  const [discount, setDiscount] = useState("");
  const [tax, setTax] = useState("");
  const [saleDate, setSaleDate] = useState(new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");

  const [materialId, setMaterialId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unitPrice, setUnitPrice] = useState("");

  const { data: sales, isLoading } = useQuery({
    queryKey: ["material-sales", statusFilter],
    queryFn: () => MaterialSalesApi.list({ status: statusFilter || undefined })
  });

  const { data: warehouses } = useQuery({
    queryKey: ["warehouses"],
    queryFn: () => WarehousesApi.list()
  });
  const { data: customers } = useQuery({ queryKey: ["customers"], queryFn: () => CustomersApi.list() });
  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });

  // Only chemicals can be sold - the API refuses supply items, so the picker
  // never offers one that would be rejected.
  const { data: chemicals } = useQuery({
    queryKey: ["materials", "chemicals"],
    queryFn: () => MaterialsApi.list({ activeOnly: true, kind: "Chemical" })
  });

  const create = useMutation({
    mutationFn: () =>
      MaterialSalesApi.create({
        saleDate,
        warehouseId,
        buyerName,
        customerId: customerId || undefined,
        treasuryAccountId: treasuryAccountId || undefined,
        paymentMethod: paymentMethod || undefined,
        discount: discount ? Number(discount) : 0,
        tax: tax ? Number(tax) : 0,
        notes: notes || undefined
      }),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["material-sales"] });
      setShowForm(false);
      setBuyerName("");
      setNotes("");
      setError(null);
      setExpanded(created.id);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const addLine = useMutation({
    mutationFn: (saleId: string) =>
      MaterialSalesApi.addLine(saleId, {
        materialId,
        quantity: Number(quantity),
        unitPrice: unitPrice ? Number(unitPrice) : undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["material-sales"] });
      setMaterialId("");
      setQuantity("");
      setUnitPrice("");
      setError(null);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const removeLine = useMutation({
    mutationFn: (vars: { id: string; lineId: string }) => MaterialSalesApi.removeLine(vars.id, vars.lineId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["material-sales"] })
  });

  const post = useMutation({
    mutationFn: (id: string) => MaterialSalesApi.post(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["material-sales"] }),
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const cancel = useMutation({
    mutationFn: (id: string) => MaterialSalesApi.cancel(id, window.prompt(t("common.reason")) || ""),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["material-sales"] }),
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  const statusTone = (s: MaterialSaleStatus) => (s === "Posted" ? "green" : s === "Cancelled" ? "red" : "yellow");
  const statusLabel = (s: MaterialSaleStatus) =>
    s === "Posted" ? t("msale.posted") : s === "Cancelled" ? t("msale.cancelled") : t("msale.draft");

  const unitFor = (id: string): MaterialUnit => chemicals?.find((m) => m.id === id)?.unit ?? "KG";

  return (
    <>
      <PageHeader
        title={t("msale.title")}
        subtitle={t("msale.subtitle")}
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: MaterialSalesExports.excel }}
              pdf={{ action: MaterialSalesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("msale.new")}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <div>
              <label className="field-label">{t("common.warehouse")}</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                <option value="">—</option>
                {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">{t("msale.buyerName")}</label>
              <Input value={buyerName} onChange={(e) => setBuyerName(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="field-label">{t("msale.customerAccount")}</label>
              <Select
                value={customerId}
                onChange={(e) => {
                  setCustomerId(e.target.value);
                  const c = customers?.find((x) => x.id === e.target.value);
                  if (c) setBuyerName(c.name);
                }}
              >
                <option value="">{t("msale.walkIn")}</option>
                {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} — {c.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">{t("msale.settleNow")}</label>
              <Select value={treasuryAccountId} onChange={(e) => setTreasuryAccountId(e.target.value)}>
                <option value="">—</option>
                {accounts?.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">{t("common.date")}</label>
              <Input type="date" value={saleDate} onChange={(e) => setSaleDate(e.target.value)} />
            </div>
            <div>
              <label className="field-label">{t("msale.discount")}</label>
              <Input type="number" step="0.01" min="0" value={discount} onChange={(e) => setDiscount(e.target.value)} />
            </div>
            <div>
              <label className="field-label">{t("msale.tax")}</label>
              <Input type="number" step="0.01" min="0" value={tax} onChange={(e) => setTax(e.target.value)} />
            </div>
            <div>
              <label className="field-label">Payment method</label>
              <Input value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)} maxLength={100} />
            </div>
            <div>
              <label className="field-label">{t("common.notes")}</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
            </div>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      <Card className="p-4 mb-4 flex flex-col sm:flex-row gap-3 sm:items-end">
        <div className="w-full sm:w-56">
          <label className="field-label">{t("common.status")}</label>
          <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as "" | MaterialSaleStatus)}>
            <option value="">{t("common.all")}</option>
            <option value="Draft">{t("msale.draft")}</option>
            <option value="Posted">{t("msale.posted")}</option>
            <option value="Cancelled">{t("msale.cancelled")}</option>
          </Select>
        </div>
        <p className="text-xs text-gray-400 sm:mb-2">{t("msale.postedNote")}</p>
      </Card>

      {error && <p className="form-error mb-3">{error}</p>}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>{t("approvals.document")}</th>
              <th>PDF</th>
              <th>{t("common.date")}</th>
              <th>{t("msale.buyer")}</th>
              <th>{t("common.warehouse")}</th>
              <th>{t("common.total")}</th>
              <th>{t("common.status")}</th>
              <th>{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && sales?.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {sales?.map((sale: MaterialSale) => (
              <Fragment key={sale.id}>
                <tr className="border-b border-gray-100 hover:bg-gray-50">
                  <td className="font-medium ltr-nums">{sale.saleNumber}</td>
                  <td>
                    <Button variant="ghost" onClick={MaterialSalesExports.documentPdf(sale.id)}>
                      PDF
                    </Button>
                  </td>
                  <td className="ltr-nums">{new Date(sale.saleDate).toLocaleDateString("en-GB")}</td>
                  <td>
                    <div>{sale.buyerName}</div>
                    {sale.customerId && (
                      <div className="text-xs text-gray-400">
                        {t("msale.customerAccount")} {sale.customerCode}
                      </div>
                    )}
                    {sale.treasuryAccountName && (
                      <div className="text-xs text-emerald-600">{sale.treasuryAccountName}</div>
                    )}
                  </td>
                  <td className="text-ink-muted">{sale.warehouseName}</td>
                  <td className="ltr-nums font-medium">{money(sale.total)}</td>
                  <td>
                    <Badge tone={statusTone(sale.status)}>{statusLabel(sale.status)}</Badge>
                  </td>
                  <td className="flex flex-wrap gap-2">
                    <Button variant="ghost" onClick={() => setExpanded(expanded === sale.id ? null : sale.id)}>
                      {expanded === sale.id ? t("common.close") : t("common.details")}
                    </Button>
                    {sale.isEditable && (
                      <Button variant="secondary" onClick={() => post.mutate(sale.id)} disabled={post.isPending}>
                        {t("msale.post")}
                      </Button>
                    )}
                    {sale.status !== "Cancelled" && (
                      <Button variant="ghost" onClick={() => cancel.mutate(sale.id)}>{t("msale.cancelSale")}</Button>
                    )}
                  </td>
                </tr>

                {expanded === sale.id && (
                  <tr className="bg-gray-50/60">
                    <td colSpan={8} className="px-4 py-4">
                      <table className="table mb-4">
                        <thead>
                          <tr>
                            <th>{t("common.item")}</th>
                            <th>{t("common.quantity")}</th>
                            <th>{t("pur.unitPrice")}</th>
                            <th>{t("common.total")}</th>
                            <th>{t("common.actions")}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {sale.lines.length === 0 && (
                            <tr>
                              <td colSpan={5} className="py-3 text-center text-gray-400 text-xs">{t("common.empty")}</td>
                            </tr>
                          )}
                          {sale.lines.map((l) => (
                            <tr key={l.id} className="border-b border-gray-100 last:border-0">
                              <td>
                                <span className="ltr-nums text-gray-500 me-2">{l.materialCode}</span>
                                {l.materialName}
                              </td>
                              <td className="ltr-nums">{l.quantity} {l.unit}</td>
                              <td className="ltr-nums">{money(l.unitPrice)}</td>
                              <td className="ltr-nums">{money(l.lineTotal)}</td>
                              <td>
                                {sale.isEditable && (
                                  <button
                                    className="text-red-500 hover:underline text-xs font-semibold"
                                    onClick={() => removeLine.mutate({ id: sale.id, lineId: l.id })}
                                  >
                                    {t("common.remove")}
                                  </button>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>

                      {sale.isEditable && (
                        <form
                          className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end"
                          onSubmit={(e) => {
                            e.preventDefault();
                            addLine.mutate(sale.id);
                          }}
                        >
                          <div>
                            <label className="field-label">{t("common.item")}</label>
                            <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required>
                              <option value="">—</option>
                              {chemicals?.map((m) => (
                                <option key={m.id} value={m.id}>
                                  {m.code} — {m.name} ({m.unit}) · {m.currentBalance}
                                </option>
                              ))}
                            </Select>
                          </div>
                          <div>
                            <label className="field-label">
                              {t("common.quantity")} {materialId ? `(${unitFor(materialId)})` : ""}
                            </label>
                            <Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required />
                          </div>
                          <div>
                            <label className="field-label">{t("pur.unitPrice")}</label>
                            <Input type="number" step="0.0001" min="0" value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} />
                          </div>
                          <Button type="submit" disabled={addLine.isPending}>{t("sup.addLine")}</Button>
                        </form>
                      )}

                      <div className="mt-4 text-sm text-gray-600">
                        <div>{t("msale.subtotal")}: <span className="ltr-nums">{money(sale.subTotal)}</span></div>
                        <div>{t("msale.discount")}: <span className="ltr-nums">{money(sale.discount)}</span></div>
                        <div>{t("msale.tax")}: <span className="ltr-nums">{money(sale.tax)}</span></div>
                        <div className="font-semibold text-gray-800">
                          {t("common.total")}: <span className="ltr-nums">{money(sale.total)}</span>
                        </div>
                      </div>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
