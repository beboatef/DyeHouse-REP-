import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MaterialIssuesApi, MaterialPreparationsApi, MaterialsApi, MaterialTransfersApi, MaterialUnit, WarehousesApi
} from "@/api/client";
import { ProductionOrdersApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";
import { MaterialsExports } from "@/api/exports";

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
      {tab === "transfers" && (
        <>
          <div className="flex justify-end mb-3">
            <ExportButtons
              excel={{ action: MaterialsExports.transfers.excel }}
              pdf={{ action: MaterialsExports.transfers.pdf }}
            />
          </div>
          <MaterialTransfersTab />
        </>
      )}
      {tab === "issues" && (
        <>
          <div className="flex justify-end mb-3">
            <ExportButtons
              excel={{ action: MaterialsExports.issues.excel }}
              pdf={{ action: MaterialsExports.issues.pdf }}
            />
          </div>
          <MaterialIssuesTab />
        </>
      )}
      {tab === "preparations" && (
        <>
          <div className="flex justify-end mb-3">
            <ExportButtons
              excel={{ action: MaterialsExports.preparations.excel }}
              pdf={{ action: MaterialsExports.preparations.pdf }}
            />
          </div>
          <MaterialPreparationsTab />
        </>
      )}
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
      <div className="flex justify-end gap-2 mb-4">
        <ExportButtons
          excel={{ action: MaterialsExports.excel }}
          pdf={{ action: MaterialsExports.pdf }}
        />
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مادة جديدة"}</Button>
      </div>

      <ImportPanel
        base="/materials/import"
        columns={["Code", "Name", "Unit", "PurchasePrice", "Kind", "ReorderLevel"]}
        title="استيراد المواد والكيماويات من Excel"
        onDone={() => qc.invalidateQueries({ queryKey: ["materials"] })}
      />
      {showForm && (
        <Card className="p-5 mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="field-label">الكود</label><Input value={code} onChange={(e) => setCode(e.target.value)} required /></div>
            <div><label className="field-label">الاسم</label><Input value={name} onChange={(e) => setName(e.target.value)} required /></div>
            <div>
              <label className="field-label">الوحدة</label>
              <Select value={unit} onChange={(e) => setUnit(e.target.value as MaterialUnit)}>
                <option value="KG">كجم</option><option value="Gram">جرام</option><option value="Liter">لتر</option>
              </Select>
            </div>
            <div><label className="field-label">سعر الشراء</label><Input type="number" step="0.0001" min="0" value={purchasePrice} onChange={(e) => setPurchasePrice(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
        </Card>
      )}
      <Card>
        <table className="table">
          <thead><tr>
            <th>الكود</th><th>الاسم</th>
            <th>الوحدة</th><th>سعر الشراء</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {materials?.map((m) => (
              <tr key={m.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{m.code}</td><td>{m.name}</td>
                <td><Badge tone="blue">{m.unit}</Badge></td><td className="ltr-nums">{m.purchasePrice}</td>
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
            <div><label className="field-label">المادة</label>
              <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required>
                <option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}
              </Select>
            </div>
            <div><label className="field-label">من مخزن</label>
              <Select value={fromWarehouseId} onChange={(e) => setFromWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div><label className="field-label">إلى مخزن</label>
              <Select value={toWarehouseId} onChange={(e) => setToWarehouseId(e.target.value)} required>
                <option value="">اختر...</option>{warehouses?.filter((w) => w.id !== fromWarehouseId).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div><label className="field-label">الكمية</label><Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>المادة</th>
            <th>من</th><th>إلى</th><th>الكمية</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{t.transferNumber}</td><td>{t.materialCode}</td>
                <td>{t.fromWarehouseName}</td><td>{t.toWarehouseName}</td><td className="ltr-nums">{t.quantity}</td>
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
            <div><label className="field-label">المادة</label>
              <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required><option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}</Select>
            </div>
            <div><label className="field-label">المخزن</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required><option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}</Select>
            </div>
            <div><label className="field-label">أمر التشغيل</label>
              <Select value={productionOrderId} onChange={(e) => setProductionOrderId(e.target.value)} required><option value="">اختر...</option>{orders?.map((o) => <option key={o.id} value={o.id}>{o.orderNumber}</option>)}</Select>
            </div>
            <div><label className="field-label">الكمية</label><Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required /></div>
            <div><label className="field-label">تكلفة الوحدة (اختياري)</label><Input type="number" step="0.0001" min="0" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>المادة</th>
            <th>أمر التشغيل</th><th>الكمية</th><th>التكلفة الإجمالية</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {issues?.map((i) => (
              <tr key={i.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{i.issueNumber}</td><td>{i.materialCode}</td>
                <td className="ltr-nums">{i.productionOrderNumber}</td><td className="ltr-nums">{i.quantity}</td><td className="ltr-nums">{i.totalCost}</td>
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
            <div><label className="field-label">المادة الأصلية</label>
              <Select value={originalMaterialId} onChange={(e) => setOriginalMaterialId(e.target.value)} required><option value="">اختر...</option>{materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name}</option>)}</Select>
            </div>
            <div><label className="field-label">المخزن</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required><option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}</Select>
            </div>
            <div><label className="field-label">الكمية الأصلية</label><Input type="number" step="0.001" min="0" value={originalQuantity} onChange={(e) => setOriginalQuantity(e.target.value)} required /></div>
            <div><label className="field-label">كمية الماء/المذيب</label><Input type="number" step="0.001" min="0" value={waterQuantity} onChange={(e) => setWaterQuantity(e.target.value)} /></div>
            <div><label className="field-label">الكمية الناتجة</label><Input type="number" step="0.001" min="0" value={resultingQuantity} onChange={(e) => setResultingQuantity(e.target.value)} required /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>المادة</th>
            <th>الأصلية</th><th>الماء</th><th>الناتج</th><th>التكلفة</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {preparations?.map((p) => (
              <tr key={p.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{p.preparationNumber}</td><td>{p.originalMaterialCode}</td>
                <td className="ltr-nums">{p.originalQuantity}</td><td className="ltr-nums">{p.waterQuantity}</td>
                <td className="ltr-nums">{p.resultingQuantity}</td><td className="ltr-nums">{p.cost ?? "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
