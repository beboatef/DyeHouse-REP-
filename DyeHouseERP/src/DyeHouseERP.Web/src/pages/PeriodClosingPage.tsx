import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PeriodClosingApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";

export default function PeriodClosingPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [reopenReasonById, setReopenReasonById] = useState<Record<string, string>>({});

  const { data: periods, isLoading } = useQuery({ queryKey: ["period-closes"], queryFn: () => PeriodClosingApi.list() });

  const closeMutation = useMutation({
    mutationFn: () => PeriodClosingApi.close({ periodStart, periodEnd, notes: notes || undefined }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["period-closes"] });
      setShowForm(false); setPeriodStart(""); setPeriodEnd(""); setNotes(""); setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ - تأكد أن حسابك يملك صلاحية settings.manage")
  });

  const reopenMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => PeriodClosingApi.reopen(vars.id, vars.reason),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["period-closes"] })
  });

  return (
    <>
      <PageHeader
        title="إقفال الفترات المحاسبية"
        subtitle="بعد إقفال فترة، لا يمكن إضافة فواتير أو مقبوضات أو مدفوعات أو تسويات مخزون بتاريخ داخلها إلا بعد إعادة الفتح (وهي عملية موثقة بالكامل)"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ إقفال فترة جديدة"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); closeMutation.mutate(); }}>
            <div><label className="field-label">من تاريخ</label><Input type="date" value={periodStart} onChange={(e) => setPeriodStart(e.target.value)} required /></div>
            <div><label className="field-label">إلى تاريخ</label><Input type="date" value={periodEnd} onChange={(e) => setPeriodEnd(e.target.value)} required /></div>
            <div><label className="field-label">ملاحظات</label><Input value={notes} onChange={(e) => setNotes(e.target.value)} /></div>
            <Button type="submit" disabled={closeMutation.isPending}>{closeMutation.isPending ? "جارٍ الإقفال..." : "إقفال"}</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}

      <Card>
        <table className="table">
          <thead><tr>
            <th>من</th><th>إلى</th>
            <th>بواسطة</th><th>الحالة</th><th></th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && periods?.length === 0 && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">لا توجد فترات مقفلة</td></tr>}
            {periods?.map((p) => (
              <tr key={p.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="ltr-nums">{new Date(p.periodStart).toLocaleDateString("en-GB")}</td>
                <td className="ltr-nums">{new Date(p.periodEnd).toLocaleDateString("en-GB")}</td>
                <td>{p.createdBy}</td>
                <td><Badge tone={p.isReopened ? "gray" : "red"}>{p.isReopened ? "معاد فتحها" : "مقفلة"}</Badge></td>
                <td>
                  {!p.isReopened && (
                    <div className="flex items-center gap-2">
                      <Input placeholder="سبب إعادة الفتح..." value={reopenReasonById[p.id] ?? ""} onChange={(e) => setReopenReasonById((r) => ({ ...r, [p.id]: e.target.value }))} className="w-40" />
                      <Button variant="ghost" disabled={!reopenReasonById[p.id]} onClick={() => reopenMutation.mutate({ id: p.id, reason: reopenReasonById[p.id] })}>إعادة فتح</Button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
