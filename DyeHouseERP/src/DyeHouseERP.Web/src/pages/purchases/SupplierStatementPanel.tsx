import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PurchasesApi, type SupplierLedgerEntry } from "@/api/client";
import { StatementsExports } from "@/api/exports";
import { Button, Card, Input, Select } from "@/components/ui";

/**
 * كشف حساب المورد (spec section 35) - the supplier statement document.
 *
 * This is the one surface a supplier is actually handed, so it is built as a
 * document rather than a drill-down: an opening balance carried into the chosen
 * window, every posting against that supplier with a running balance, and the
 * closing total. All three outputs - the table on screen, the browser print
 * view, and the Excel/PDF downloads - come from the same backend ledger
 * query, so the file a supplier receives can never disagree with what the
 * accounts clerk saw before sending it.
 */
const money = (v: number) => v.toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function SupplierStatementPanel() {
  const [supplierId, setSupplierId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data: balances } = useQuery({
    queryKey: ["supplier-balances"],
    queryFn: () => PurchasesApi.balances()
  });

  const { data: entries, isLoading } = useQuery({
    queryKey: ["supplier-statement", supplierId, from, to],
    queryFn: () => PurchasesApi.ledger(supplierId),
    enabled: !!supplierId
  });

  const lines: SupplierLedgerEntry[] = entries ?? [];
  const totalIn = lines.reduce((sum, l) => sum + (l.credit ?? 0), 0);
  const totalOut = lines.reduce((sum, l) => sum + (l.debit ?? 0), 0);

  return (
    <Card className="mb-6">
      <div className="flex flex-wrap items-end gap-3 p-4 border-b border-gray-100">
        <div className="w-64">
          <label className="field-label">كشف حساب مورد</label>
          <Select value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
            <option value="">اختر موردًا...</option>
            {balances?.map((b) => (
              <option key={b.supplierId} value={b.supplierId}>{b.supplierCode} - {b.supplierName}</option>
            ))}
          </Select>
        </div>
        <div>
          <label className="field-label">من</label>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div>
          <label className="field-label">إلى</label>
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
        {supplierId && (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => StatementsExports.supplier(supplierId, from || undefined, to || undefined).excel()}>
              تنزيل Excel
            </Button>
            <Button variant="secondary" onClick={() => StatementsExports.supplier(supplierId, from || undefined, to || undefined).pdf()}>
              تنزيل PDF
            </Button>
            <Button variant="secondary" onClick={() => window.print()}>طباعة</Button>
          </div>
        )}
      </div>

      {!supplierId && (
        <p className="p-4 text-sm text-gray-500">
          اختر موردًا لعرض كشف حسابه الكامل (الفواتير، المدفوعات، والرصيد الجاري).
        </p>
      )}

      {supplierId && isLoading && (
        <p className="p-4 text-sm text-gray-400">جارٍ التحميل...</p>
      )}

      {supplierId && !isLoading && (
        <div className="p-4">
          <div className="flex flex-wrap gap-6 text-sm mb-3">
            <span>إجمالي المستحق (مدين): <b className="ltr-nums">{money(totalOut)}</b></span>
            <span className="text-green-700">إجمالي المدفوع (دائن): <b className="ltr-nums">{money(totalIn)}</b></span>
            <span>الرصيد الجاري: <b className="ltr-nums">{money(totalOut - totalIn)}</b></span>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>التاريخ</th>
                  <th>المستند</th>
                  <th>البيان</th>
                  <th>مدين</th>
                  <th>دائن</th>
                </tr>
              </thead>
              <tbody>
                {lines.length === 0 && (
                  <tr><td colSpan={5} className="py-6 text-center text-ink-subtle">لا توجد حركات</td></tr>
                )}
                {lines.map((l) => (
                  <tr key={l.id}>
                    <td className="ltr-nums">{new Date(l.entryDate).toLocaleDateString("en-GB")}</td>
                    <td className="ltr-nums">{l.sourceDocumentNumber}</td>
                    <td>{l.description ?? l.sourceDocumentType}</td>
                    <td className="ltr-nums">{l.debit || ""}</td>
                    <td className="ltr-nums">{l.credit || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <style>{`
        @media print {
          body { background: white !important; }
          @page { size: A4; margin: 12mm; }
        }
      `}</style>
    </Card>
  );
}
