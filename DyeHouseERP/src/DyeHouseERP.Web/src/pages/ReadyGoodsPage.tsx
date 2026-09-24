import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionOrdersApi, ReadyGoodsApi, WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";

export default function ReadyGoodsPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [productionOrderId, setProductionOrderId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [qtyKg, setQtyKg] = useState("");
  const [qtyMeter, setQtyMeter] = useState("");
  const [pieceCount, setPieceCount] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: orders } = useQuery({ queryKey: ["production-orders", "completed-eligible"], queryFn: () => ProductionOrdersApi.list() });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "ready"], queryFn: () => WarehousesApi.list({ kind: "ReadyGoods" }) });
  const { data: balance, isLoading: balanceLoading } = useQuery({ queryKey: ["ready-goods-balance"], queryFn: () => ReadyGoodsApi.balance() });
  const { data: transfers, isLoading: transfersLoading } = useQuery({ queryKey: ["ready-goods-transfers"], queryFn: () => ReadyGoodsApi.listTransfers() });

  const createMutation = useMutation({
    mutationFn: () => ReadyGoodsApi.createTransfer({
      productionOrderId, warehouseId,
      quantityKg: qtyKg ? Number(qtyKg) : undefined,
      quantityMeter: qtyMeter ? Number(qtyMeter) : undefined,
      pieceCount: pieceCount ? Number(pieceCount) : undefined
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["ready-goods-balance"] });
      qc.invalidateQueries({ queryKey: ["ready-goods-transfers"] });
      setShowForm(false); setQtyKg(""); setQtyMeter(""); setPieceCount(""); setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  return (
    <>
      <PageHeader
        title="المخزون الجاهز"
        subtitle="ترحيل الإنتاج المكتمل إلى مخزن الجاهز، والرصيد المتاح لكل أمر تشغيل"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ ترحيل جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="sm:col-span-2">
              <label className="block text-xs font-medium text-gray-600 mb-1">أمر التشغيل</label>
              <Select value={productionOrderId} onChange={(e) => setProductionOrderId(e.target.value)} required>
                <option value="">اختر...</option>
                {orders?.map((o) => <option key={o.id} value={o.id}>{o.orderNumber} - {o.customerCode} - {o.itemCode}</option>)}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">مخزن الجاهز</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>
                {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية (كجم)</label><Input type="number" step="0.001" min="0" value={qtyKg} onChange={(e) => setQtyKg(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية (متر)</label><Input type="number" step="0.001" min="0" value={qtyMeter} onChange={(e) => setQtyMeter(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">عدد القطع (وصفي)</label><Input type="number" min="0" value={pieceCount} onChange={(e) => setPieceCount(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "ترحيل"}</Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        </Card>
      )}

      <h2 className="font-semibold text-gray-800 mb-3">الرصيد الجاهز الحالي</h2>
      <Card className="mb-8">
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">أمر التشغيل</th><th className="text-start px-4 py-3 font-medium">العميل</th>
            <th className="text-start px-4 py-3 font-medium">الصنف</th><th className="text-start px-4 py-3 font-medium">اللون</th><th className="text-start px-4 py-3 font-medium">الرصيد</th>
          </tr></thead>
          <tbody>
            {balanceLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!balanceLoading && balance?.length === 0 && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">لا يوجد رصيد جاهز حاليًا</td></tr>}
            {balance?.map((b) => (
              <tr key={`${b.productionOrderId}-${b.itemId}`} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{b.productionOrderNumber}</td><td className="px-4 py-3">{b.customerCode}</td>
                <td className="px-4 py-3">{b.itemCode}</td><td className="px-4 py-3">{b.color ?? "-"}</td>
                <td className="px-4 py-3 ltr-nums font-medium">{b.remainingKg > 0 && `${b.remainingKg} كجم `}{b.remainingMeter > 0 && `${b.remainingMeter} م`}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      <h2 className="font-semibold text-gray-800 mb-3">سجل الترحيلات</h2>
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">أمر التشغيل</th>
            <th className="text-start px-4 py-3 font-medium">العميل</th><th className="text-start px-4 py-3 font-medium">الكمية</th>
          </tr></thead>
          <tbody>
            {transfersLoading && <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{t.transferNumber}</td><td className="px-4 py-3 ltr-nums">{t.productionOrderNumber}</td>
                <td className="px-4 py-3">{t.customerCode}</td>
                <td className="px-4 py-3 ltr-nums">{t.quantityKg != null && `${t.quantityKg} كجم `}{t.quantityMeter != null && `${t.quantityMeter} م`}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
