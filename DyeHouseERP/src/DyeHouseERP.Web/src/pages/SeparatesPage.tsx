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

  // The stage the reprocessing Job Order actually begins at. Only ACTIVE stages are offered; empty
  // keeps the ordinary formation start. Reprocessed material is already past formation, so starting
  // later is the normal case - and it never posts stock (the raw issue is the single deduction).
  const [startStageById, setStartStageById] = useState<Record<string, string>>({});
  const { data: stages } = useQuery({
    queryKey: ["production-stages", "active"],
    queryFn: () => ProductionStagesApi.list({ activeOnly: true })
  });

  const reprocessMutation = useMutation({
    mutationFn: (id: string) => SeparatesApi.reprocess(id, {
      orderDate: new Date().toISOString().slice(0, 10),
      startingStageId: startStageById[id] || null
    }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["separates"] })
  });
  const scrapMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => SeparatesApi.scrap(vars.id, vars.reason),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["separates"] })
  });

  return (
    <>
      <PageHeader
        title={t("nav.separates", "المنفصلات")}
        subtitle="المنفصلات ليست فاقدًا - يتم تتبعها بشكل مستقل داخل المخازن، وإعادة تشغيلها ينشئ أمر تشغيل جديد دون المساس بتاريخ الأمر الأصلي"
      />
      <div className="space-y-3">
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
                {/* Optional starting stage for the new Job Order (spec section 23). */}
                <Select
                  aria-label={t("prod.startingStage", "مرحلة البداية")}
                  value={startStageById[s.id] ?? ""}
                  onChange={(e) => setStartStageById((p) => ({ ...p, [s.id]: e.target.value }))}
                  className="min-w-[12rem] max-w-full"
                >
                  <option value="">{t("prod.startingStageDefault", "التشكيل (الافتراضي)")}</option>
                  {(stages ?? []).map((st) => (
                    <option key={st.id} value={st.id}>{st.code} - {st.name}</option>
                  ))}
                </Select>
                <Button variant="secondary" onClick={() => reprocessMutation.mutate(s.id)}>إنشاء أمر إعادة تشغيل</Button>
                <Input
                  placeholder="سبب الإتلاف..."
                  value={scrapReasonById[s.id] ?? ""}
                  onChange={(e) => setScrapReasonById((p) => ({ ...p, [s.id]: e.target.value }))}
                  className="min-w-[12rem] max-w-full flex-1 sm:max-w-xs"
                />
                <Button
                  variant="ghost"
                  disabled={!scrapReasonById[s.id]}
                  onClick={() => scrapMutation.mutate({ id: s.id, reason: scrapReasonById[s.id] })}
                >
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
