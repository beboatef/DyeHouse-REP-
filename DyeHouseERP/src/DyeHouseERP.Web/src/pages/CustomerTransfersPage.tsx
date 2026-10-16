import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApprovalsApi, CustomersApi, CustomerTransfersApi, ItemsApi, RawMessagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { CustomerTransfersExports } from "@/api/exports";

export default function CustomerTransfersPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [fromCustomerId, setFromCustomerId] = useState("");
  const [toCustomerId, setToCustomerId] = useState("");
  const [itemId, setItemId] = useState("");
  const [rawMessageId, setRawMessageId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [reason, setReason] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [negativeStockDetail, setNegativeStockDetail] = useState<{ shortage: number; reason: string } | null>(null);
  const [approvalRequested, setApprovalRequested] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: messages } = useQuery({
    queryKey: ["raw-messages", "for-transfer", fromCustomerId],
    queryFn: () => RawMessagesApi.list({ customerId: fromCustomerId, onlyWithBalance: true }),
    enabled: !!fromCustomerId
  });
  const { data: transfers, isLoading } = useQuery({ queryKey: ["customer-transfers"], queryFn: () => CustomerTransfersApi.list() });

  const createMutation = useMutation({
    mutationFn: () =>
      CustomerTransfersApi.create({
        fromCustomerId, toCustomerId, rawMessageId, itemId, reason,
        quantityKg: qtyKg ? Number(qtyKg) : undefined,
        quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
        notes: notes || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customer-transfers"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setShowForm(false);
      setQtyKg(""); setQtyMeter(""); setReason(""); setNotes(""); setError(null); setNegativeStockDetail(null);
      setApprovalRequested(null);
    },
    onError: (err: ApiErrorLike) => {
      if (err?.response?.data?.code === "NEGATIVE_STOCK") {
        setNegativeStockDetail({ shortage: err.response?.data?.shortage ?? 0, reason: "" });
      } else {
        setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ");
      }
    }
  });

  /**
   * Spec section 24: an override is never taken by the person who wants it.
   * The screen SUBMITS the transfer it would otherwise post, and a different
   * user with the approval permission decides it in the Approval Center - only
   * their approval executes the movement, and it is bound to exactly these
   * facts (sender, receiver, lot, item, quantities).
   */
  const requestApprovalMutation = useMutation({
    mutationFn: (approvalReason: string) =>
      ApprovalsApi.submit({
        type: "CustomerTransfer",
        reason: approvalReason,
        payloadJson: JSON.stringify({
          fromCustomerId, toCustomerId, rawMessageId, itemId,
          quantityKg: qtyKg ? Number(qtyKg) : undefined,
          quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
          reason,
          notes: notes || undefined
        })
      }),
    onSuccess: (request) => {
      setNegativeStockDetail(null);
      setApprovalRequested(request.documentNumber);
      qc.invalidateQueries({ queryKey: ["approval-requests"] });
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "تعذر إرسال طلب الاعتماد")
  });

  return (
    <>
      <PageHeader
        title="تحويل الخام بين العملاء"
        subtitle="لا يتم تعديل بيانات الرسالة الأصلية أبدًا - كل تحويل يُسجَّل كحركة مستقلة وقابلة للتتبع بالكامل"
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: CustomerTransfersExports.excel }}
              pdf={{ action: CustomerTransfersExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحويل جديد"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="card-pad mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              setApprovalRequested(null);
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="field-label">من عميل</label>
                <Select value={fromCustomerId} onChange={(e) => { setFromCustomerId(e.target.value); setRawMessageId(""); }} required>
                  <option value="">اختر...</option>
                  {customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">إلى عميل</label>
                <Select value={toCustomerId} onChange={(e) => setToCustomerId(e.target.value)} required>
                  <option value="">اختر...</option>
                  {customers?.filter((c) => c.id !== fromCustomerId).map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
                </Select>
              </div>
              <div>
                <label className="field-label">الصنف</label>
                <Select value={itemId} onChange={(e) => setItemId(e.target.value)} required>
                  <option value="">اختر الصنف...</option>
                  {items?.map((i) => <option key={i.id} value={i.id}>{i.code} - {i.name}</option>)}
                </Select>
              </div>
              <div className="sm:col-span-2">
                <label className="field-label">الرسالة (المرسال)</label>
                <Select value={rawMessageId} onChange={(e) => setRawMessageId(e.target.value)} required disabled={!fromCustomerId}>
                  <option value="">اختر الرسالة...</option>
                  {messages?.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.messageNumber} - {m.lines.map((l) => `${l.itemCode}: ${l.remainingKg ?? l.remainingMeter ?? 0}`).join(", ")}
                    </option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="field-label">الكمية (كجم)</label>
                <Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} />
              </div>
              <div>
                <label className="field-label">الكمية (متر)</label>
                <Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} />
              </div>
              <div className="sm:col-span-2">
                <label className="field-label">سبب التحويل</label>
                <Input value={reason} onChange={(e) => setReason(e.target.value)} required />
              </div>
            </div>
            <div>
              <label className="field-label">ملاحظات</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
            {error && <p className="form-error">{error}</p>}

            {negativeStockDetail && (
              <Card className="card-pad border-warning/30 bg-warning-soft">
                <p className="text-sm text-warning-ink">
                  الكمية تتجاوز رصيد العميل المرسل بمقدار <span className="ltr-nums font-bold">{negativeStockDetail.shortage}</span>.
                  لا يتم تجاوز الرصيد السالب هنا: يُرسل الطلب إلى مركز الاعتمادات، ولا ينفّذه إلا مستخدم آخر
                  يملك صلاحية اعتماد تجاوز الرصيد السالب. لا يمكن للمستخدم اعتماد طلبه بنفسه.
                </p>
                <div className="flex gap-2 mt-2">
                  <Input placeholder="سبب طلب الاعتماد..." value={negativeStockDetail.reason}
                    onChange={(e) => setNegativeStockDetail({ ...negativeStockDetail, reason: e.target.value })} />
                  <Button type="button" variant="secondary"
                    disabled={!negativeStockDetail.reason || requestApprovalMutation.isPending}
                    onClick={() => { setError(null); requestApprovalMutation.mutate(negativeStockDetail.reason); }}>
                    {requestApprovalMutation.isPending ? "جارٍ الإرسال..." : "طلب اعتماد"}
                  </Button>
                </div>
                <p className="mt-2 text-2xs text-warning-ink">
                  الاعتماد مرتبط بنفس التحويل ونفس الكميات والوحدة والعميلين - أي تغيير في التفاصيل يتطلب طلبًا جديدًا.
                </p>
              </Card>
            )}

            {approvalRequested && (
              <p className="text-sm text-success-ink">
                تم إرسال طلب اعتماد على الرسالة <span className="ltr-nums font-bold">{approvalRequested}</span>.
                لن يتم الحفظ إلا بعد موافقة مستخدم آخر من مركز الاعتمادات.
              </p>
            )}
          </form>
        </Card>
      )}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>رقم التحويل</th>
              <th>من</th>
              <th>إلى</th>
              <th>الصنف</th>
              <th>الرسالة</th>
              <th>الكمية</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {!isLoading && transfers?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">لا توجد تحويلات بعد</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{t.transferNumber}</td>
                <td>{t.fromCustomerCode}</td>
                <td>{t.toCustomerCode}</td>
                <td>{t.itemCode}</td>
                <td className="ltr-nums">{t.messageNumber}</td>
                <td className="ltr-nums">
                  {t.quantityKg != null && `${t.quantityKg} كجم `}
                  {t.quantityMeter != null && `${t.quantityMeter} م`}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
