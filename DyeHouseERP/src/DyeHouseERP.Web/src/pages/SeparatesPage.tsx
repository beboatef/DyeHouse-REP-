import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SeparatesApi, SeparateStatus } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";

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
  const qc = useQueryClient();
  const { data: separates, isLoading } = useQuery({ queryKey: ["separates"], queryFn: () => SeparatesApi.list() });
  const [scrapReasonById, setScrapReasonById] = useState<Record<string, string>>({});

  const reprocessMutation = useMutation({
    mutationFn: (id: string) => SeparatesApi.reprocess(id, { orderDate: new Date().toISOString().slice(0, 10) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["separates"] })
  });
  const scrapMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => SeparatesApi.scrap(vars.id, vars.reason),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["separates"] })
  });

  return (
    <>
      <PageHeader
        title="المنفصلات وإعادة التشغيل"
        subtitle="المنفصلات ليست فاقدًا - يتم تتبعها بشكل مستقل، وإعادة تشغيلها ينشئ أمر تشغيل جديد دون المساس بتاريخ الأمر الأصلي"
      />
      <div className="space-y-3">
        {isLoading && <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>}
        {!isLoading && separates?.length === 0 && <Card className="p-6 text-center text-gray-400">لا توجد منفصلات مسجلة بعد</Card>}
        {separates?.map((s) => (
          <Card key={s.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <span className="text-sm text-gray-600">من أمر <span className="ltr-nums font-medium">{s.originalOrderNumber}</span> · مرحلة {s.stageName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{s.itemCode} - {s.itemName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="ltr-nums font-medium">
                  {s.quantityKg != null && `${s.quantityKg} كجم `}
                  {s.quantityMeter != null && `${s.quantityMeter} م`}
                </span>
              </div>
              <Badge tone={statusTone[s.status]}>{statusLabel[s.status]}</Badge>
            </div>

            {s.reprocessingOrderNumber && (
              <p className="text-xs text-gray-500 mt-1">أمر إعادة التشغيل: <span className="ltr-nums font-medium">{s.reprocessingOrderNumber}</span></p>
            )}

            {s.status === "PendingReprocessing" && (
              <div className="flex gap-2 mt-3 items-center">
                <Button variant="secondary" onClick={() => reprocessMutation.mutate(s.id)}>إنشاء أمر إعادة تشغيل</Button>
                <Input
                  placeholder="سبب الإتلاف..."
                  value={scrapReasonById[s.id] ?? ""}
                  onChange={(e) => setScrapReasonById((p) => ({ ...p, [s.id]: e.target.value }))}
                  className="max-w-xs"
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
