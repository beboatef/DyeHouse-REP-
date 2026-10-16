import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MaterialIssuesApi, MaterialPreparationsApi, MaterialsApi, MaterialTransfersApi, MaterialUnit,
  ProductionOrdersApi, ProductionStagesApi, PurchasesApi, WarehousesApi
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";
import StockTraceability from "@/components/StockTraceability";
import { MaterialsExports } from "@/api/exports";

type Tab = "balances" | "master" | "receipts" | "issues" | "transfers" | "preparations";

export default function MaterialsPage() {
  const [tab, setTab] = useState<Tab>("balances");

  const tabs: { key: Tab; label: string }[] = [
    { key: "balances", label: "الأرصدة والحركات" },
    { key: "master", label: "أصناف المواد والكيماويات" },
    { key: "receipts", label: "الاستلام من الموردين" },
    { key: "issues", label: "صرف لأوامر/مراحل التشغيل" },
    { key: "transfers", label: "التحويلات بين المخازن" },
    { key: "preparations", label: "الإحلال (التحضير)" }
  ];

  return (
    <>
      <PageHeader title="مخزن المواد والكيماويات" subtitle="وحدة مستقلة للمواد والكيماويات الخاصة بالمصنع: الأرصدة والحركات، الاستلام من الموردين، الصرف على أوامر ومراحل التشغيل وحساب التكلفة" />
      <div className="tabs mb-6 flex-wrap">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`tab ${tab === t.key ? "tab-active" : ""}`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "balances" && <StockTraceability scope="materials" />}
      {tab === "master" && <MaterialMasterTab />}
      {tab === "receipts" && <MaterialReceiptsTab />}
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
        <Card className="card-pad mb-6">
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
            <th>النوع</th><th>الوحدة</th><th>سعر الشراء</th><th>الرصيد الحالي</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {materials?.map((m) => (
              <tr key={m.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{m.code}</td><td>{m.name}</td>
                <td><Badge tone="gray">{m.kind === "OperatingSupply" ? "مستلزمات تشغيل" : "كيماويات إنتاج"}</Badge></td>
                <td><Badge tone="blue">{m.unit}</Badge></td>
                <td className="ltr-nums">{m.purchasePrice}</td>
                <td className="ltr-nums font-semibold">{m.currentBalance} {m.unit}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

function MaterialReceiptsTab() {
  const { data: receipts, isLoading } = useQuery({
    queryKey: ["purchase-receipts"],
    queryFn: () => PurchasesApi.receipts()
  });

  return (
    <Card>
      <div className="card-pad flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="card-title">حركات استلام المواد والكيماويات من الموردين</h3>
          <p className="text-xs text-ink-subtle">الكميات المستلمة فعليًا في المخزن بوحدة كل مادة المسجلة، مع المورد وأمر الشراء المرتبط</p>
        </div>
        <Link className="btn-link" to="/purchases?tab=receipts">+ تسجيل إذن استلام جديد</Link>
      </div>
      <table className="table">
        <thead>
          <tr>
            <th>رقم إذن الاستلام</th>
            <th>التاريخ</th>
            <th>المورد</th>
            <th>أمر الشراء</th>
            <th>المخزن</th>
            <th>الأصناف المستلمة والوحدات</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
          {!isLoading && (receipts?.length ?? 0) === 0 && (
            <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">لا توجد أذون استلام مسجلة</td></tr>
          )}
          {receipts?.map((r) => (
            <tr key={r.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
              <td className="font-medium ltr-nums">{r.receiptNumber}</td>
              <td className="ltr-nums text-ink-muted">{r.receiptDate.slice(0, 10)}</td>
              <td>{r.supplierCode} - {r.supplierName}</td>
              <td className="ltr-nums">{r.orderNumber ?? "—"}</td>
              <td>{r.warehouseName}</td>
              <td>
                <div className="space-y-1 text-xs">
                  {r.lines.map((l) => (
                    <div key={l.id} className="ltr-nums">
                      <span className="font-medium">{l.materialCode}</span> ({l.materialName}): {l.quantity} {l.unit}
                    </div>
                  ))}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
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
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["material-transfers"] });
      qc.invalidateQueries({ queryKey: ["materials"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setShowForm(false); setQuantity(""); setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحويل جديد"}</Button></div>
      {showForm && (
        <Card className="card-pad mb-6">
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
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {transfers?.map((t) => (
              <tr key={t.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
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
  const [stageName, setStageName] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: materials } = useQuery({ queryKey: ["materials", "active"], queryFn: () => MaterialsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "materials"], queryFn: () => WarehousesApi.list({ kind: "Materials" }) });
  const { data: orders } = useQuery({ queryKey: ["production-orders"], queryFn: () => ProductionOrdersApi.list() });
  const { data: stages } = useQuery({ queryKey: ["production-stages", "active"], queryFn: () => ProductionStagesApi.list({ activeOnly: true }) });
  const { data: issues, isLoading } = useQuery({ queryKey: ["material-issues"], queryFn: () => MaterialIssuesApi.list() });

  const selectedMaterial = materials?.find((m) => m.id === materialId);
  const composeNotes = () => {
    const parts = [stageName ? `مرحلة: ${stageName}` : "", notes.trim()].filter(Boolean);
    return parts.length > 0 ? parts.join(" — ") : undefined;
  };

  const createMutation = useMutation({
    mutationFn: () => MaterialIssuesApi.create({
      materialId, warehouseId, productionOrderId,
      quantity: Number(quantity),
      unitCost: unitCost ? Number(unitCost) : undefined,
      notes: composeNotes()
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["material-issues"] });
      qc.invalidateQueries({ queryKey: ["materials"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setShowForm(false); setQuantity(""); setUnitCost(""); setStageName(""); setNotes(""); setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "حدث خطأ")
  });

  // Reversal, not a delete: the API keeps the original OUT ledger row and posts a compensating IN row.
  // A reason is mandatory (the backend validator refuses an empty one), and a second cancellation is refused.
  const cancelMutation = useMutation({
    mutationFn: (id: string) => MaterialIssuesApi.cancel(id, window.prompt("سبب إلغاء صرف المواد") || ""),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["material-issues"] });
      qc.invalidateQueries({ queryKey: ["materials"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "حدث خطأ")
  });

  const unitByMaterialCode = (code: string) => materials?.find((m) => m.code === code)?.unit ?? "";

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ صرف جديد"}</Button></div>
      {showForm && (
        <Card className="card-pad mb-6">
          <form className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div><label className="field-label">المادة</label>
              <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required>
                <option value="">اختر...</option>
                {materials?.map((m) => <option key={m.id} value={m.id}>{m.code} - {m.name} ({m.unit})</option>)}
              </Select>
            </div>
            <div><label className="field-label">المخزن</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required><option value="">اختر...</option>{warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}</Select>
            </div>
            <div><label className="field-label">أمر التشغيل</label>
              <Select value={productionOrderId} onChange={(e) => setProductionOrderId(e.target.value)} required><option value="">اختر...</option>{orders?.map((o) => <option key={o.id} value={o.id}>{o.orderNumber}</option>)}</Select>
            </div>
            <div><label className="field-label">مرحلة الإنتاج (اختياري)</label>
              <Select value={stageName} onChange={(e) => setStageName(e.target.value)}>
                <option value="">كل المراحل / عام على الأمر</option>
                {stages?.map((s) => <option key={s.id} value={s.name}>{s.code} - {s.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="field-label">الكمية {selectedMaterial ? `(${selectedMaterial.unit})` : ""}</label>
              <Input type="number" step="0.001" min="0" value={quantity} onChange={(e) => setQuantity(e.target.value)} required />
            </div>
            <div><label className="field-label">تكلفة الوحدة (اختياري)</label><Input type="number" step="0.0001" min="0" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} /></div>
            <div className="sm:col-span-2"><label className="field-label">ملاحظات</label><Input value={notes} onChange={(e) => setNotes(e.target.value)} /></div>
            <Button type="submit" disabled={createMutation.isPending}>حفظ</Button>
          </form>
          {error && <p className="form-error mt-2">{error}</p>}
        </Card>
      )}
      {error && !showForm && <p className="form-error mb-2">{error}</p>}
      <Card>
        <table className="table">
          <thead><tr>
            <th>الرقم</th><th>التاريخ</th><th>المادة</th>
            <th>أمر التشغيل</th><th>المرحلة / بيان الصرف</th><th>الكمية والوحدة</th><th>التكلفة الإجمالية</th><th>الحالة</th><th>إجراءات</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={9} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {issues?.map((i) => (
              <tr key={i.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{i.issueNumber}</td>
                <td className="ltr-nums text-ink-muted">{i.issueDate.slice(0, 10)}</td>
                <td>{i.materialCode} - {i.materialName}</td>
                <td className="ltr-nums"><Link className="text-brand-600 hover:underline" to={`/production-orders/${i.productionOrderId}`}>{i.productionOrderNumber}</Link></td>
                <td className="text-xs text-ink-muted">{i.notes ?? "—"}</td>
                <td className="ltr-nums font-medium">{i.quantity} {unitByMaterialCode(i.materialCode)}</td>
                <td className="ltr-nums">{i.totalCost}</td>
                <td>
                  <Badge tone={i.isCancelled ? "red" : "green"}>{i.isCancelled ? "ملغي" : "مُرحَّل"}</Badge>
                  {i.isCancelled && i.cancellationReason && (
                    <div className="text-xs text-ink-subtle">{i.cancellationReason}</div>
                  )}
                </td>
                <td>
                  {!i.isCancelled && (
                    <Button variant="ghost" onClick={() => cancelMutation.mutate(i.id)} disabled={cancelMutation.isPending}>
                      إلغاء
                    </Button>
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
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["material-preparations"] });
      qc.invalidateQueries({ queryKey: ["materials"] });
      qc.invalidateQueries({ queryKey: ["stock-traceability"] });
      setShowForm(false); setOriginalQuantity(""); setWaterQuantity(""); setResultingQuantity(""); setError(null);
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.title ?? "حدث خطأ")
  });

  return (
    <>
      <div className="flex justify-end mb-4"><Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ تحضير جديد"}</Button></div>
      {showForm && (
        <Card className="card-pad mb-6">
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
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-ink-subtle">جارٍ التحميل...</td></tr>}
            {preparations?.map((p) => (
              <tr key={p.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
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
