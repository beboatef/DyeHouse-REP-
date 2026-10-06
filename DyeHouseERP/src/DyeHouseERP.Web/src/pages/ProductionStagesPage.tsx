import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionStagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

type Draft = {
  code: string; name: string; sequence: string;
  requiresInputQuantity: boolean; requiresOutputQuantity: boolean; requiresApproval: boolean;
  allowSkip: boolean; allowRepeat: boolean; allowRework: boolean; allowReturn: boolean; notes: string;
};

const emptyDraft = (): Draft => ({
  code: "", name: "", sequence: "",
  requiresInputQuantity: true, requiresOutputQuantity: true, requiresApproval: false,
  allowSkip: false, allowRepeat: false, allowRework: true, allowReturn: false, notes: ""
});

const flags: { key: keyof Draft; label: string }[] = [
  { key: "requiresInputQuantity", label: "يتطلب كمية مدخلة" },
  { key: "requiresOutputQuantity", label: "يتطلب كمية مخرجة" },
  { key: "requiresApproval", label: "يتطلب اعتماد" },
  { key: "allowSkip", label: "يسمح بالتخطي" },
  { key: "allowRepeat", label: "يسمح بالتكرار" },
  { key: "allowRework", label: "يسمح بإعادة المعالجة" },
  { key: "allowReturn", label: "يسمح بالإرجاع" }
];

export default function ProductionStagesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [draft, setDraft] = useState<Draft>(emptyDraft());
  const [error, setError] = useState<string | null>(null);

  /**
   * Setting a route marker MOVES it off whichever stage held it before, so the
   * formation stage and the ready-goods stage are each unique. The API rejects a
   * stage that would be both, which is why the button is disabled there.
   */
  const markerMutation = useMutation({
    mutationFn: ({ id, kind }: { id: string; kind: "formation" | "readyGoods" }) =>
      kind === "formation" ? ProductionStagesApi.setFormationStage(id) : ProductionStagesApi.setReadyGoodsStage(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["production-stages"] }),
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "حدث خطأ")
  });

  const { data: stages, isLoading } = useQuery({ queryKey: ["production-stages"], queryFn: () => ProductionStagesApi.list() });

  const createMutation = useMutation({
    mutationFn: () =>
      ProductionStagesApi.create({
        code: draft.code, name: draft.name, sequence: Number(draft.sequence),
        requiresInputQuantity: draft.requiresInputQuantity, requiresOutputQuantity: draft.requiresOutputQuantity,
        requiresApproval: draft.requiresApproval, allowSkip: draft.allowSkip, allowRepeat: draft.allowRepeat,
        allowRework: draft.allowRework, allowReturn: draft.allowReturn, notes: draft.notes || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["production-stages"] });
      setShowForm(false);
      setDraft(emptyDraft());
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  return (
    <>
      <PageHeader
        title="مراحل التشغيل"
        subtitle="لا توجد أسماء مراحل ثابتة في النظام - كل مرحلة يتم تعريفها هنا، بالترتيب الذي تحدده، وتُطبَّق تلقائيًا على كل أمر تشغيل جديد"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مرحلة جديدة"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="field-label">كود المرحلة</label>
                <Input value={draft.code} onChange={(e) => setDraft((d) => ({ ...d, code: e.target.value }))} required maxLength={20} />
              </div>
              <div>
                <label className="field-label">اسم المرحلة</label>
                <Input value={draft.name} onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} required maxLength={200} />
              </div>
              <div>
                <label className="field-label">الترتيب</label>
                <Input type="number" value={draft.sequence} onChange={(e) => setDraft((d) => ({ ...d, sequence: e.target.value }))} required min={1} />
              </div>
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              {flags.map((f) => (
                <label key={f.key} className="flex items-center gap-2 text-sm text-gray-700">
                  <input
                    type="checkbox"
                    checked={draft[f.key] as boolean}
                    onChange={(e) => setDraft((d) => ({ ...d, [f.key]: e.target.checked }))}
                  />
                  {f.label}
                </label>
              ))}
            </div>

            <div>
              <label className="field-label">ملاحظات</label>
              <Input value={draft.notes} onChange={(e) => setDraft((d) => ({ ...d, notes: e.target.value }))} />
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
            {error && <p className="form-error">{error}</p>}
          </form>
        </Card>
      )}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>الترتيب</th>
              <th>الكود</th>
              <th>الاسم</th>
              <th>الخصائص</th>
              <th>الحالة</th>
              <th>دور المرحلة</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && stages?.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لم يتم تعريف أي مراحل بعد</td></tr>
            )}
            {stages?.map((s) => (
              <tr key={s.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="ltr-nums">{s.sequence}</td>
                <td className="font-medium ltr-nums">{s.code}</td>
                <td>{s.name}</td>
                <td className="space-x-1 space-x-reverse">
                  {s.requiresApproval && <Badge tone="yellow">اعتماد</Badge>}
                  {s.allowSkip && <Badge tone="blue">قابل للتخطي</Badge>}
                  {s.allowRework && <Badge tone="gray">إعادة معالجة</Badge>}
                </td>
                <td>
                  <Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? "نشطة" : "غير نشطة"}</Badge>
                </td>
                {/* The two route markers are explicit buttons, never derived from
                    the sequence column - spec sections 12 and 17. */}
                <td>
                  <div className="flex flex-wrap gap-1">
                    {s.isFormationStage ? (
                      <Badge tone="blue">{t("stg.isFormation")}</Badge>
                    ) : (
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={!s.isActive || markerMutation.isPending}
                        onClick={() => markerMutation.mutate({ id: s.id, kind: "formation" })}
                      >
                        {t("stg.setFormation")}
                      </Button>
                    )}
                    {s.isReadyGoodsStage ? (
                      <Badge tone="green">{t("stg.isReadyGoods")}</Badge>
                    ) : (
                      <Button
                        size="sm"
                        variant="ghost"
                        // The formation stage can never also be the final stage, so
                        // the button is disabled there rather than failing on click.
                        disabled={!s.isActive || s.isFormationStage || markerMutation.isPending}
                        title={s.isFormationStage ? t("stg.markerHint") : undefined}
                        onClick={() => markerMutation.mutate({ id: s.id, kind: "readyGoods" })}
                      >
                        {t("stg.setReadyGoods")}
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
