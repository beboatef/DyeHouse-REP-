import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, CustomerStatementApi, InvoicesApi, ItemsApi } from "@/api/client";
import type { Invoice, Item, UnitOfMeasure } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { InvoicesExports, StatementsExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/** The two billing units a service line can be priced in. A line is billed in one unit only, never converted. */
const BILLING_UNITS: UnitOfMeasure[] = ["KG", "Meter"];

const statusLabel: Record<string, string> = { Draft: "مسودة", Issued: "معتمدة", PartiallyPaid: "مدفوعة جزئيًا", Paid: "مدفوعة", Cancelled: "ملغاة" };
const statusTone: Record<string, "gray" | "blue" | "yellow" | "green" | "red"> = { Draft: "gray", Issued: "blue", PartiallyPaid: "yellow", Paid: "green", Cancelled: "red" };

type LineDraft = { itemId: string; color: string; unit: UnitOfMeasure; quantity: string; processingPrice: string };
const emptyLine = (): LineDraft => ({ itemId: "", color: "", unit: "KG", quantity: "", processingPrice: "" });

export default function InvoicesPage() {
  const qc = useQueryClient();
  const { t } = useI18n();
  const [tab, setTab] = useState<"invoices" | "statement">("invoices");
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(new Date().toISOString().slice(0, 10));
  const [discount, setDiscount] = useState("0");
  const [tax, setTax] = useState("0");
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: invoices, isLoading } = useQuery({ queryKey: ["invoices"], queryFn: () => InvoicesApi.list() });

  const [statementCustomerId, setStatementCustomerId] = useState("");
  const { data: statement } = useQuery({
    queryKey: ["customer-statement", statementCustomerId], queryFn: () => CustomerStatementApi.get(statementCustomerId), enabled: !!statementCustomerId
  });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["invoices"] });

  const createMutation = useMutation({
    mutationFn: () => InvoicesApi.create({
      customerId, invoiceDate, discount: Number(discount || 0), tax: Number(tax || 0),
      lines: lines.filter((l) => l.itemId && l.quantity).map((l) => ({
        itemId: l.itemId, color: l.color || undefined, unit: l.unit,
        quantity: Number(l.quantity), processingPrice: Number(l.processingPrice || 0)
      }))
    }),
    onSuccess: () => { invalidate(); setShowForm(false); setLines([emptyLine()]); setError(null); },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const issueMutation = useMutation({ mutationFn: InvoicesApi.issue, onSuccess: invalidate });
  const [editingId, setEditingId] = useState<string | null>(null);
  const [cancelReasonById, setCancelReasonById] = useState<Record<string, string>>({});
  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => InvoicesApi.cancel(vars.id, vars.reason), onSuccess: invalidate
  });

  const updateLine = (idx: number, patch: Partial<LineDraft>) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  return (
    <>
      <PageHeader title="الفواتير وحساب العميل" subtitle="مسودة ← معتمدة (تسجل في حساب العميل) - الإلغاء ينشئ حركة عكسية ولا يحذف السجل الأصلي" />

      <div className="tabs mb-6">
        <button onClick={() => setTab("invoices")} className={`tab ${tab === "invoices" ? "tab-active" : ""}`}>الفواتير</button>
        <button onClick={() => setTab("statement")} className={`tab ${tab === "statement" ? "tab-active" : ""}`}>كشف حساب عميل</button>
      </div>

      {tab === "invoices" && (
        <>
          <div className="flex justify-end gap-2 mb-4">
            <ExportButtons
              excel={{ action: InvoicesExports.list.excel }}
              pdf={{ action: InvoicesExports.list.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ فاتورة جديدة"}</Button>
          </div>

          {showForm && (
            <Card className="p-5 mb-6">
              <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
                <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
                  <div className="sm:col-span-2">
                    <label className="field-label">العميل</label>
                    <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                      <option value="">اختر...</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                    </Select>
                  </div>
                  <div><label className="field-label">تاريخ الفاتورة</label><Input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} required /></div>
                  <div><label className="field-label">الخصم</label><Input type="number" step="0.01" value={discount} onChange={(e) => setDiscount(e.target.value)} /></div>
                  <div><label className="field-label">الضريبة</label><Input type="number" step="0.01" value={tax} onChange={(e) => setTax(e.target.value)} /></div>
                </div>

                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="field-label">بنود الفاتورة</label>
                    <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
                  </div>
                  <div className="space-y-3">
                    {lines.map((line, idx) => (
                      <div key={idx} className="grid grid-cols-1 sm:grid-cols-5 gap-3 items-end bg-gray-50 rounded-lg p-3">
                        <div>
                          <label className="field-label">الصنف</label>
                          <Select value={line.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} required>
                            <option value="">اختر...</option>{items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                          </Select>
                        </div>
                        <div><label className="field-label">اللون</label><Input value={line.color} onChange={(e) => updateLine(idx, { color: e.target.value })} /></div>
                        <div>
                          <label className="field-label">{t("common.unit")}</label>
                          <Select value={line.unit} onChange={(e) => updateLine(idx, { unit: e.target.value as UnitOfMeasure })} required>
                            {BILLING_UNITS.map((u) => <option key={u} value={u}>{t(`unit.${u === "KG" ? "kg" : "meter"}`)}</option>)}
                          </Select>
                        </div>
                        <div><label className="field-label">الكمية</label><Input type="number" step="0.001" min="0" value={line.quantity} onChange={(e) => updateLine(idx, { quantity: e.target.value })} required /></div>
                        <div><label className="field-label">سعر التشغيل للوحدة</label><Input type="number" step="0.01" min="0" value={line.processingPrice} onChange={(e) => updateLine(idx, { processingPrice: e.target.value })} required /></div>
                      </div>
                    ))}
                  </div>
                </div>

                <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ كمسودة"}</Button>
                {error && <p className="form-error">{error}</p>}
              </form>
            </Card>
          )}

          <div className="space-y-3">
            {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
            {invoices?.map((inv) => (
              <Card key={inv.id} className="card-pad">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <span className="font-bold ltr-nums">{inv.invoiceNumber}</span>
                    <span className="text-gray-400 mx-2">·</span>
                    <span className="text-sm text-gray-600">{inv.customerCode} - {inv.customerName}</span>
                    <span className="text-gray-400 mx-2">·</span>
                    <span className="ltr-nums font-medium">{inv.total}</span>
                  </div>
                  <Badge tone={statusTone[inv.status]}>{statusLabel[inv.status]}</Badge>
                </div>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  {inv.status === "Draft" && (
                    <Button variant="secondary" onClick={() => setEditingId(editingId === inv.id ? null : inv.id)}>
                      {editingId === inv.id ? "إغلاق التعديل" : "تعديل المسودة"}
                    </Button>
                  )}
                  {inv.status === "Draft" && <Button variant="secondary" onClick={() => issueMutation.mutate(inv.id)}>اعتماد</Button>}
                  <Link to={`/print/invoice/${inv.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
                  <Button variant="ghost" onClick={InvoicesExports.documentPdf(inv.id)}>تنزيل PDF</Button>
                  {(inv.status === "Draft" || inv.status === "Issued" || inv.status === "PartiallyPaid") && (
                    <>
                      <Input placeholder="سبب الإلغاء..." value={cancelReasonById[inv.id] ?? ""} onChange={(e) => setCancelReasonById((p) => ({ ...p, [inv.id]: e.target.value }))} className="min-w-[12rem] flex-1 sm:max-w-xs" />
                      <Button variant="ghost" disabled={!cancelReasonById[inv.id]} onClick={() => cancelMutation.mutate({ id: inv.id, reason: cancelReasonById[inv.id] })}>إلغاء</Button>
                    </>
                  )}
                </div>
                {editingId === inv.id && inv.status === "Draft" && (
                  <DraftInvoiceEditor invoice={inv} items={items ?? []} onDone={invalidate} onClose={() => setEditingId(null)} />
                )}
              </Card>
            ))}
          </div>
        </>
      )}

      {tab === "statement" && (
        <>
          <Card className="p-4 mb-4">
            <Select value={statementCustomerId} onChange={(e) => setStatementCustomerId(e.target.value)}>
              <option value="">اختر عميلًا لعرض كشف حسابه...</option>
              {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
            </Select>
          </Card>
          {statementCustomerId && (
            <>
              <div className="flex justify-end gap-2 mb-3">
                <Button
                  variant="secondary"
                  onClick={() => StatementsExports.customer(statementCustomerId).pdf()}
                >
                  تنزيل PDF
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => StatementsExports.customer(statementCustomerId).excel()}
                >
                  تنزيل Excel
                </Button>
              </div>
              <Card>
              <table className="table">
                <thead><tr>
                  <th>التاريخ</th><th>البيان</th>
                  <th>المستند</th><th>مدين</th>
                  <th>دائن</th><th>الرصيد</th>
                </tr></thead>
                <tbody>
                  {statement?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد حركات بعد</td></tr>}
                  {statement?.map((s, idx) => (
                    <tr key={idx} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                      <td className="ltr-nums">{new Date(s.date).toLocaleDateString("en-GB")}</td>
                      <td>{s.description}</td>
                      <td className="ltr-nums">{s.documentNumber}</td>
                      <td className="ltr-nums">{s.debit || "-"}</td>
                      <td className="ltr-nums">{s.credit || "-"}</td>
                      <td className="ltr-nums font-medium">{s.runningBalance}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              </Card>
            </>
          )}
        </>
      )}
    </>
  );
}

/**
 * Draft-invoice editor (spec section 32). A Draft has posted nothing yet (no customer-ledger row),
 * so its header and lines are ordinary working data - edited here behind the configurable
 * invoices.edit permission, enforced server-side. Totals are always derived from the lines, so the
 * screen never has to recompute them. An Issued/Cancelled invoice is never edited here; it is
 * corrected through the cancellation workflow.
 */
function DraftInvoiceEditor({ invoice, items, onDone, onClose }: {
  invoice: Invoice; items: Item[]; onDone: () => void; onClose: () => void;
}) {
  const { t } = useI18n();
  const [invoiceDate, setInvoiceDate] = useState(invoice.invoiceDate.slice(0, 10));
  const [discount, setDiscount] = useState(String(invoice.discount));
  const [tax, setTax] = useState(String(invoice.tax));
  const [notes, setNotes] = useState(invoice.notes ?? "");
  const [error, setError] = useState<string | null>(null);

  // Per-line edit buffer, keyed by line id. Lines are saved individually so an edit is never
  // silently applied to a line the user did not touch.
  const [lineEdits, setLineEdits] = useState<Record<string, { quantity: string; processingPrice: string; color: string; unit: UnitOfMeasure }>>(
    () => Object.fromEntries(invoice.lines.map((l) => [l.id, {
      quantity: String(l.quantity), processingPrice: String(l.processingPrice), color: l.color ?? "", unit: l.unit
    }]))
  );
  const [newLine, setNewLine] = useState({ itemId: "", quantity: "", processingPrice: "", color: "", unit: "KG" as UnitOfMeasure });

  const after = (msg?: string) => { onDone(); setError(msg ?? null); };

  const headerMutation = useMutation({
    mutationFn: () => InvoicesApi.update(invoice.id, {
      invoiceDate, discount: Number(discount || 0), tax: Number(tax || 0), notes: notes || undefined,
      // Round-trip the delivery link unchanged so a header edit never silently clears it.
      deliveryId: invoice.deliveryId ?? undefined
    }),
    onSuccess: () => after(),
    onError: (e: ApiErrorLike) => setError(e?.response?.data?.title ?? "تعذر حفظ بيانات الفاتورة")
  });

  const lineMutation = useMutation({
    mutationFn: (vars: { lineId: string; quantity: number; processingPrice: number; unit: UnitOfMeasure; color?: string }) =>
      InvoicesApi.updateLine(invoice.id, vars.lineId, { quantity: vars.quantity, processingPrice: vars.processingPrice, unit: vars.unit, color: vars.color }),
    onSuccess: () => after(),
    onError: (e: ApiErrorLike) => setError(e?.response?.data?.title ?? "تعذر حفظ البند")
  });

  const removeLineMutation = useMutation({
    mutationFn: (lineId: string) => InvoicesApi.removeLine(invoice.id, lineId),
    onSuccess: () => after(),
    onError: (e: ApiErrorLike) => setError(e?.response?.data?.title ?? "تعذر حذف البند")
  });

  const addLineMutation = useMutation({
    mutationFn: () => InvoicesApi.addLine(invoice.id, {
      itemId: newLine.itemId, quantity: Number(newLine.quantity), processingPrice: Number(newLine.processingPrice || 0),
      unit: newLine.unit, color: newLine.color || undefined
    }),
    onSuccess: () => { onDone(); setNewLine({ itemId: "", quantity: "", processingPrice: "", color: "", unit: "KG" }); },
    onError: (e: ApiErrorLike) => setError(e?.response?.data?.title ?? "تعذر إضافة البند")
  });

  const patch = (lineId: string, p: Partial<{ quantity: string; processingPrice: string; color: string; unit: UnitOfMeasure }>) =>
    setLineEdits((prev) => ({ ...prev, [lineId]: { ...prev[lineId], ...p } }));

  return (
    <div className="mt-4 border-t border-gray-100 pt-4 space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end">
        <div><label className="field-label">تاريخ الفاتورة</label><Input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} /></div>
        <div><label className="field-label">الخصم</label><Input type="number" step="0.01" min="0" value={discount} onChange={(e) => setDiscount(e.target.value)} /></div>
        <div><label className="field-label">الضريبة</label><Input type="number" step="0.01" min="0" value={tax} onChange={(e) => setTax(e.target.value)} /></div>
        <div><label className="field-label">ملاحظات</label><Input value={notes} onChange={(e) => setNotes(e.target.value)} /></div>
      </div>
      <div className="flex justify-end">
        <Button variant="secondary" disabled={headerMutation.isPending} onClick={() => headerMutation.mutate()}>حفظ البيانات</Button>
      </div>

      <div className="space-y-2">
        <div className="text-sm font-medium text-gray-700">بنود الفاتورة</div>
        {invoice.lines.map((l) => {
          const edit = lineEdits[l.id] ?? { quantity: String(l.quantity), processingPrice: String(l.processingPrice), color: l.color ?? "", unit: l.unit };
          return (
            <div key={l.id} className="grid grid-cols-1 sm:grid-cols-6 gap-2 items-end bg-gray-50 rounded-lg p-3">
              <div className="text-sm"><div className="field-label">الصنف</div>{l.itemCode}</div>
              <div><label className="field-label">اللون</label><Input value={edit.color} onChange={(e) => patch(l.id, { color: e.target.value })} /></div>
              <div>
                <label className="field-label">{t("common.unit")}</label>
                <Select value={edit.unit} onChange={(e) => patch(l.id, { unit: e.target.value as UnitOfMeasure })}>
                  {BILLING_UNITS.map((u) => <option key={u} value={u}>{t(`unit.${u === "KG" ? "kg" : "meter"}`)}</option>)}
                </Select>
              </div>
              <div><label className="field-label">الكمية</label><Input type="number" step="0.001" min="0" value={edit.quantity} onChange={(e) => patch(l.id, { quantity: e.target.value })} /></div>
              <div><label className="field-label">السعر</label><Input type="number" step="0.01" min="0" value={edit.processingPrice} onChange={(e) => patch(l.id, { processingPrice: e.target.value })} /></div>
              <div className="flex gap-1">
                <Button variant="secondary" disabled={lineMutation.isPending} onClick={() => lineMutation.mutate({
                  lineId: l.id, quantity: Number(edit.quantity), processingPrice: Number(edit.processingPrice || 0), unit: edit.unit, color: edit.color || undefined
                })}>حفظ</Button>
                <Button variant="ghost" disabled={removeLineMutation.isPending} onClick={() => removeLineMutation.mutate(l.id)}>حذف</Button>
              </div>
            </div>
          );
        })}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-5 gap-2 items-end bg-green-50 rounded-lg p-3">
        <div>
          <label className="field-label">إضافة بند - الصنف</label>
          <Select value={newLine.itemId} onChange={(e) => setNewLine((p) => ({ ...p, itemId: e.target.value }))}>
            <option value="">اختر...</option>{items.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
          </Select>
        </div>
        <div><label className="field-label">اللون</label><Input value={newLine.color} onChange={(e) => setNewLine((p) => ({ ...p, color: e.target.value }))} /></div>
        <div>
          <label className="field-label">{t("common.unit")}</label>
          <Select value={newLine.unit} onChange={(e) => setNewLine((p) => ({ ...p, unit: e.target.value as UnitOfMeasure }))}>
            {BILLING_UNITS.map((u) => <option key={u} value={u}>{t(`unit.${u === "KG" ? "kg" : "meter"}`)}</option>)}
          </Select>
        </div>
        <div><label className="field-label">الكمية</label><Input type="number" step="0.001" min="0" value={newLine.quantity} onChange={(e) => setNewLine((p) => ({ ...p, quantity: e.target.value }))} /></div>
        <div><label className="field-label">السعر</label><Input type="number" step="0.01" min="0" value={newLine.processingPrice} onChange={(e) => setNewLine((p) => ({ ...p, processingPrice: e.target.value }))} /></div>
        <Button variant="secondary" disabled={!newLine.itemId || !newLine.quantity || addLineMutation.isPending} onClick={() => addLineMutation.mutate()}>إضافة</Button>
      </div>

      {error && <p className="form-error">{error}</p>}
      <div className="flex justify-end"><Button variant="ghost" onClick={onClose}>إغلاق</Button></div>
    </div>
  );
}
