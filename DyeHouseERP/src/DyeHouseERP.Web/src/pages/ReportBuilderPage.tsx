import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ReportBuilderApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select } from "@/components/ui";

export default function ReportBuilderPage() {
  const [entityKey, setEntityKey] = useState("");
  const [columns, setColumns] = useState<string[]>([]);
  const [templateNameAr, setTemplateNameAr] = useState("");
  const [templateNameEn, setTemplateNameEn] = useState("");

  const { data: entities } = useQuery({ queryKey: ["reportable-entities"], queryFn: () => ReportBuilderApi.entities() });
  const { data: templates, refetch: refetchTemplates } = useQuery({ queryKey: ["report-templates"], queryFn: () => ReportBuilderApi.templates() });

  const runMutation = useMutation({ mutationFn: () => ReportBuilderApi.run({ entityKey, columns }) });
  const saveMutation = useMutation({
    mutationFn: () => ReportBuilderApi.saveTemplate({ nameAr: templateNameAr, nameEn: templateNameEn, entityKey, columns }),
    onSuccess: () => { refetchTemplates(); setTemplateNameAr(""); setTemplateNameEn(""); }
  });

  const currentEntity = entities?.find((e) => e.entityKey === entityKey);
  const toggleColumn = (c: string) => setColumns((prev) => (prev.includes(c) ? prev.filter((x) => x !== c) : [...prev, c]));

  const loadTemplate = (t: NonNullable<typeof templates>[number]) => {
    setEntityKey(t.entityKey);
    setColumns(t.columns);
  };

  return (
    <>
      <PageHeader title="منشئ التقارير المخصص" subtitle="اختر كيانًا وأعمدته المسموح بها، شغّل التقرير، احفظه كقالب، أو صدّره PDF/Excel" />

      {templates && templates.length > 0 && (
        <Card className="p-4 mb-4">
          <div className="text-xs text-gray-500 mb-2">قوالب محفوظة</div>
          <div className="flex flex-wrap gap-2">
            {templates.map((t) => (
              <button key={t.id} onClick={() => loadTemplate(t)} className="text-xs px-3 py-1.5 rounded-full bg-gray-100 hover:bg-gray-200">
                {t.nameAr}
              </button>
            ))}
          </div>
        </Card>
      )}

      <Card className="p-5 mb-6">
        <div className="mb-4">
          <label className="block text-xs font-medium text-gray-600 mb-1">الكيان</label>
          <Select value={entityKey} onChange={(e) => { setEntityKey(e.target.value); setColumns([]); }}>
            <option value="">اختر...</option>
            {entities?.map((en) => <option key={en.entityKey} value={en.entityKey}>{en.label}</option>)}
          </Select>
        </div>

        {currentEntity && (
          <div className="mb-4">
            <label className="block text-xs font-medium text-gray-600 mb-2">الأعمدة</label>
            <div className="flex flex-wrap gap-3">
              {currentEntity.columns.map((c) => (
                <label key={c} className="flex items-center gap-1.5 text-sm">
                  <input type="checkbox" checked={columns.includes(c)} onChange={() => toggleColumn(c)} />
                  {c}
                </label>
              ))}
            </div>
          </div>
        )}

        <div className="flex flex-wrap items-center gap-2">
          <Button disabled={!entityKey || columns.length === 0 || runMutation.isPending} onClick={() => runMutation.mutate()}>
            {runMutation.isPending ? "جارٍ التشغيل..." : "تشغيل التقرير"}
          </Button>
          <Button variant="secondary" disabled={!entityKey || columns.length === 0} onClick={() => ReportBuilderApi.runPdf({ entityKey, columns })}>تصدير PDF</Button>
          <Button variant="secondary" disabled={!entityKey || columns.length === 0} onClick={() => ReportBuilderApi.runExcel({ entityKey, columns })}>تصدير Excel</Button>
        </div>

        <div className="flex items-end gap-2 mt-4 pt-4 border-t border-gray-100">
          <div><label className="block text-[11px] text-gray-500 mb-1">اسم القالب (عربي)</label><Input value={templateNameAr} onChange={(e) => setTemplateNameAr(e.target.value)} className="w-40" /></div>
          <div><label className="block text-[11px] text-gray-500 mb-1">اسم القالب (إنجليزي)</label><Input value={templateNameEn} onChange={(e) => setTemplateNameEn(e.target.value)} className="w-40" dir="ltr" /></div>
          <Button variant="ghost" disabled={!entityKey || columns.length === 0 || !templateNameAr || !templateNameEn} onClick={() => saveMutation.mutate()}>حفظ كقالب</Button>
        </div>
      </Card>

      {runMutation.data && (
        <Card>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-gray-500 text-xs">
                {runMutation.data.headers.map((h) => <th key={h} className="text-start px-4 py-3 font-medium">{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {runMutation.data.rows.length === 0 && (
                <tr><td colSpan={runMutation.data.headers.length} className="px-4 py-6 text-center text-gray-400">لا توجد نتائج</td></tr>
              )}
              {runMutation.data.rows.map((row, i) => (
                <tr key={i} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                  {row.map((cell, j) => <td key={j} className="px-4 py-3 ltr-nums">{cell ?? "-"}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </>
  );
}
