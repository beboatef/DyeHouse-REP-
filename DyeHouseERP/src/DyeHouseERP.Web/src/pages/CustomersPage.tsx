import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Customer, CustomersApi } from "@/api/client";
import { CustomersExports } from "@/api/exports";
import { PageHeader, Card, Button, Input, Select, Badge, TableStateRow, TableWrap } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";
import { useI18n } from "@/i18n";

/**
 * Customer master data (spec section 7).
 *
 * The Code is entered by the user and is the key every downstream document refers
 * to, so it is shown but NEVER editable - renaming it would orphan history.
 * Everything descriptive (name, phone, address, contact, tax number) is editable,
 * and deactivation is an explicit action rather than a delete.
 */
type FormState = {
  code: string;
  name: string;
  phone: string;
  address: string;
  contactPerson: string;
  taxNumber: string;
  /** Direct parent customer id, or "" for a top-level customer. */
  parentCustomerId: string;
};

const emptyForm: FormState = { code: "", name: "", phone: "", address: "", contactPerson: "", taxNumber: "", parentCustomerId: "" };

export default function CustomersPage() {
  const { t } = useI18n();
  const qc = useQueryClient();

  const [search, setSearch] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Customer | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [error, setError] = useState<string | null>(null);

  const { data: customers, isLoading } = useQuery({
    queryKey: ["customers", search],
    queryFn: () => CustomersApi.list({ search: search || undefined })
  });

  const reset = () => {
    setShowForm(false);
    setEditing(null);
    setForm(emptyForm);
    setError(null);
  };

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (editing) {
        const updated = await CustomersApi.update(editing.id, {
          name: form.name,
          phone: form.phone || null,
          address: form.address || null,
          contactPerson: form.contactPerson || null,
          taxNumber: form.taxNumber || null
        });
        // The parent (sub-customer) relationship is saved through its own endpoint, which
        // validates self-parenting and cycles - only call it when it actually changed.
        const desired = form.parentCustomerId || null;
        if ((editing.parentCustomerId ?? null) !== desired)
          return CustomersApi.setParent(editing.id, desired);
        return updated;
      }
      return CustomersApi.create({
            code: form.code,
            name: form.name,
            phone: form.phone || null,
            address: form.address || null,
            contactPerson: form.contactPerson || null,
            taxNumber: form.taxNumber || null
          });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customers"] });
      reset();
    },
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  // Deactivation, never deletion: customers own raw material and historical documents.
  const activeMutation = useMutation({
    mutationFn: (c: Customer) => CustomersApi.setActive(c.id, !c.isActive),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["customers"] }),
    onError: (err: ApiErrorLike) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const startEdit = (c: Customer) => {
    setEditing(c);
    setForm({
      code: c.code,
      name: c.name,
      phone: c.phone ?? "",
      address: c.address ?? "",
      contactPerson: c.contactPerson ?? "",
      taxNumber: c.taxNumber ?? "",
      parentCustomerId: c.parentCustomerId ?? ""
    });
    setShowForm(true);
    setError(null);
  };

  return (
    <>
      <PageHeader
        title={t("cust.title")}
        subtitle={t("cust.subtitle")}
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: CustomersExports.excel }}
              pdf={{ action: CustomersExports.pdf }}
            />
            <Button onClick={() => (showForm ? reset() : setShowForm(true))}>
              {showForm ? t("common.cancel") : t("cust.new")}
            </Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <h3 className="text-sm font-bold text-slate-800 mb-3">
            {editing ? t("cust.editTitle") : t("cust.newTitle")}
          </h3>
          <form
            className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              saveMutation.mutate();
            }}
          >
            <div>
              <label className="field-label">{t("cust.code")}</label>
              <Input
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                required
                maxLength={30}
                // The code is the join key to every downstream document, so it is fixed once created.
                disabled={Boolean(editing)}
              />
              {editing && <p className="text-[10px] text-slate-400 mt-1">{t("cust.codeLocked")}</p>}
            </div>
            <div>
              <label className="field-label">{t("cust.name")}</label>
              <Input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                required
                maxLength={200}
              />
            </div>
            <div>
              <label className="field-label">{t("cust.phone")}</label>
              <Input
                value={form.phone}
                onChange={(e) => setForm({ ...form, phone: e.target.value })}
                maxLength={50}
              />
            </div>
            <div className="sm:col-span-2">
              <label className="field-label">{t("cust.address")}</label>
              <Input
                value={form.address}
                onChange={(e) => setForm({ ...form, address: e.target.value })}
                maxLength={500}
              />
            </div>
            <div>
              <label className="field-label">{t("cust.contactPerson")}</label>
              <Input
                value={form.contactPerson}
                onChange={(e) => setForm({ ...form, contactPerson: e.target.value })}
                maxLength={200}
              />
            </div>
            <div>
              <label className="field-label">{t("cust.taxNumber")}</label>
              <Input
                value={form.taxNumber}
                onChange={(e) => setForm({ ...form, taxNumber: e.target.value })}
                maxLength={50}
              />
            </div>
            {/* Sub-customer relationship (spec section 7): an explicit, stored parent. Only
                meaningful while editing; a customer is never its own parent. */}
            {editing && (
              <div>
                <label className="field-label">{t("cust.parent")}</label>
                <Select
                  value={form.parentCustomerId}
                  onChange={(e) => setForm({ ...form, parentCustomerId: e.target.value })}
                >
                  <option value="">{t("cust.noParent")}</option>
                  {customers
                    ?.filter((x) => x.id !== editing.id)
                    .map((x) => (
                      <option key={x.id} value={x.id}>{x.code} — {x.name}</option>
                    ))}
                </Select>
                <p className="text-[10px] text-slate-400 mt-1">{t("cust.parentHint")}</p>
              </div>
            )}
            <div className="flex items-center gap-3">
              <Button type="submit" disabled={saveMutation.isPending || !form.name}>
                {saveMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              {editing && <Button type="button" variant="ghost" onClick={reset}>{t("common.cancel")}</Button>}
            </div>
          </form>
          {error && <p className="form-error mt-3">{error}</p>}
        </Card>
      )}

      <ImportPanel
        base="/customers/import"
        columns={["Code", "Name", "AccountNumber", "Phone", "Address", "ContactPerson", "TaxNumber", "IsActive"]}
        title={t("cust.importTitle")}
        onDone={() => qc.invalidateQueries({ queryKey: ["customers"] })}
      />

      <Card className="p-4 mb-4">
        <Input placeholder={t("cust.search")} value={search} onChange={(e) => setSearch(e.target.value)} />
      </Card>

      <Card>
        <TableWrap>
          <table className="table">
            <thead>
              <tr>
                <th>{t("cust.code")}</th>
                <th>{t("cust.name")}</th>
                <th>{t("cust.parent")}</th>
                <th>{t("cust.phone")}</th>
                <th>{t("cust.contactPerson")}</th>
                <th>{t("cust.taxNumber")}</th>
                <th>{t("common.status")}</th>
                <th>{t("common.actions")}</th>
              </tr>
            </thead>
            <tbody>
              <TableStateRow
                colSpan={8}
                loading={isLoading}
                isEmpty={customers?.length === 0}
                loadingText={t("common.loading")}
                emptyText={t("cust.empty")}
              />
              {customers?.map((c) => (
                <tr key={c.id} className={`border-b border-gray-100 last:border-0 hover:bg-gray-50 ${c.isActive ? "" : "opacity-60"}`}>
                  <td className="font-medium ltr-nums">{c.code}</td>
                  <td>{c.name}</td>
                  <td className="ltr-nums">{c.parentCustomerCode ?? "—"}</td>
                  <td className="ltr-nums">{c.phone ?? "—"}</td>
                  <td>{c.contactPerson ?? "—"}</td>
                  <td className="ltr-nums">{c.taxNumber ?? "—"}</td>
                  <td>
                    <Badge tone={c.isActive ? "green" : "gray"}>
                      {c.isActive ? t("cust.active") : t("cust.inactive")}
                    </Badge>
                  </td>
                  <td>
                    <div className="flex gap-2">
                      <Button variant="ghost" onClick={() => startEdit(c)}>{t("common.edit")}</Button>
                      <Button
                        variant="ghost"
                        onClick={() => activeMutation.mutate(c)}
                        disabled={activeMutation.isPending}
                      >
                        {c.isActive ? t("cust.deactivate") : t("cust.activate")}
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableWrap>
      </Card>

      {error && !showForm && <p className="form-error mt-3">{error}</p>}
    </>
  );
}