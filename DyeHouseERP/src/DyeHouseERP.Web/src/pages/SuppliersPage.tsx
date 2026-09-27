import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SuppliersApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Supplier master (spec section 35). Phone/address/contact stay optional by design.
 * Used by the checks register today (outgoing checks and endorsements) and by the
 * purchases module that builds on this master.
 */
export default function SuppliersPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();

  const [showForm, setShowForm] = useState(false);
  const [search, setSearch] = useState("");
  const [form, setForm] = useState({
    code: "",
    nameAr: "",
    nameEn: "",
    accountNumber: "",
    phone: "",
    address: "",
    contactPerson: "",
    taxNumber: ""
  });
  const [error, setError] = useState<string | null>(null);

  const { data: suppliers, isLoading } = useQuery({
    queryKey: ["suppliers", search],
    queryFn: () => SuppliersApi.list({ search: search || undefined })
  });

  const createMutation = useMutation({
    mutationFn: () =>
      SuppliersApi.create({
        code: form.code,
        name: form.nameEn || form.nameAr,
        nameAr: form.nameAr || undefined,
        nameEn: form.nameEn || undefined,
        accountNumber: form.accountNumber || undefined,
        phone: form.phone || undefined,
        address: form.address || undefined,
        contactPerson: form.contactPerson || undefined,
        taxNumber: form.taxNumber || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["suppliers"] });
      setShowForm(false);
      setForm({ code: "", nameAr: "", nameEn: "", accountNumber: "", phone: "", address: "", contactPerson: "", taxNumber: "" });
      setError(null);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const toggleMutation = useMutation({
    mutationFn: (vars: { id: string; isActive: boolean }) => SuppliersApi.update(vars.id, { isActive: vars.isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["suppliers"] })
  });

  return (
    <>
      <PageHeader
        title={t("suppliers.title")}
        subtitle={t("suppliers.subtitle")}
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("suppliers.new")}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.code")}</label>
              <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} required maxLength={30} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.nameAr")}</label>
              <Input value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} maxLength={200} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.nameEn")}</label>
              <Input value={form.nameEn} onChange={(e) => setForm({ ...form, nameEn: e.target.value })} maxLength={200} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("customers.account")}</label>
              <Input value={form.accountNumber} onChange={(e) => setForm({ ...form, accountNumber: e.target.value })} maxLength={30} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("customers.phone")}</label>
              <Input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("customers.address")}</label>
              <Input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">Contact</label>
              <Input value={form.contactPerson} onChange={(e) => setForm({ ...form, contactPerson: e.target.value })} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <Card className="p-4 mb-4">
        <Input
          placeholder={t("common.searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("common.code")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.name")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("customers.account")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("customers.phone")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && suppliers?.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {suppliers?.map((s) => (
              <tr key={s.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{s.code}</td>
                <td className="px-4 py-3">{pick(s.nameAr, s.nameEn, s.name)}</td>
                <td className="px-4 py-3 ltr-nums">{s.accountNumber}</td>
                <td className="px-4 py-3 ltr-nums text-gray-600">{s.phone ?? "—"}</td>
                <td className="px-4 py-3">
                  <Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? t("common.active") : t("common.inactive")}</Badge>
                </td>
                <td className="px-4 py-3">
                  <button
                    className="text-brand-600 hover:underline text-xs font-semibold"
                    onClick={() => toggleMutation.mutate({ id: s.id, isActive: !s.isActive })}
                  >
                    {s.isActive ? t("common.inactive") : t("common.active")}
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
