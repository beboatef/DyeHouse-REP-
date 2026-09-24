import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, CustomerStatementApi, DocumentPdfApi, downloadFile, InvoicesApi, ItemsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const statusLabel: Record<string, string> = { Draft: "مسودة", Issued: "معتمدة", PartiallyPaid: "مدفوعة جزئيًا", Paid: "مدفوعة", Cancelled: "ملغاة" };
const statusTone: Record<string, "gray" | "blue" | "yellow" | "green" | "red"> = { Draft: "gray", Issued: "blue", PartiallyPaid: "yellow", Paid: "green", Cancelled: "red" };

type LineDraft = { itemId: string; color: string; quantity: string; processingPrice: string };
const emptyLine = (): LineDraft => ({ itemId: "", color: "", quantity: "", processingPrice: "" });

export default function InvoicesPage() {
  const qc = useQueryClient();
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
        itemId: l.itemId, color: l.color || undefined, quantity: Number(l.quantity), processingPrice: Number(l.processingPrice || 0)
      }))
    }),
    onSuccess: () => { invalidate(); setShowForm(false); setLines([emptyLine()]); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const issueMutation = useMutation({ mutationFn: InvoicesApi.issue, onSuccess: invalidate });
  const [cancelReasonById, setCancelReasonById] = useState<Record<string, string>>({});
  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => InvoicesApi.cancel(vars.id, vars.reason), onSuccess: invalidate
  });

  const updateLine = (idx: number, patch: Partial<LineDraft>) => setLines((p) => p.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  return (
    <>
      <PageHeader title="الفواتير وحساب العميل" subtitle="مسودة ← معتمدة (تسجل في حساب العميل) - الإلغاء ينشئ حركة عكسية ولا يحذف السجل الأصلي" />

      <div className="flex gap-2 mb-6 border-b border-gray-200">
        <button onClick={() => setTab("invoices")} className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${tab === "invoices" ? "border-brand-600 text-brand-700" : "border-transparent text-gray-500"}`}>الفواتير</button>
        <button onClick={() => setTab("statement")} className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${tab === "statement" ? "border-brand-600 text-brand-700" : "border-transparent text-gray-500"}`}>كشف حساب عميل</button>
      </div>

      {tab === "invoices" && (
        <>
          <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ فاتورة جديدة"}</Button></div>

          {showForm && (
            <Card className="p-5 mb-6">
              <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
                <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
                  <div className="sm:col-span-2">
                    <label className="block text-xs font-medium text-gray-600 mb-1">العميل</label>
                    <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                      <option value="">اختر...</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                    </Select>
                  </div>
                  <div><label className="block text-xs font-medium text-gray-600 mb-1">تاريخ الفاتورة</label><Input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} required /></div>
                  <div><label className="block text-xs font-medium text-gray-600 mb-1">الخصم</label><Input type="number" step="0.01" value={discount} onChange={(e) => setDiscount(e.target.value)} /></div>
                  <div><label className="block text-xs font-medium text-gray-600 mb-1">الضريبة</label><Input type="number" step="0.01" value={tax} onChange={(e) => setTax(e.target.value)} /></div>
                </div>

                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="block text-xs font-medium text-gray-600">بنود الفاتورة</label>
                    <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>+ إضافة بند</Button>
                  </div>
                  <div className="space-y-3">
                    {lines.map((line, idx) => (
                      <div key={idx} className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end bg-gray-50 rounded-lg p-3">
                        <div>
                          <label className="block text-[11px] text-gray-500 mb-1">الصنف</label>
                          <Select value={line.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} required>
                            <option value="">اختر...</option>{items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                          </Select>
                        </div>
                        <div><label className="block text-[11px] text-gray-500 mb-1">اللون</label><Input value={line.color} onChange={(e) => updateLine(idx, { color: e.target.value })} /></div>
                        <div><label className="block text-[11px] text-gray-500 mb-1">الكمية</label><Input type="number" step="0.001" min="0" value={line.quantity} onChange={(e) => updateLine(idx, { quantity: e.target.value })} required /></div>
                        <div><label className="block text-[11px] text-gray-500 mb-1">سعر التشغيل للوحدة</label><Input type="number" step="0.01" min="0" value={line.processingPrice} onChange={(e) => updateLine(idx, { processingPrice: e.target.value })} required /></div>
                      </div>
                    ))}
                  </div>
                </div>

                <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ كمسودة"}</Button>
                {error && <p className="text-sm text-red-600">{error}</p>}
              </form>
            </Card>
          )}

          <div className="space-y-3">
            {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
            {invoices?.map((inv) => (
              <Card key={inv.id} className="p-4">
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
                <div className="flex items-center gap-2 mt-3">
                  {inv.status === "Draft" && <Button variant="secondary" onClick={() => issueMutation.mutate(inv.id)}>اعتماد</Button>}
                  <Link to={`/print/invoice/${inv.id}`} target="_blank"><Button type="button" variant="ghost">معاينة قبل الطباعة</Button></Link>
                  <Button variant="ghost" onClick={() => DocumentPdfApi.invoice(inv.id, inv.invoiceNumber)}>تنزيل PDF</Button>
                  {(inv.status === "Draft" || inv.status === "Issued" || inv.status === "PartiallyPaid") && (
                    <>
                      <Input placeholder="سبب الإلغاء..." value={cancelReasonById[inv.id] ?? ""} onChange={(e) => setCancelReasonById((p) => ({ ...p, [inv.id]: e.target.value }))} className="max-w-xs" />
                      <Button variant="ghost" disabled={!cancelReasonById[inv.id]} onClick={() => cancelMutation.mutate({ id: inv.id, reason: cancelReasonById[inv.id] })}>إلغاء</Button>
                    </>
                  )}
                </div>
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
                  onClick={() => downloadFile(`/customers/${statementCustomerId}/statement/pdf`, `statement-${statementCustomerId}.pdf`)}
                >
                  تنزيل PDF
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => downloadFile(`/customers/${statementCustomerId}/statement/excel`, `statement-${statementCustomerId}.xlsx`)}
                >
                  تنزيل Excel
                </Button>
              </div>
              <Card>
              <table className="w-full text-sm">
                <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
                  <th className="text-start px-4 py-3 font-medium">التاريخ</th><th className="text-start px-4 py-3 font-medium">البيان</th>
                  <th className="text-start px-4 py-3 font-medium">المستند</th><th className="text-start px-4 py-3 font-medium">مدين</th>
                  <th className="text-start px-4 py-3 font-medium">دائن</th><th className="text-start px-4 py-3 font-medium">الرصيد</th>
                </tr></thead>
                <tbody>
                  {statement?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد حركات بعد</td></tr>}
                  {statement?.map((s, idx) => (
                    <tr key={idx} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                      <td className="px-4 py-3 ltr-nums">{new Date(s.date).toLocaleDateString("en-GB")}</td>
                      <td className="px-4 py-3">{s.description}</td>
                      <td className="px-4 py-3 ltr-nums">{s.documentNumber}</td>
                      <td className="px-4 py-3 ltr-nums">{s.debit || "-"}</td>
                      <td className="px-4 py-3 ltr-nums">{s.credit || "-"}</td>
                      <td className="px-4 py-3 ltr-nums font-medium">{s.runningBalance}</td>
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
