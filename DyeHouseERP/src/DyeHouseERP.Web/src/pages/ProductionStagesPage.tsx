import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ProductionStagesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

type Draft = {
  code: string; name: string; sequence: string;
  requiresInputQuantity: boolean; requiresOutputQuantity: boolean;
  notes: string;
};

const emptyDraft = (): Draft => ({
  code: "", name: "", sequence: "",
  requiresInputQuantity: true, requiresOutputQuantity: true,
  notes: ""
});

/**
 * The capability switches that actually exist. `key` is the domain field sent to the API; the label
 * is an i18n key, translated both in the create form and in the list column.
 *
 * Phase 4 removed the last legacy capability (`allowSkip`): the skip workflow was deleted with its
 * endpoint and its `AllowSkip` column, so the switch and its "skippable" badge were removed here too
 * rather than left advertising behaviour no code path honours.
 */
const flags: { key: keyof Draft; labelKey: string }[] = [
  { key: "requiresInputQuantity", labelKey: "stg.flag.requiresInputQuantity" },
  { key: "requiresOutputQuantity", labelKey: "stg.flag.requiresOutputQuantity" }
];

type EditDraft = { name: string; sequence: string; notes: string };

export default function ProductionStagesPage() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [draft, setDraft] = useState<Draft>(emptyDraft());
  const [error, setError] = useState<string | null>(null);

  /** Free-text filter, applied by the API next to the activity filter. */
  const [search, setSearch] = useState("");
  /** The row currently being edited in place (name / sequence / notes), if any. */
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState<EditDraft>({ name: "", sequence: "", notes: "" });

  /** Every write funnels through here so one invalidation covers list, create, edit and toggle. */
  const invalidate = () => qc.invalidateQueries({ queryKey: ["production-stages"] });

  const apiError = (err: ApiErrorLike) =>
    err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error");

  /**
   * Setting a route marker MOVES it off whichever stage held it before, so the
   * formation stage and the ready-goods stage are each unique. The API rejects a
   * stage that would be both, which is why the button is disabled there.
   */
  const markerMutation = useMutation({
    mutationFn: ({ id, kind }: { id: string; kind: "formation" | "readyGoods" }) =>
      kind === "formation" ? ProductionStagesApi.setFormationStage(id) : ProductionStagesApi.setReadyGoodsStage(id),
    onSuccess: () => { invalidate(); setError(null); },
    onError: (err: ApiErrorLike) => setError(apiError(err))
  });

  const { data: stages, isLoading, isError } = useQuery({
    queryKey: ["production-stages", search],
    queryFn: () => ProductionStagesApi.list({ search: search.trim() || undefined })
  });

  const createMutation = useMutation({
    mutationFn: () =>
      ProductionStagesApi.create({
        code: draft.code, name: draft.name, sequence: Number(draft.sequence),
        requiresInputQuantity: draft.requiresInputQuantity, requiresOutputQuantity: draft.requiresOutputQuantity,
        notes: draft.notes || undefined
      }),
    onSuccess: () => {
      invalidate();
      setShowForm(false);
      setDraft(emptyDraft());
      setError(null);
    },
    onError: (err: ApiErrorLike) => setError(apiError(err))
  });

  const updateMutation = useMutation({
    mutationFn: (id: string) =>
      ProductionStagesApi.update(id, {
        name: editDraft.name,
        sequence: Number(editDraft.sequence),
        notes: editDraft.notes.trim() || undefined
      }),
    onSuccess: () => {
      invalidate();
      setEditingId(null);
      setEditDraft({ name: "", sequence: "", notes: "" });
      setError(null);
    },
    onError: (err: ApiErrorLike) => setError(apiError(err))
  });

  /**
   * Deactivate / reactivate one stage. Deactivating never deletes history: the executions
   * that already reference this stage keep their id and stay readable, the stage simply
   * stops being offered for new Job Orders and new transfers.
   */
  const activeMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      ProductionStagesApi.setActive(id, isActive),
    onSuccess: () => { invalidate(); setError(null); },
    onError: (err: ApiErrorLike) => setError(apiError(err))
  });

  const startEdit = (id: string, name: string, sequence: number, notes: string | null) => {
    setEditingId(id);
    setEditDraft({ name, sequence: String(sequence), notes: notes ?? "" });
    setError(null);
  };

  return (
    <>
      <PageHeader
        title={t("nav.productionStages")}
        subtitle={t("stg.subtitle")}
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("stg.new")}</Button>}
      />

      <div className="flex flex-wrap items-center gap-3 mb-4">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t("common.searchPlaceholder")}
          aria-label={t("common.search")}
          className="w-full sm:w-72"
        />
        <span className="text-xs text-gray-500">
          {t("common.results")}: {stages?.length ?? 0}
        </span>
      </div>

      {error && <p className="form-error mb-4" role="alert">{error}</p>}

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
                <label className="field-label">{t("stg.code")}</label>
                <Input value={draft.code} onChange={(e) => setDraft((d) => ({ ...d, code: e.target.value }))} required maxLength={20} />
              </div>
              <div>
                <label className="field-label">{t("stg.name")}</label>
                <Input value={draft.name} onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} required maxLength={200} />
              </div>
              <div>
                <label className="field-label">{t("stg.sequence")}</label>
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
                  {t(f.labelKey)}
                </label>
              ))}
            </div>

            <div>
              <label className="field-label">{t("common.notes")}</label>
              <Input value={draft.notes} onChange={(e) => setDraft((d) => ({ ...d, notes: e.target.value }))} />
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>{t("stg.sequence")}</th>
              <th>{t("common.code")}</th>
              <th>{t("common.name")}</th>
              <th>{t("stg.flags")}</th>
              <th>{t("common.status")}</th>
              <th>{t("stg.role")}</th>
              <th>{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && <tr><td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>}
            {isError && <tr><td colSpan={7} className="px-4 py-6 text-center text-red-500">{t("common.error")}</td></tr>}
            {!isLoading && !isError && stages?.length === 0 && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {stages?.map((s) => (
              <tr key={s.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="ltr-nums">
                  {editingId === s.id ? (
                    <Input
                      type="number"
                      min={1}
                      value={editDraft.sequence}
                      onChange={(e) => setEditDraft((d) => ({ ...d, sequence: e.target.value }))}
                      className="w-20"
                      aria-label={t("stg.sequence")}
                    />
                  ) : (
                    s.sequence
                  )}
                </td>
                <td className="font-medium ltr-nums">{s.code}</td>
                <td>
                  {editingId === s.id ? (
                    <div className="space-y-2 min-w-[200px]">
                      <Input
                        value={editDraft.name}
                        onChange={(e) => setEditDraft((d) => ({ ...d, name: e.target.value }))}
                        maxLength={200}
                        aria-label={t("common.name")}
                      />
                      <Input
                        value={editDraft.notes}
                        onChange={(e) => setEditDraft((d) => ({ ...d, notes: e.target.value }))}
                        placeholder={t("common.notes")}
                        aria-label={t("common.notes")}
                      />
                    </div>
                  ) : (
                    s.name
                  )}
                </td>
                <td className="space-x-1 space-x-reverse">
                  {s.requiresInputQuantity && <Badge tone="blue">{t("stg.flag.requiresInputQuantity")}</Badge>}
                  {s.requiresOutputQuantity && <Badge tone="blue">{t("stg.flag.requiresOutputQuantity")}</Badge>}
                </td>
                <td>
                  <Badge tone={s.isActive ? "green" : "gray"}>
                    {s.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
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
                <td>
                  {editingId === s.id ? (
                    <div className="flex flex-wrap gap-1">
                      <Button
                        size="sm"
                        disabled={updateMutation.isPending || !editDraft.name.trim() || !editDraft.sequence}
                        onClick={() => updateMutation.mutate(s.id)}
                      >
                        {updateMutation.isPending ? t("common.saving") : t("common.save")}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>
                        {t("common.cancel")}
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-1">
                      <Button size="sm" variant="ghost" onClick={() => startEdit(s.id, s.name, s.sequence, s.notes)}>
                        {t("common.edit")}
                      </Button>
                      {s.isActive ? (
                        /* A stage that IS the route's start or its final stop cannot be switched off:
                           doing so would leave new Job Orders with no formation stage. It is disabled
                           here with the reason, and the API refuses it as well. */
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={s.isFormationStage || s.isReadyGoodsStage || activeMutation.isPending}
                          title={s.isFormationStage || s.isReadyGoodsStage ? t("stg.routeStageLocked") : undefined}
                          onClick={() => activeMutation.mutate({ id: s.id, isActive: false })}
                        >
                          {t("common.deactivate")}
                        </Button>
                      ) : (
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={activeMutation.isPending}
                          onClick={() => activeMutation.mutate({ id: s.id, isActive: true })}
                        >
                          {t("common.activate")}
                        </Button>
                      )}
                    </div>
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
