import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PurchaseUnitsApi, type PurchaseUnit, type PurchaseUnitInput } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Field, Badge } from "@/components/ui";
import { TableStateRow, errorMessage } from "@/components/ui";
import { useI18n } from "@/i18n";
import { Ruler, Plus } from "lucide-react";

/**
 * Purchase units of measure (spec section 44).
 *
 * Units are data, not a fixed list: the nine defaults ship seeded, and users add
 * their own bilingual units. Two rules the screen has to make obvious, because
 * getting them wrong is what the spec is guarding against:
 *   * There is NO delete. A unit that a purchase document already references must
 *     stay resolvable, so the lifecycle is deactivate/reactivate.
 *   * There is NO automatic conversion. A conversion exists only when the user
 *     explicitly supplies both a factor and the unit it converts into.
 */

interface FormState {
  id: string | null;
  code: string;
  nameAr: string;
  nameEn: string;
  conversionFactor: string;
  baseUnitId: string;
  notes: string;
}

const emptyForm: FormState = {
  id: null, code: "", nameAr: "", nameEn: "", conversionFactor: "", baseUnitId: "", notes: ""
};

export default function PurchaseUnitsPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();

  const [search, setSearch] = useState("");
  const [activeOnly, setActiveOnly] = useState(false);
  const [form, setForm] = useState<FormState | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const { data: units, isLoading, error } = useQuery({
    queryKey: ["purchase-units", activeOnly, search],
    queryFn: () => PurchaseUnitsApi.list({ activeOnly: activeOnly || undefined, search: search || undefined })
  });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["purchase-units"] });

  const saveMutation = useMutation({
    mutationFn: (state: FormState) => {
      const body: PurchaseUnitInput = {
        nameAr: state.nameAr.trim(),
        nameEn: state.nameEn.trim(),
        // Both halves of a conversion travel together, or neither does.
        conversionFactor: state.conversionFactor ? Number(state.conversionFactor) : null,
        baseUnitId: state.conversionFactor ? state.baseUnitId || null : null,
        notes: state.notes.trim() || null
      };
      return state.id
        ? PurchaseUnitsApi.update(state.id, body)
        : PurchaseUnitsApi.create({ ...body, code: state.code.trim() });
    },
    onSuccess: () => {
      invalidate();
      setForm(null);
      setFormError(null);
    },
    onError: (err) => setFormError(errorMessage(err, t("common.error")))
  });

  const toggleMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      PurchaseUnitsApi.setActive(id, isActive),
    onSuccess: invalidate
  });

  /** Candidate conversion targets: any unit except the one being edited. */
  const baseUnitOptions = useMemo(
    () => (units ?? []).filter((u) => u.id !== form?.id),
    [units, form?.id]
  );

  const label = (unit: PurchaseUnit) => pick(unit.nameAr, unit.nameEn, unit.code);

  const conversionText = (unit: PurchaseUnit) => {
    if (unit.conversionFactor == null || !unit.baseUnitId) return t("units.noConversion");
    const target = (units ?? []).find((u) => u.id === unit.baseUnitId);
    const targetLabel = target ? label(target) : (unit.baseUnitName ?? "");
    return `1 ${label(unit)} = ${unit.conversionFactor} ${targetLabel}`;
  };

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!form) return;
    setFormError(null);

    if (!form.nameAr.trim() || !form.nameEn.trim() || (!form.id && !form.code.trim())) {
      setFormError(t("units.requiredFields"));
      return;
    }
    // Mirrors the server rule rather than replacing it - the API still enforces it.
    if (form.conversionFactor && !form.baseUnitId) {
      setFormError(t("units.conversionNeedsTarget"));
      return;
    }
    saveMutation.mutate(form);
  };

  return (
    <>
      <PageHeader
        title={t("units.title")}
        subtitle={t("units.subtitle")}
        action={
          <Button
            onClick={() => {
              setFormError(null);
              setForm(form ? null : { ...emptyForm });
            }}
          >
            <Plus size={16} />
            {form ? t("common.cancel") : t("units.new")}
          </Button>
        }
      />

      {form && (
        <Card className="mb-6 p-5">
          <form onSubmit={submit} className="grid grid-cols-1 items-end gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Field label={t("common.code")} required>
              <Input
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                maxLength={30}
                required
                disabled={!!form.id}
                placeholder="CARTON"
              />
              {form.id && <p className="kpi-footnote">{t("units.codeImmutable")}</p>}
            </Field>

            <Field label={t("units.nameAr")} required>
              <Input
                value={form.nameAr}
                onChange={(e) => setForm({ ...form, nameAr: e.target.value })}
                maxLength={100}
                required
              />
            </Field>

            <Field label={t("units.nameEn")} required>
              <Input
                value={form.nameEn}
                onChange={(e) => setForm({ ...form, nameEn: e.target.value })}
                maxLength={100}
                required
              />
            </Field>

            <Field label={t("units.conversionFactor")}>
              <Input
                type="number"
                min="0"
                step="any"
                value={form.conversionFactor}
                onChange={(e) => setForm({ ...form, conversionFactor: e.target.value })}
                placeholder="20"
              />
            </Field>

            <Field label={t("units.baseUnit")}>
              <Select
                value={form.baseUnitId}
                onChange={(e) => setForm({ ...form, baseUnitId: e.target.value })}
                disabled={!form.conversionFactor}
              >
                <option value="">{t("units.selectUnit")}</option>
                {baseUnitOptions.map((u) => (
                  <option key={u.id} value={u.id}>{label(u)}</option>
                ))}
              </Select>
            </Field>

            <Field label={t("common.notes")}>
              <Input
                value={form.notes}
                onChange={(e) => setForm({ ...form, notes: e.target.value })}
                maxLength={1000}
              />
            </Field>

            <div className="flex items-center gap-3 sm:col-span-2 lg:col-span-3">
              <Button type="submit" disabled={saveMutation.isPending}>
                {saveMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              <Button
                variant="secondary"
                onClick={() => {
                  setForm(null);
                  setFormError(null);
                }}
              >
                {t("common.cancel")}
              </Button>
              <p className="kpi-footnote">{t("units.conversionHint")}</p>
            </div>
          </form>

          {formError && <p className="mt-3 text-sm text-danger">{formError}</p>}
        </Card>
      )}

      {/* ---- Filters ---- */}
      <Card className="mb-4 p-4 sm:p-5">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label={t("app.search")}>
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("units.searchPlaceholder")}
            />
          </Field>
          <Field label={t("common.status")}>
            <Select value={activeOnly ? "active" : "all"} onChange={(e) => setActiveOnly(e.target.value === "active")}>
              <option value="all">{t("common.all")}</option>
              <option value="active">{t("common.active")}</option>
            </Select>
          </Field>
        </div>
      </Card>

      <Card>
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>{t("common.code")}</th>
                <th>{t("common.name")}</th>
                <th>{t("units.conversion")}</th>
                <th>{t("common.status")}</th>
                <th className="text-end">{t("common.actions")}</th>
              </tr>
            </thead>
            <tbody>
              <TableStateRow
                colSpan={5}
                loading={isLoading}
                error={error}
                isEmpty={units?.length === 0}
                loadingText={t("common.loading")}
                emptyText={t("units.empty")}
              />
              {units?.map((unit) => (
                <tr key={unit.id}>
                  <td className="ltr-nums font-medium">{unit.code}</td>
                  <td>
                    <div className="flex items-center gap-2">
                      <span>{label(unit)}</span>
                      {unit.isSystemDefault && (
                        <Badge tone="neutral">{t("units.systemDefault")}</Badge>
                      )}
                    </div>
                    <div className="text-2xs text-ink-subtle">
                      {pick(unit.nameEn, unit.nameAr)}
                    </div>
                  </td>
                  <td className={unit.conversionFactor == null ? "text-ink-subtle" : ""}>
                    {conversionText(unit)}
                  </td>
                  <td>
                    <Badge tone={unit.isActive ? "success" : "neutral"}>
                      {unit.isActive ? t("common.active") : t("common.inactive")}
                    </Badge>
                  </td>
                  <td className="text-end">
                    <div className="flex justify-end gap-1">
                      <button type="button" className="btn-link" onClick={() => {
                        setFormError(null);
                        setForm({
                          id: unit.id,
                          code: unit.code,
                          nameAr: unit.nameAr,
                          nameEn: unit.nameEn,
                          conversionFactor: unit.conversionFactor != null ? String(unit.conversionFactor) : "",
                          baseUnitId: unit.baseUnitId ?? "",
                          notes: unit.notes ?? ""
                        });
                      }}>
                        {t("common.edit")}
                      </button>
                      <button
                        type="button"
                        className="btn-link"
                        disabled={toggleMutation.isPending}
                        onClick={() => toggleMutation.mutate({ id: unit.id, isActive: !unit.isActive })}
                      >
                        {unit.isActive ? t("units.deactivate") : t("units.activate")}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {toggleMutation.isError && (
          <p className="px-4 py-3 text-sm text-danger">
            {errorMessage(toggleMutation.error, t("common.error"))}
          </p>
        )}

        <div className="flex items-center gap-2 border-t border-line px-4 py-3 text-2xs text-ink-subtle">
          <Ruler size={14} />
          {t("units.footerNote")}
        </div>
      </Card>
    </>
  );
}
