import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomersApi, InvoicesApi, PaymentsApi, ReceiptsApi, TreasuryAccountKind, TreasuryAccountsApi, TreasuryTransfersApi
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { DocumentActions } from "@/components/ExportButtons";
import { StatementsExports } from "@/api/exports";

type Tab = "accounts" | "receipts" | "payments" | "transfers";

export default function TreasuryPage() {
  const [tab, setTab] = useState<Tab>("accounts");
  const tabs: { key: Tab; label: string }[] = [
    { key: "accounts", label: "الحسابات" },
    { key: "receipts", label: "المقبوضات" },
    { key: "payments", label: "المدفوعات" },
    { key: "transfers", label: "التحويلات" }
  ];

  return (
    <>
      <PageHeader title="الخزينة" subtitle="حسابات نقدية وبنكية، مقبوضات، مدفوعات، وتحويلات بين الحسابات" />
      <div className="flex gap-2 mb-6 border-b border-gray-200">
        {tabs.map((t) => (
          <button key={t.key} onClick={() => setTab(t.key)} className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${tab === t.key ? "border-brand-600 text-brand-700" : "border-transparent text-gray-500"}`}>{t.label}</button>
        ))}
      </div>
      {tab === "accounts" && <AccountsTab />}
      {tab === "receipts" && <ReceiptsTab />}
      {tab === "payments" && <PaymentsTab />}
      {tab === "transfers" && <TransfersTab />}
    </>
  );
}

function AccountsTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [kind, setKind] = useState<TreasuryAccountKind>("Cash");

  const { data: accounts, isLoading } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const createMutation = useMutation({
    mutationFn: () => TreasuryAccountsApi.create({ code, name, kind }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["treasury-accounts"] }); setShowForm(false); setCode(""); setName(""); }
  });

  // كشف الحساب per account: a real statement from the treasury ledger, with the
  // same date range feeding the screen, the print view and the Excel/PDF files.
  const [statementFor, setStatementFor] = useState<{ id: string; code: string; name: string } | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const { data: statement } = useQuery({
    queryKey: ["treasury-statement", statementFor?.id, from, to],
    queryFn: () => TreasuryAccountsApi.statement(statementFor!.id, { from: from || undefined, to: to || undefined }),
    enabled: !!statementFor
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ حساب جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكود</label><Input value={code} onChange={(e) => setCode(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الاسم</label><Input value={name} onChange={(e) => setName(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">النوع</label>
              <Select value={kind} onChange={(e) => setKind(e.target.value as TreasuryAccountKind)}><option value="Cash">نقدي</option><option value="Bank">بنكي</option></Select>
            </div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الكود</th><th className="text-start px-4 py-3 font-medium">الاسم</th>
            <th className="text-start px-4 py-3 font-medium">النوع</th>
            <th className="text-start px-4 py-3 font-medium">الرصيد</th>
            <th className="text-start px-4 py-3 font-medium">كشف الحساب</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {accounts?.map((a) => (
              <tr key={a.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{a.code}</td><td className="px-4 py-3">{a.name}</td>
                <td className="px-4 py-3"><Badge tone="blue">{a.kind === "Cash" ? "نقدي" : "بنكي"}</Badge></td>
                <td className="px-4 py-3 ltr-nums font-medium">{a.balance}</td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1">
                    <Button variant="ghost" type="button" onClick={() => setStatementFor({ id: a.id, code: a.code, name: a.name })}>
                      كشف حساب
                    </Button>
                    <DocumentActions pdf={StatementsExports.treasury(a.id, from || undefined, to || undefined).pdf} />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {statementFor && (
        <Card className="mt-6">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4 border-b border-gray-100">
            <div>
              <div className="font-semibold">كشف حساب: {statementFor.code} - {statementFor.name}</div>
              <div className="text-xs text-gray-500">الرصيد الافتتاحي والإجماليات محسوبة من دفتر الخزينة، وليست حقولًا مخزَّنة</div>
            </div>
            <div className="flex flex-wrap items-end gap-2">
              <div><label className="block text-[11px] text-gray-500 mb-1">من</label>
                <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
              <div><label className="block text-[11px] text-gray-500 mb-1">إلى</label>
                <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
              <Button variant="secondary" onClick={() => StatementsExports.treasury(statementFor.id, from || undefined, to || undefined).excel()}>
                تنزيل Excel
              </Button>
              <Button variant="secondary" onClick={() => StatementsExports.treasury(statementFor.id, from || undefined, to || undefined).pdf()}>
                تنزيل PDF
              </Button>
              <Button variant="secondary" onClick={() => window.print()}>طباعة</Button>
              <Button variant="ghost" onClick={() => setStatementFor(null)}>إغلاق</Button>
            </div>
          </div>

          {statement && (
            <div className="p-4">
              <div className="flex flex-wrap gap-6 text-sm mb-3">
                <span>افتتاحي: <b className="ltr-nums">{statement.openingBalance}</b></span>
                <span className="text-green-700">وارد: <b className="ltr-nums">{statement.totalIn}</b></span>
                <span className="text-red-600">منصرف: <b className="ltr-nums">{statement.totalOut}</b></span>
                <span>ختامي: <b className="ltr-nums">{statement.closingBalance}</b></span>
              </div>
              <table className="w-full text-sm">
                <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
                  <th className="text-start px-3 py-2 font-medium">التاريخ</th>
                  <th className="text-start px-3 py-2 font-medium">المستند</th>
                  <th className="text-start px-3 py-2 font-medium">البيان</th>
                  <th className="text-start px-3 py-2 font-medium">منصرف</th>
                  <th className="text-start px-3 py-2 font-medium">وارد</th>
                  <th className="text-start px-3 py-2 font-medium">الرصيد</th>
                </tr></thead>
                <tbody>
                  {statement.lines.length === 0 && (
                    <tr><td colSpan={6} className="px-3 py-6 text-center text-gray-400">لا توجد حركات في هذه الفترة</td></tr>
                  )}
                  {statement.lines.map((l, i) => (
                    <tr key={i} className="border-b border-gray-100 last:border-0">
                      <td className="px-3 py-2 ltr-nums">{new Date(l.transactionDate).toLocaleDateString("en-GB")}</td>
                      <td className="px-3 py-2 ltr-nums">{l.sourceDocumentNumber}</td>
                      <td className="px-3 py-2">{l.description ?? l.sourceDocumentType}</td>
                      <td className="px-3 py-2 ltr-nums">{l.debit || ""}</td>
                      <td className="px-3 py-2 ltr-nums">{l.credit || ""}</td>
                      <td className="px-3 py-2 ltr-nums">{l.runningBalance}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}
    </>
  );
}

function ReceiptsTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [treasuryAccountId, setTreasuryAccountId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [invoiceId, setInvoiceId] = useState("");
  const [amount, setAmount] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: invoices } = useQuery({
    queryKey: ["invoices", "unpaid", customerId], queryFn: () => InvoicesApi.list({ customerId }), enabled: !!customerId
  });
  const { data: receipts, isLoading } = useQuery({ queryKey: ["receipts"], queryFn: () => ReceiptsApi.list() });

  const createMutation = useMutation({
    mutationFn: () => ReceiptsApi.create({
      receiptDate: new Date().toISOString().slice(0, 10), treasuryAccountId, amount: Number(amount),
      customerId: customerId || undefined, invoiceId: invoiceId || undefined
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["receipts"] }); qc.invalidateQueries({ queryKey: ["treasury-accounts"] }); setShowForm(false); setAmount(""); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مقبوض جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الحساب</label>
              <Select value={treasuryAccountId} onChange={(e) => setTreasuryAccountId(e.target.value)} required><option value="">اختر...</option>{accounts?.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">العميل (اختياري)</label>
              <Select value={customerId} onChange={(e) => { setCustomerId(e.target.value); setInvoiceId(""); }}><option value="">-</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">تطبيق على فاتورة (اختياري)</label>
              <Select value={invoiceId} onChange={(e) => setInvoiceId(e.target.value)} disabled={!customerId}>
                <option value="">-</option>{invoices?.filter((i) => i.status !== "Paid" && i.status !== "Cancelled").map((i) => <option key={i.id} value={i.id}>{i.invoiceNumber} - {i.total}</option>)}
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المبلغ</label><Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">الحساب</th>
            <th className="text-start px-4 py-3 font-medium">العميل</th><th className="text-start px-4 py-3 font-medium">الفاتورة</th><th className="text-start px-4 py-3 font-medium">المبلغ</th><th className="text-start px-4 py-3 font-medium">الحالة</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {receipts?.map((r) => (
              <tr key={r.id} className={`border-b border-gray-100 last:border-0 hover:bg-gray-50 ${r.status === "Cancelled" ? "opacity-50" : ""}`}>
                <td className="px-4 py-3 font-medium ltr-nums">{r.receiptNumber}</td><td className="px-4 py-3">{r.treasuryAccountName}</td>
                <td className="px-4 py-3">{r.customerCode ?? "-"}</td><td className="px-4 py-3 ltr-nums">{r.invoiceNumber ?? "-"}</td>
                <td className="px-4 py-3 ltr-nums font-medium">{r.amount}</td>
                <td className="px-4 py-3">
                  {r.status === "Cancelled"
                    ? <span className="text-xs text-red-600 font-medium">ملغاة</span>
                    : <CancelDocButton label="إلغاء" onConfirm={(reason) => ReceiptsApi.cancel(r.id, reason)} invalidate={["receipts", "treasury-accounts"]} />}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function PaymentsTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [treasuryAccountId, setTreasuryAccountId] = useState("");
  const [payeeDescription, setPayeeDescription] = useState("");
  const [amount, setAmount] = useState("");

  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const { data: payments, isLoading } = useQuery({ queryKey: ["payments"], queryFn: () => PaymentsApi.list() });

  const createMutation = useMutation({
    mutationFn: () => PaymentsApi.create({ paymentDate: new Date().toISOString().slice(0, 10), treasuryAccountId, amount: Number(amount), payeeDescription }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["payments"] }); qc.invalidateQueries({ queryKey: ["treasury-accounts"] }); setShowForm(false); setAmount(""); setPayeeDescription(""); }
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مدفوع جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الحساب</label>
              <Select value={treasuryAccountId} onChange={(e) => setTreasuryAccountId(e.target.value)} required><option value="">اختر...</option>{accounts?.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المستفيد / البيان</label><Input value={payeeDescription} onChange={(e) => setPayeeDescription(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المبلغ</label><Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">الحساب</th>
            <th className="text-start px-4 py-3 font-medium">البيان</th><th className="text-start px-4 py-3 font-medium">المبلغ</th><th className="text-start px-4 py-3 font-medium">الحالة</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {payments?.map((p) => (
              <tr key={p.id} className={`border-b border-gray-100 last:border-0 hover:bg-gray-50 ${p.status === "Cancelled" ? "opacity-50" : ""}`}>
                <td className="px-4 py-3 font-medium ltr-nums">{p.paymentNumber}</td><td className="px-4 py-3">{p.treasuryAccountName}</td>
                <td className="px-4 py-3">{p.payeeDescription}</td><td className="px-4 py-3 ltr-nums font-medium">{p.amount}</td>
                <td className="px-4 py-3">
                  {p.status === "Cancelled"
                    ? <span className="text-xs text-red-600 font-medium">ملغاة</span>
                    : <CancelDocButton label="إلغاء" onConfirm={(reason) => PaymentsApi.cancel(p.id, reason)} invalidate={["payments", "treasury-accounts"]} />}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function TransfersTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [fromAccountId, setFromAccountId] = useState("");
  const [toAccountId, setToAccountId] = useState("");
  const [amount, setAmount] = useState("");

  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const { data: transfers, isLoading } = useQuery({ queryKey: ["treasury-transfers"], queryFn: () => TreasuryTransfersApi.list() });

  const createMutation = useMutation({
    mutationFn: () => TreasuryTransfersApi.create({ transferDate: new Date().toISOString().slice(0, 10), fromAccountId, toAccountId, amount: Number(amount) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["treasury-transfers"] }); qc.invalidateQueries({ queryKey: ["treasury-accounts"] }); setShowForm(false); setAmount(""); }
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحويل جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">من حساب</label>
              <Select value={fromAccountId} onChange={(e) => setFromAccountId(e.target.value)} required><option value="">اختر...</option>{accounts?.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">إلى حساب</label>
              <Select value={toAccountId} onChange={(e) => setToAccountId(e.target.value)} required><option value="">اختر...</option>{accounts?.filter((a) => a.id !== fromAccountId).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المبلغ</label><Input type="number" step="0.01" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">من</th>
            <th className="text-start px-4 py-3 font-medium">إلى</th><th className="text-start px-4 py-3 font-medium">المبلغ</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{t.transferNumber}</td><td className="px-4 py-3">{t.fromAccountName}</td>
                <td className="px-4 py-3">{t.toAccountName}</td><td className="px-4 py-3 ltr-nums font-medium">{t.amount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

/// <summary>
/// Cancel action for posted treasury documents (B4): asks for a reason, calls
/// the cancel endpoint, then refreshes the affected lists. Only rendered for
/// rows whose status is still Posted.
/// </summary>
function CancelDocButton({ label, onConfirm, invalidate }: {
  label: string;
  onConfirm: (reason: string) => Promise<unknown>;
  invalidate: string[];
}) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const cancelMutation = useMutation({
    mutationFn: () => onConfirm(reason),
    onSuccess: () => {
      invalidate.forEach((key) => qc.invalidateQueries({ queryKey: [key] }));
      setOpen(false);
      setReason("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذر الإلغاء")
  });

  if (!open)
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="text-xs text-red-600 hover:text-red-800 font-medium"
      >
        {label}
      </button>
    );

  return (
    <div className="flex items-center gap-1">
      <input
        autoFocus
        placeholder="سبب الإلغاء..."
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        onKeyDown={(e) => { if (e.key === "Enter" && reason.trim()) cancelMutation.mutate(); }}
        className="border rounded px-2 py-1 text-xs w-36"
      />
      <button
        type="button"
        disabled={!reason.trim() || cancelMutation.isPending}
        onClick={() => cancelMutation.mutate()}
        className="text-xs bg-red-600 text-white rounded px-2 py-1 disabled:opacity-40"
      >
        تأكيد
      </button>
      <button type="button" onClick={() => { setOpen(false); setError(null); }} className="text-xs text-gray-500 px-1">
        ×
      </button>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </div>
  );
}
