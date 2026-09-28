import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormationSpecTemplate, FormationSpecificationsApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Reusable specification CELLS (spec section 29).
 *
 * The user saves a specification once - width, meter-per-kg or g/m², tub/tube
 * format, winding tape format and the four instruction blocks - and then simply
 * selects it on a new formation request instead of retyping it.
 *
 * Editing or deactivating a cell here never rewrites a request that already used
 * it: each request group keeps its own copy of the values taken at selection time.
 */

const emptyForm = {
  code: "",
  nameAr: "",
  nameEn: "",
  widthCm: "",
  metersPerKg: "",
  gsm: "",
  tubFormat: "",
  windingTapeFormat: "",
  notes: "",
  qualityInstructions: "",
  labInstructions: "",
  internalInstructions: "",
  customerInstructions: ""
};

const toNumber = (value: string) => (value.trim() === "" ? null : Number(value));

export default function FormationSpecificationsPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const { data: templates, isLoading } = useQuery({
    queryKey: ["formation-specifications", search],
    queryFn: () => FormationSpecificationsApi.list({ search: search || undefined })
  });

  const reset = () => {
    setForm(emptyForm);
    setEditingId(null);
    setShowForm(false);
    setError(null);
  };

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        code: form.code,
        nameAr: form.nameAr,
        nameEn: form.nameEn,
        widthCm: toNumber(form.widthCm),
        metersPerKg: toNumber(form.metersPerKg),
        gsm: toNumber(form.gsm),
        tubFormat: form.tubFormat || null,
        windingTapeFormat: form.windingTapeFormat || null,
        notes: form.notes || null,
        qualityInstructions: form.qualityInstructions || null,
        labInstructions: form.labInstructions || null,
        internalInstructions: form.internalInstructions || null,
        customerInstructions: form.customerInstructions || null
      };

      return editingId
        ? FormationSpecificationsApi.update(editingId, payload)
        : FormationSpecificationsApi.create(payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["formation-specifications"] });
      reset();
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const toggleMutation = useMutation({
    mutationFn: (vars: { id: string; isActive: boolean }) =>
      FormationSpecificationsApi.setActive(vars.id, vars.isActive),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["formation-specifications"] })
  });

  const startEdit = (template: FormationSpecTemplate) => {
    setEditingId(template.id);
    setShowForm(true);
    setError(null);
    setForm({
      code: template.code,
      nameAr: template.nameAr,
      nameEn: template.nameEn,
      widthCm: template.widthCm?.toString() ?? "",
      metersPerKg: template.metersPerKg?.toString() ?? "",
      gsm: template.gsm?.toString() ?? "",
      tubFormat: template.tubFormat ?? "",
      windingTapeFormat: template.windingTapeFormat ?? "",
      notes: template.notes ?? "",
      qualityInstructions: template.qualityInstructions ?? "",
      labInstructions: template.labInstructions ?? "",
      internalInstructions: template.internalInstructions ?? "",
      customerInstructions: template.customerInstructions ?? ""
    });
  };

  const field = (key: keyof typeof emptyForm, label: string, type = "text") => (
    <div>
      <label className="block text-xs font-medium text-gray-600 mb-1">{label}</label>
      <Input type={type} value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
    </div>
  );

  const textarea = (key: keyof typeof emptyForm, label: string) => (
    <div>
      <label className="block text-xs font-medium text-gray-600 mb-1">{label}</label>
      <textarea
        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
        rows={2}
        value={form[key]}
        onChange={(e) => setForm({ ...form, [key]: e.target.value })}
      />
    </div>
  );

  return (
    <>
      <PageHeader
        title={t("fr.specTitle")}
        subtitle={t("fr.specSubtitle")}
        action={
          <Button
            onClick={() => {
              if (showForm) reset();
              else setShowForm(true);
            }}
          >
            {showForm ? t("common.cancel") : t("fr.specNew")}
          </Button>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
            {field("code", t("common.code"))}
            {field("nameAr", t("common.nameAr"))}
            {field("nameEn", t("common.nameEn"))}
            {field("widthCm", t("fr.width"), "number")}
            {field("metersPerKg", t("fr.metersPerKg"), "number")}
            {field("gsm", t("fr.gsm"), "number")}
            {field("tubFormat", t("fr.tubFormat"))}
            {field("windingTapeFormat", t("fr.windingTapeFormat"))}
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mt-4">
            {textarea("qualityInstructions", t("fr.qualityInstructions"))}
            {textarea("labInstructions", t("fr.labInstructions"))}
            {textarea("internalInstructions", t("fr.internalInstructions"))}
            {textarea("customerInstructions", t("fr.customerInstructions"))}
            {textarea("notes", t("common.notes"))}
          </div>
          <div className="mt-4 flex items-center gap-3">
            <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending || !form.code || (!form.nameAr && !form.nameEn)}>
              {saveMutation.isPending ? t("common.saving") : editingId ? t("common.save") : t("common.add")}
            </Button>
            {error && <span className="text-sm text-red-600">{error}</span>}
          </div>
        </Card>
      )}

      <Card className="p-4 mb-4">
        <Input placeholder={t("common.searchPlaceholder")} value={search} onChange={(e) => setSearch(e.target.value)} />
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("common.code")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.name")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.width")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.metersPerKg")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.gsm")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.tubFormat")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {!isLoading && templates?.length === 0 && (
              <tr><td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {templates?.map((template) => (
              <tr key={template.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{template.code}</td>
                <td className="px-4 py-3">{pick(template.nameAr, template.nameEn)}</td>
                <td className="px-4 py-3 ltr-nums">{template.widthCm ?? "—"}</td>
                <td className="px-4 py-3 ltr-nums">{template.metersPerKg ?? "—"}</td>
                <td className="px-4 py-3 ltr-nums">{template.gsm ?? "—"}</td>
                <td className="px-4 py-3">{template.tubFormat ?? "—"}</td>
                <td className="px-4 py-3">
                  <Badge tone={template.isActive ? "green" : "gray"}>
                    {template.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                </td>
                <td className="px-4 py-3 space-x-3 whitespace-nowrap">
                  <button className="btn-link" onClick={() => startEdit(template)}>
                    {t("common.edit")}
                  </button>
                  <button
                    className="text-slate-500 hover:underline text-xs font-semibold"
                    onClick={() => toggleMutation.mutate({ id: template.id, isActive: !template.isActive })}
                  >
                    {template.isActive ? t("common.inactive") : t("common.active")}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
