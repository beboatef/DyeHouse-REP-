import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MaterialIssuesApi, MaterialPreparationsApi, MaterialsApi, MaterialTransfersApi, MaterialUnit, WarehousesApi
} from "@/api/client";
import { ProductionOrdersApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

type Tab = "master" | "transfers" | "issues" | "preparations";

export default function MaterialsPage() {
  const [tab, setTab] = useState<Tab>("master");

  const tabs: { key: Tab; label: string }[] = [
    { key: "master", label: "المواد" },
    { key: "transfers", label: "التحويلات" },
    { key: "issues", label: "صرف المواد" },
    { key: "preparations", label: "الإحلال (التحضير)" }
  ];

  return (
    <>
      <PageHeader title="المواد والكيماويات" subtitle="بيانات المواد، التحويل بين المخازن، الصرف لأوامر التشغيل، والإحلال (التحضير/التخفيف)" />
      <div className="flex gap-2 mb-6 border-b border-gray-200">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${tab === t.key ? "border-brand-600 text-brand-700" : "border-transparent text-gray-500 hover:text-gray-700"}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "master" && <MaterialMasterTab />}
      {tab === "transfers" && <MaterialTransfersTab />}
      {tab === "issues" && <MaterialIssuesTab />}
      {tab === "preparations" && <MaterialPreparationsTab />}
    </>
  );
}

function MaterialMasterTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [unit, setUnit] = useState<MaterialUnit>("KG");
  const [purchasePrice, setPurchasePrice] = useState("");

  const { data: materials, isLoading } = useQuery({ queryKey: ["materials"], queryFn: () => MaterialsApi.list() });
  const createMutation = useMutation({
    mutationFn: () => MaterialsApi.create({ code, name, unit, purchasePrice: Number(purchasePrice || 0) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["materials"] }); setShowForm(false); setCode(""); setName(""); setPurchasePrice(""); }
  });

  return (
    <>
      <div className="flex justify-end mb-4">
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مادة جديدة"}</Button>
      </div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكود</label><Input value={code} onChange={(e) => setCode(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الاسم</label><Input value={name} onChange={(e) => setName(e.target.value)} required /></div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">الوحدة</label>
              <Select value={unit} onChange={(e) => setUnit(e.target.value as MaterialUnit)}>
                <option value="KG">كجم</option><option value="Gram">جرام</option><option value="Liter">لتر</option>
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">سعر الشراء</label><Input type="number" step="0.0001" min="0" value={purchasePrice} onChange={(e) => setPurchasePrice(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الكود</th><th className="text-start px-4 py-3 font-medium">الاسم</th>
            <th className="text-start px-4 py-3 font-medium">الوحدة</th><th className="text-start px-4 py-3 font-medium">سعر الشراء</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {materials?.map((m) => (
              <tr key={m.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{m.code}</td><td className="px-4 py-3">{m.name}</td>
                <td className="px-4 py-3"><Badge tone="blue">{m.unit}</Badge></td><td className="px-4 py-3 ltr-nums">{m.purchasePrice}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function MaterialTransfersTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [materialId, setMaterialId] = useState("");
  const [fromWarehouseId, setFromWarehouseId] = useState("");
  const [toWarehouseId, setToWarehouseId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "materials"], queryFn: () => WarehousesApi.list({ kind: "Materials" }) });
  const { data: transfers, isLoading } = useQuery({ queryKey: ["material-transfers"], queryFn: () => MaterialTransfersApi.list() });

  const createMutation = useMutation({
    mutationFn: () => MaterialTransfersApi.create({ materialId, fromWarehouseId, toWarehouseId, quantity: Number(quantity) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["material-transfers"] }); setShowForm(false); setQuantity(""); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحويل جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المادة</label>
              <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required>
                <option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">من مخزن</label>
              <Select value={fromWarehouseId} onChange={(e) => setFromWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">إلى مخزن</label>
              <Select value={toWarehouseId} onChange={(e) => setToWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>{warehouses?.filter((w) => w.id !== fromWarehouseId).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية</label><Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">المادة</th>
            <th className="text-start px-4 py-3 font-medium">من</th><th className="text-start px-4 py-3 font-medium">إلى</th><th className="text-start px-4 py-3 font-medium">الكمية</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{t.transferNumber}</td><td className="px-4 py-3">{t.materialCode}</td>
                <td className="px-4 py-3">{t.fromWarehouseName}</td><td className="px-4 py-3">{t.toWarehouseName}</td><td className="px-4 py-3 ltr-nums">{t.quantity}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function MaterialIssuesTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [materialId, setMaterialId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [productionOrderId, setProductionOrderId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "materials"], queryFn: () => WarehousesApi.list({ kind: "Materials" }) });
  const { data: orders } = useQuery({ queryKey: ["production-orders"], queryFn: () => ProductionOrdersApi.list() });
  const { data: issues, isLoading } = useQuery({ queryKey: ["material-issues"], queryFn: () => MaterialIssuesApi.list() });

  const createMutation = useMutation({
    mutationFn: () => MaterialIssuesApi.create({ materialId, warehouseId, productionOrderId, quantity: Number(quantity), unitCost: unitCost ? Number(unitCost) : undefined }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["material-issues"] }); setShowForm(false); setQuantity(""); setUnitCost(""); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ صرف جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المادة</label>
              <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required><option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المخزن</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required><option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">أمر التشغيل</label>
              <Select value={productionOrderId} onChange={(e) => setProductionOrderId(e.target.value)} required><option value="">اختر...</option>{orders?.map((o) => <option key={o.id} value={o.id}>{o.orderNumber}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية</label><Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">تكلفة الوحدة (اختياري)</label><Input type="number" step="0.0001" min="0" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">المادة</th>
            <th className="text-start px-4 py-3 font-medium">أمر التشغيل</th><th className="text-start px-4 py-3 font-medium">الكمية</th><th className="text-start px-4 py-3 font-medium">التكلفة الإجمالية</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {issues?.map((i) => (
              <tr key={i.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{i.issueNumber}</td><td className="px-4 py-3">{i.materialCode}</td>
                <td className="px-4 py-3 ltr-nums">{i.productionOrderNumber}</td><td className="px-4 py-3 ltr-nums">{i.quantity}</td><td className="px-4 py-3 ltr-nums">{i.totalCost}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function MaterialPreparationsTab() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [originalMaterialId, setOriginalMaterialId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [originalQuantity, setOriginalQuantity] = useState("");
  const [waterQuantity, setWaterQuantity] = useState("");
  const [resultingQuantity, setResultingQuantity] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "materials"], queryFn: () => WarehousesApi.list({ kind: "Materials" }) });
  const { data: preparations, isLoading } = useQuery({ queryKey: ["material-preparations"], queryFn: () => MaterialPreparationsApi.list() });

  const createMutation = useMutation({
    mutationFn: () => MaterialPreparationsApi.create({
      originalMaterialId, warehouseId, originalQuantity: Number(originalQuantity),
      waterQuantity: Number(waterQuantity || 0), resultingQuantity: Number(resultingQuantity)
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["material-preparations"] }); setShowForm(false); setOriginalQuantity(""); setWaterQuantity(""); setResultingQuantity(""); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحضير جديد"}</Button></div>
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المادة الأصلية</label>
              <Select value={originalMaterialId} onChange={(e) => setOriginalMaterialId(e.target.value)} required><option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">المخزن</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required><option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}</Select>
            </div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية الأصلية</label><Input type="number" step="0.001" min="0" value={originalQuantity} onChange={(e) => setOriginalQuantity(e.target.value)} required /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">كمية الماء/المذيب</label><Input type="number" step="0.001" min="0" value={waterQuantity} onChange={(e) => setWaterQuantity(e.target.value)} /></div>
            <div><label className="block text-xs font-medium text-gray-600 mb-1">الكمية الناتجة</label><Input type="number" step="0.001" min="0" value={resultingQuantity} onChange={(e) => setResultingQuantity(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الرقم</th><th className="text-start px-4 py-3 font-medium">المادة</th>
            <th className="text-start px-4 py-3 font-medium">الأصلية</th><th className="text-start px-4 py-3 font-medium">الماء</th><th className="text-start px-4 py-3 font-medium">الناتج</th><th className="text-start px-4 py-3 font-medium">التكلفة</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {preparations?.map((p) => (
              <tr key={p.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{p.preparationNumber}</td><td className="px-4 py-3">{p.originalMaterialCode}</td>
                <td className="px-4 py-3 ltr-nums">{p.originalQuantity}</td><td className="px-4 py-3 ltr-nums">{p.waterQuantity}</td>
                <td className="px-4 py-3 ltr-nums">{p.resultingQuantity}</td><td className="px-4 py-3 ltr-nums">{p.cost ?? "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
