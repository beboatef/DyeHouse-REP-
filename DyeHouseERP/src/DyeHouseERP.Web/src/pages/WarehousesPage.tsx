import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";
import { WarehousesExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/** `value` is the technical WarehouseKind sent to the API - only the label is translated. */
const kinds = [
  { value: "RawMaterial", labelKey: "wh.kind.RawMaterial" },
  { value: "Materials", labelKey: "wh.kind.Materials" },
  { value: "ReadyGoods", labelKey: "wh.kind.ReadyGoods" }
];

export default function WarehousesPage() {
  const { t } = useI18n();
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [kind, setKind] = useState("RawMaterial");
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  /** The row being renamed in place (name only - see the note on Kind below). */
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");

  const qc = useQueryClient();
  const invalidate = () => qc.invalidateQueries({ queryKey: ["warehouses"] });

  const { data: warehouses, isLoading, isError } = useQuery({
    queryKey: ["warehouses", search],
    queryFn: () => WarehousesApi.list({ search: search.trim() || undefined })
  });

  const apiError = (err: any) =>
    err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error");

  const createMutation = useMutation({
    mutationFn: WarehousesApi.create,
    onSuccess: () => {
      invalidate();
      setShowForm(false);
      setCode("");
      setName("");
      setError(null);
    },
    onError: (err: any) => setError(apiError(err))
  });

  /**
   * Renaming a warehouse. Only the NAME is editable: the Kind decides whether the
   * stock inside is customer-owned or factory-owned, so reinterpreting it would
   * silently reinterpret every balance the warehouse already holds - which is why the
   * import flow is create-only too. The current Kind is sent back unchanged.
   */
  const renameMutation = useMutation({
    mutationFn: ({ id, kind }: { id: string; kind: string }) => WarehousesApi.update(id, { name: editName, kind }),
    onSuccess: () => {
      invalidate();
      setEditingId(null);
      setEditName("");
      setError(null);
    },
    onError: (err: any) => setError(apiError(err))
  });

  /**
   * Deactivation is the delete substitute. Nothing is removed: existing movements,
   * balances and documents keep pointing at this warehouse and stay readable, and the
   * warehouse simply stops being offered for new operations.
   */
  const activeMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) => WarehousesApi.setActive(id, isActive),
    onSuccess: () => { invalidate(); setError(null); },
    onError: (err: any) => setError(apiError(err))
  });

  /** A kind the catalogue does not know (data written by another client) falls back to its raw value. */
  const kindLabel = (k: string) => {
    const known = kinds.find((x) => x.value === k);
    return known ? t(known.labelKey) : k;
  };

  return (
    <>
      <PageHeader
        title={t("nav.warehouses")}
        subtitle={t("wh.masterSubtitle")}
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: WarehousesExports.excel }}
              pdf={{ action: WarehousesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("wh.new")}</Button>
          </div>
        }
      />

      {/*
        Warehouses import is create-only by design: changing a warehouse's Kind
        would silently reinterpret every historical balance it already holds,
        so an existing code is always reported as a conflict rather than
        updated. `supportsUpdate={false}` is what shows that in the UI.
      */}
      <ImportPanel
        base="/warehouses/import"
        supportsUpdate={false}
        columns={["Code", "Name", "Kind"]}
        title={t("wh.importTitle")}
        onDone={invalidate}
      />

      <div className="flex flex-wrap items-center gap-3 mb-4">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t("common.searchPlaceholder")}
          aria-label={t("common.search")}
          className="w-full sm:w-72"
        />
      </div>

      {error && <p className="form-error mb-4" role="alert">{error}</p>}

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate({ code, name, kind });
            }}
          >
            <div>
              <label className="field-label">{t("wh.code")}</label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} />
            </div>
            <div>
              <label className="field-label">{t("wh.name")}</label>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="field-label">{t("wh.kind")}</label>
              <Select value={kind} onChange={(e) => setKind(e.target.value)}>
                {kinds.map((k) => (
                  <option key={k.value} value={k.value}>{t(k.labelKey)}</option>
                ))}
              </Select>
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
              <th>{t("common.code")}</th>
              <th>{t("common.name")}</th>
              <th>{t("common.type")}</th>
              <th>{t("common.status")}</th>
              <th>{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {isError && (
              <tr><td colSpan={5} className="px-4 py-6 text-center text-red-500">{t("common.error")}</td></tr>
            )}
            {!isLoading && !isError && warehouses?.length === 0 && (
              <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {warehouses?.map((w) => (
              <tr key={w.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{w.code}</td>
                <td>
                  {editingId === w.id ? (
                    <Input
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      maxLength={200}
                      aria-label={t("common.name")}
                      autoFocus
                    />
                  ) : (
                    w.name
                  )}
                </td>
                <td>
                  {/* The Kind is deliberately NOT editable here: it decides whether the stock is
                      customer-owned or factory-owned, so changing it would silently reinterpret every
                      balance this warehouse already holds (the same reason the import flow is
                      create-only). Only the name is editable - see renameMutation. */}
                  <Badge tone="blue">{kindLabel(w.kind)}</Badge>
                </td>
                <td>
                  <Badge tone={w.isActive ? "green" : "gray"}>
                    {w.isActive ? t("common.active") : t("common.inactive")}
                  </Badge>
                </td>
                <td>
                  {editingId === w.id ? (
                    <div className="flex flex-wrap gap-1">
                      <Button
                        size="sm"
                        disabled={renameMutation.isPending || !editName.trim()}
                        onClick={() => renameMutation.mutate({ id: w.id, kind: w.kind })}
                      >
                        {renameMutation.isPending ? t("common.saving") : t("common.save")}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>
                        {t("common.cancel")}
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => { setEditingId(w.id); setEditName(w.name); setError(null); }}
                      >
                        {t("common.edit")}
                      </Button>
                      {w.isActive ? (
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={activeMutation.isPending}
                          onClick={() => activeMutation.mutate({ id: w.id, isActive: false })}
                        >
                          {t("common.deactivate")}
                        </Button>
                      ) : (
                        <Button
                          size="sm"
                          variant="ghost"
                          disabled={activeMutation.isPending}
                          onClick={() => activeMutation.mutate({ id: w.id, isActive: true })}
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
