import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SeparatesApi, SeparateStatus, ProductionStagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

const statusLabel: Record<SeparateStatus, string> = {
  PendingReprocessing: "بانتظار إعادة التشغيل",
  Reprocessing: "قيد إعادة التشغيل",
  Reprocessed: "تمت إعادة تشغيلها",
  Ready: "جاهزة",
  Scrapped: "تالفة"
};
const statusTone: Record<SeparateStatus, "yellow" | "blue" | "green" | "red" | "gray"> = {
  PendingReprocessing: "yellow", Reprocessing: "blue", Reprocessed: "green", Ready: "green", Scrapped: "red"
};

export default function SeparatesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const { data: separates, isLoading } = useQuery({ queryKey: ["separates"], queryFn: () => SeparatesApi.list() });
  const [scrapReasonById, setScrapReasonById] = useState<Record<string, string>>({});
  const [selectedSeparateIds, setSelectedSeparateIds] = useState<string[]>([]);
  const [startingStageId, setStartingStageId] = useState("");

  // A batch shares one starting stage. Only ACTIVE stages are offered; the backend verifies the
  // selected separate balances and posts their Ready Goods OUT movements atomically.

  const { data: stages } = useQuery({
    queryKey: ["production-stages", "active"],
    queryFn: () => ProductionStagesApi.list({ activeOnly: true })
  });

  const reprocessMutation = useMutation({
    mutationFn: (ids: string[]) => SeparatesApi.reprocessBatch({
      separateIds: ids,
      orderDate: new Date().toISOString().slice(0, 10),
      startingStageId: startingStageId || null
    }),
    onSuccess: () => {
      setSelectedSeparateIds([]);
      qc.invalidateQueries({ queryKey: ["separates"] });
      qc.invalidateQueries({ queryKey: ["production-orders"] });
    }
  });
  const scrapMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => SeparatesApi.scrap(vars.id, vars.reason),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["separates"] })
  });

  const selectedSeparates = (separates ?? []).filter((s) => selectedSeparateIds.includes(s.id));
  const toggleSeparate = (id: string, checked: boolean) => {
    if (!checked) {
      setSelectedSeparateIds((currentIds) => currentIds.filter((selectedId) => selectedId !== id));
      return;
    }
    const candidate = (separates ?? []).find((s) => s.id === id);
    if (!candidate) return;
    const current = (separates ?? []).filter((s) => selectedSeparateIds.includes(s.id));
    if (current.some((s) => s.customerId !== candidate.customerId || s.itemId !== candidate.itemId)) {
      setSelectedSeparateIds([id]);
      return;
    }
    setSelectedSeparateIds((currentIds) => currentIds.includes(id) ? currentIds : [...currentIds, id]);
  };

  return (
    <>
      <PageHeader
        title={t("nav.separates", "المنفصلات")}
        subtitle="المنفصلات ليست فاقدًا - يتم تتبعها بشكل مستقل داخل المخازن، وإعادة تشغيلها ينشئ أمر تشغيل جديد دون المساس بتاريخ الأمر الأصلي"
      />
      <div className="space-y-3">
        {selectedSeparateIds.length > 0 && (
          <Card className="p-4">
            <div className="flex flex-wrap items-center gap-3">
              <span className="text-sm font-medium">عدد المنفصلات المحددة: {selectedSeparateIds.length}</span>
              <Select aria-label={t("prod.startingStage", "مرحلة البداية")} value={startingStageId} onChange={(e) => setStartingStageId(e.target.value)} className="min-w-[12rem] max-w-full">
                <option value="">{t("prod.startingStageDefault", "التشكيل (الافتراضي)")}</option>
                {(stages ?? []).map((st) => <option key={st.id} value={st.id}>{st.code} - {st.name}</option>)}
              </Select>
              <Button variant="secondary" disabled={selectedSeparates.length === 0 || reprocessMutation.isPending} onClick={() => reprocessMutation.mutate(selectedSeparateIds)}>
                {reprocessMutation.isPending ? "جارٍ إنشاء الأمر..." : "إنشاء أمر إعادة تشغيل واحد"}
              </Button>
              <Button variant="ghost" onClick={() => setSelectedSeparateIds([])}>إلغاء التحديد</Button>
            </div>
            {reprocessMutation.isError && <p className="text-sm text-red-600 mt-2">تعذر إنشاء أمر إعادة التشغيل. تحقق من رصيد المنفصلات في مخزن الجاهز ثم أعد المحاولة.</p>}
          </Card>
        )}
        {isLoading && <Card className="card-pad text-center text-ink-subtle">جارٍ التحميل...</Card>}
        {!isLoading && separates?.length === 0 && <Card className="card-pad text-center text-ink-subtle">لا توجد منفصلات مسجلة بعد</Card>}
        {separates?.map((s) => (
          <Card key={s.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="text-sm text-ink-muted">من أمر <span className="ltr-nums font-medium">{s.originalOrderNumber}</span> · مرحلة {s.stageName}</span>
                <span className="text-ink-subtle mx-2">·</span>
                <span className="text-sm text-ink-muted">{s.itemCode} - {s.itemName}</span>
                <span className="text-ink-subtle mx-2">·</span>
                <span className="ltr-nums font-medium">
                  {s.quantityKg != null && `${s.quantityKg} كجم `}
                  {s.quantityMeter != null && `${s.quantityMeter} م`}
                </span>
              </div>
              <Badge tone={statusTone[s.status]}>{statusLabel[s.status]}</Badge>
            </div>

            {s.reprocessingOrderNumber && (
              <p className="text-xs text-ink-muted mt-1">أمر إعادة التشغيل: <span className="ltr-nums font-medium">{s.reprocessingOrderNumber}</span></p>
            )}

            {s.status === "PendingReprocessing" && (
              <div className="mt-3 flex flex-wrap items-center gap-2">
                {s.readyForReprocessing ? (
                  <label className="flex items-center gap-2 text-sm">
                    <input type="checkbox" checked={selectedSeparateIds.includes(s.id)} onChange={(e) => toggleSeparate(s.id, e.target.checked)} />
                    اختيار لأمر إعادة التشغيل
                  </label>
                ) : (
                  <span className="text-sm text-ink-muted">متاحة لإعادة التشغيل بعد اكتمال الأمر الأصلي وترحيل المنفصلات إلى مخزن الجاهز</span>
                )}
                <Input
                  placeholder="سبب الإتلاف..."
                  value={scrapReasonById[s.id] ?? ""}
                  onChange={(e) => setScrapReasonById((p) => ({ ...p, [s.id]: e.target.value }))}
                  className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                />
                <Button variant="ghost" disabled={!scrapReasonById[s.id] || scrapMutation.isPending} onClick={() => scrapMutation.mutate({ id: s.id, reason: scrapReasonById[s.id] })}>
                  إتلاف
                </Button>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
