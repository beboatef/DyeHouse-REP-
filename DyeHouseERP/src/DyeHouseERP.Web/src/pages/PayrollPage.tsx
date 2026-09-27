import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PayrollApi, TreasuryAccountsApi } from "@/api/client";
import type { EmployeeStatus, PayrollRun, PayrollRunStatus } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Payroll & wages (spec section 36): an INDEPENDENT module with its own tabs for
 * departments, employees and monthly payroll runs. Nothing here is allocated to
 * job-order costing - that would require an explicit approved allocation, which
 * this module deliberately does not do implicitly.
 *
 * Workflow: build the run for a period (one line per active employee) → adjust
 * allowances/deductions → pick the paying account → approve (accept the amounts)
 * → post (release the money). Approval and posting are separate permissions.
 */

type TabKey = "departments" | "employees" | "runs";

const tabs: { key: TabKey; labelKey: string }[] = [
  { key: "departments", labelKey: "pay.tab.departments" },
  { key: "employees", labelKey: "pay.tab.employees" },
  { key: "runs", labelKey: "pay.tab.runs" }
];

const runTone: Record<PayrollRunStatus, "gray" | "blue" | "green" | "red"> = {
  Draft: "gray",
  Approved: "blue",
  Posted: "green",
  Cancelled: "red"
};

const statusTone: Record<EmployeeStatus, "green" | "yellow" | "red"> = {
  Active: "green",
  Suspended: "yellow",
  Terminated: "red"
};

const money = (v: number) => v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export default function PayrollPage() {
  const { t } = useI18n();
  const [params, setParams] = useSearchParams();
  const initial = (params.get("tab") as TabKey) ?? "runs";
  const [tab, setTab] = useState<TabKey>(tabs.some((x) => x.key === initial) ? initial : "runs");

  const select = (key: TabKey) => {
    setTab(key);
    setParams({ tab: key }, { replace: true });
  };

  return (
    <>
      <PageHeader title={t("pay.title")} subtitle={t("pay.subtitle")} />

      <div className="flex flex-wrap gap-2 mb-5">
        {tabs.map((x) => (
          <Button key={x.key} variant={tab === x.key ? "primary" : "secondary"} onClick={() => select(x.key)}>
            {t(x.labelKey)}
          </Button>
        ))}
      </div>

      {tab === "departments" && <DepartmentsTab />}
      {tab === "employees" && <EmployeesTab />}
      {tab === "runs" && <RunsTab />}
    </>
  );
}

// ------------------------------------------------------------- departments

function DepartmentsTab() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ code: "", nameAr: "", nameEn: "", notes: "" });

  const { data: departments, isLoading } = useQuery({ queryKey: ["departments"], queryFn: () => PayrollApi.departments() });

  const createMutation = useMutation({
    mutationFn: () =>
      PayrollApi.createDepartment({
        code: form.code,
        nameAr: form.nameAr || undefined,
        nameEn: form.nameEn || undefined,
        notes: form.notes || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["departments"] });
      setShowForm(false);
      setForm({ code: "", nameAr: "", nameEn: "", notes: "" });
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const toggleMutation = useMutation({
    mutationFn: (vars: { id: string; isActive: boolean }) => PayrollApi.updateDepartment(vars.id, { isActive: vars.isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["departments"] })
  });

  return (
    <>
      <Card className="p-4 mb-4">
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pay.newDepartment")}</Button>
      </Card>

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
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("common.code")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.name")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pay.departmentCount")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={5} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && departments?.length === 0 && (
              <tr>
                <td colSpan={5} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {departments?.map((d) => (
              <tr key={d.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{d.code}</td>
                <td className="px-4 py-3">{pick(d.nameAr, d.nameEn, d.name)}</td>
                <td className="px-4 py-3 ltr-nums">{d.employeeCount}</td>
                <td className="px-4 py-3">
                  <Badge tone={d.isActive ? "green" : "gray"}>{d.isActive ? t("common.active") : t("common.inactive")}</Badge>
                </td>
                <td className="px-4 py-3">
                  <button
                    className="text-brand-600 hover:underline text-xs font-semibold"
                    onClick={() => toggleMutation.mutate({ id: d.id, isActive: !d.isActive })}
                  >
                    {d.isActive ? t("common.inactive") : t("common.active")}
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

// --------------------------------------------------------------- employees

function EmployeesTab() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [departmentFilter, setDepartmentFilter] = useState("");
  const [search, setSearch] = useState("");
  const [form, setForm] = useState({
    code: "",
    nameAr: "",
    nameEn: "",
    departmentId: "",
    jobTitle: "",
    basicSalary: "",
    hireDate: new Date().toISOString().slice(0, 10),
    phone: "",
    nationalId: "",
    bankAccountNumber: ""
  });

  const { data: departments } = useQuery({ queryKey: ["departments", "active"], queryFn: () => PayrollApi.departments({ activeOnly: true }) });
  const { data: employees, isLoading } = useQuery({
    queryKey: ["employees", departmentFilter, search],
    queryFn: () => PayrollApi.employees({ departmentId: departmentFilter || undefined, search: search || undefined })
  });

  const createMutation = useMutation({
    mutationFn: () =>
      PayrollApi.createEmployee({
        code: form.code,
        departmentId: form.departmentId,
        basicSalary: Number(form.basicSalary || 0),
        hireDate: form.hireDate,
        nameAr: form.nameAr || undefined,
        nameEn: form.nameEn || undefined,
        jobTitle: form.jobTitle || undefined,
        phone: form.phone || undefined,
        nationalId: form.nationalId || undefined,
        bankAccountNumber: form.bankAccountNumber || undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees"] });
      qc.invalidateQueries({ queryKey: ["departments"] });
      setShowForm(false);
      setForm({
        code: "",
        nameAr: "",
        nameEn: "",
        departmentId: "",
        jobTitle: "",
        basicSalary: "",
        hireDate: new Date().toISOString().slice(0, 10),
        phone: "",
        nationalId: "",
        bankAccountNumber: ""
      });
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const statusMutation = useMutation({
    mutationFn: (vars: { id: string; status: EmployeeStatus }) => PayrollApi.updateEmployee(vars.id, { status: vars.status }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees"] })
  });

  return (
    <>
      <Card className="p-4 mb-4 flex flex-wrap items-end gap-3">
        <div className="w-56">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.department")}</label>
          <Select value={departmentFilter} onChange={(e) => setDepartmentFilter(e.target.value)}>
            <option value="">{t("common.all")}</option>
            {departments?.map((d) => (
              <option key={d.id} value={d.id}>{d.code} - {pick(d.nameAr, d.nameEn, d.name)}</option>
            ))}
          </Select>
        </div>
        <div className="w-64">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.search")}</label>
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("common.searchPlaceholder")} />
        </div>
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pay.newEmployee")}</Button>
      </Card>

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
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.employeeCode")}</label>
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
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.department")}</label>
              <Select value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })} required>
                <option value="">{t("common.select")}</option>
                {departments?.map((d) => (
                  <option key={d.id} value={d.id}>{d.code} - {pick(d.nameAr, d.nameEn, d.name)}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.jobTitle")}</label>
              <Input value={form.jobTitle} onChange={(e) => setForm({ ...form, jobTitle: e.target.value })} maxLength={150} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.basicSalary")}</label>
              <Input
                type="number" step="0.01" min="0" value={form.basicSalary}
                onChange={(e) => setForm({ ...form, basicSalary: e.target.value })} required
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.hireDate")}</label>
              <Input type="date" value={form.hireDate} onChange={(e) => setForm({ ...form, hireDate: e.target.value })} required />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("customers.phone")}</label>
              <Input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} maxLength={40} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">National ID</label>
              <Input value={form.nationalId} onChange={(e) => setForm({ ...form, nationalId: e.target.value })} maxLength={40} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">Bank account</label>
              <Input value={form.bankAccountNumber} onChange={(e) => setForm({ ...form, bankAccountNumber: e.target.value })} maxLength={60} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("pay.employeeCode")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.name")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pay.department")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pay.jobTitle")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pay.basicSalary")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("pay.hireDate")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && employees?.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {employees?.map((e) => (
              <tr key={e.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{e.code}</td>
                <td className="px-4 py-3">{pick(e.nameAr, e.nameEn, e.name)}</td>
                <td className="px-4 py-3">{e.departmentName}</td>
                <td className="px-4 py-3 text-gray-600">{e.jobTitle ?? "—"}</td>
                <td className="px-4 py-3 ltr-nums">{money(e.basicSalary)}</td>
                <td className="px-4 py-3 ltr-nums">{new Date(e.hireDate).toLocaleDateString("en-GB")}</td>
                <td className="px-4 py-3">
                  <Badge tone={statusTone[e.status]}>{t(`pay.empStatus.${e.status}`)}</Badge>
                </td>
                <td className="px-4 py-3">
                  {e.status === "Active" ? (
                    <button
                      className="text-amber-600 hover:underline text-xs font-semibold"
                      onClick={() => statusMutation.mutate({ id: e.id, status: "Suspended" })}
                    >
                      {t("pay.empStatus.Suspended")}
                    </button>
                  ) : (
                    <button
                      className="text-brand-600 hover:underline text-xs font-semibold"
                      onClick={() => statusMutation.mutate({ id: e.id, status: "Active" })}
                    >
                      {t("pay.empStatus.Active")}
                    </button>
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

// -------------------------------------------------------------------- runs

function RunsTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [editLine, setEditLine] = useState<{ runId: string; lineId: string } | null>(null);
  const [editValues, setEditValues] = useState({ allowances: "", deductions: "" });
  const now = new Date();
  const [periodYear, setPeriodYear] = useState(String(now.getFullYear()));
  const [periodMonth, setPeriodMonth] = useState(String(now.getMonth() + 1));
  const [accountId, setAccountId] = useState("");
  const [notes, setNotes] = useState("");

  const { data: accounts } = useQuery({ queryKey: ["treasury-accounts"], queryFn: () => TreasuryAccountsApi.list() });
  const { data: runs, isLoading } = useQuery({ queryKey: ["payroll-runs"], queryFn: () => PayrollApi.runs() });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["payroll-runs"] });
    qc.invalidateQueries({ queryKey: ["treasury-accounts"] });
  };

  const createMutation = useMutation({
    mutationFn: () =>
      PayrollApi.createRun({
        periodYear: Number(periodYear),
        periodMonth: Number(periodMonth),
        treasuryAccountId: accountId || undefined,
        notes: notes || undefined
      }),
    onSuccess: () => {
      invalidate();
      setShowForm(false);
      setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const approveMutation = useMutation({
    mutationFn: (id: string) => PayrollApi.approveRun(id),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const postMutation = useMutation({
    mutationFn: (id: string) => PayrollApi.postRun(id),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const cancelMutation = useMutation({
    mutationFn: (vars: { id: string; reason: string }) => PayrollApi.cancelRun(vars.id, vars.reason),
    onSuccess: invalidate,
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const lineMutation = useMutation({
    mutationFn: (vars: { run: PayrollRun; lineId: string; allowances: string; deductions: string }) =>
      PayrollApi.updateRunLine(vars.run.id, vars.lineId, {
        allowances: Number(vars.allowances || 0),
        deductions: Number(vars.deductions || 0)
      }),
    onSuccess: () => {
      invalidate();
      setEditLine(null);
      setEditValues({ allowances: "", deductions: "" });
    },
    onError: (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const removeLineMutation = useMutation({
    mutationFn: (vars: { runId: string; lineId: string }) => PayrollApi.removeRunLine(vars.runId, vars.lineId),
    onSuccess: invalidate
  });

  return (
    <>
      <Card className="p-4 mb-4">
        <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("pay.newRun")}</Button>
        <p className="text-xs text-gray-500 mt-2">{t("pay.runHint")}</p>
      </Card>

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
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.periodYear")}</label>
              <Input type="number" min="2000" max="2200" value={periodYear} onChange={(e) => setPeriodYear(e.target.value)} required />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.periodMonth")}</label>
              <Select value={periodMonth} onChange={(e) => setPeriodMonth(e.target.value)} required>
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <option key={m} value={m}>{String(m).padStart(2, "0")}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("pay.selectAccount")}</label>
              <Select value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                <option value="">{t("common.select")}</option>
                {accounts?.map((a) => (
                  <option key={a.id} value={a.id}>{a.code} - {a.name}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
          <p className="text-xs text-gray-500 mt-2">{t("pay.oneRunPerMonth")}</p>
        </Card>
      )}

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <div className="space-y-4">
        {isLoading && <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>}
        {!isLoading && runs?.length === 0 && <Card className="p-6 text-center text-gray-400">{t("common.empty")}</Card>}

        {runs?.map((r) => (
          <Card key={r.id} className="p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-bold ltr-nums">{r.runNumber}</span>
                <Badge tone={runTone[r.status]}>{t(`pay.runStatus.${r.status}`)}</Badge>
                <span className="text-sm text-gray-600 ltr-nums">{String(r.periodMonth).padStart(2, "0")}/{r.periodYear}</span>
                <span className="text-gray-400">·</span>
                <span className="text-sm text-gray-600">{r.employeeCount} {t("pay.employeeCount")}</span>
                {r.treasuryAccountName && (
                  <>
                    <span className="text-gray-400">·</span>
                    <span className="text-sm text-gray-600">{r.treasuryAccountName}</span>
                  </>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold ltr-nums">{t("pay.totalNet")}: {money(r.totalNet)}</span>
                <Button variant="ghost" onClick={() => setExpanded(expanded === r.id ? null : r.id)}>
                  {t("common.details")}
                </Button>
                {r.status === "Draft" && (
                  <Button variant="secondary" onClick={() => approveMutation.mutate(r.id)}>{t("pay.approve")}</Button>
                )}
                {r.status === "Approved" && (
                  <Button variant="secondary" onClick={() => postMutation.mutate(r.id)}>{t("pay.post")}</Button>
                )}
                {r.status !== "Cancelled" && r.status !== "Posted" && (
                  <Button
                    variant="ghost"
                    onClick={() => {
                      const reason = window.prompt(t("pay.cancelReason"));
                      if (reason) cancelMutation.mutate({ id: r.id, reason });
                    }}
                  >
                    {t("pay.cancelRun")}
                  </Button>
                )}
              </div>
            </div>

            {expanded === r.id && (
              <div className="mt-4">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-gray-400 text-xs border-b border-gray-100">
                      <th className="text-start py-2 font-medium">{t("pay.employee")}</th>
                      <th className="text-start py-2 font-medium">{t("pay.department")}</th>
                      <th className="text-start py-2 font-medium">{t("pay.basicSalary")}</th>
                      <th className="text-start py-2 font-medium">{t("pay.allowances")}</th>
                      <th className="text-start py-2 font-medium">{t("pay.deductions")}</th>
                      <th className="text-start py-2 font-medium">{t("pay.net")}</th>
                      <th className="text-start py-2 font-medium" />
                    </tr>
                  </thead>
                  <tbody>
                    {r.lines.map((l) => {
                      const editing = editLine?.runId === r.id && editLine.lineId === l.id;
                      return (
                        <tr key={l.id} className="border-b border-gray-50 last:border-0">
                          <td className="py-2">{l.employeeCode} - {l.employeeName}</td>
                          <td className="py-2 text-gray-600">{l.departmentName ?? "—"}</td>
                          <td className="py-2 ltr-nums">{money(l.basicSalary)}</td>
                          <td className="py-2 ltr-nums">
                            {editing ? (
                              <Input
                                type="number" step="0.01" min="0" value={editValues.allowances}
                                onChange={(e) => setEditValues({ ...editValues, allowances: e.target.value })}
                              />
                            ) : (
                              money(l.allowances)
                            )}
                          </td>
                          <td className="py-2 ltr-nums">
                            {editing ? (
                              <Input
                                type="number" step="0.01" min="0" value={editValues.deductions}
                                onChange={(e) => setEditValues({ ...editValues, deductions: e.target.value })}
                              />
                            ) : (
                              money(l.deductions)
                            )}
                          </td>
                          <td className="py-2 ltr-nums font-medium">{money(l.netPay)}</td>
                          <td className="py-2">
                            {r.status === "Draft" && !editing && (
                              <>
                                <button
                                  className="text-brand-600 hover:underline text-xs font-semibold"
                                  onClick={() => {
                                    setEditLine({ runId: r.id, lineId: l.id });
                                    setEditValues({ allowances: String(l.allowances), deductions: String(l.deductions) });
                                  }}
                                >
                                  {t("pay.editLine")}
                                </button>
                                <button
                                  className="text-red-600 hover:underline text-xs font-semibold ms-3"
                                  onClick={() => removeLineMutation.mutate({ runId: r.id, lineId: l.id })}
                                >
                                  {t("pay.removeLine")}
                                </button>
                              </>
                            )}
                            {editing && (
                              <>
                                <button
                                  className="text-brand-600 hover:underline text-xs font-semibold"
                                  onClick={() =>
                                    lineMutation.mutate({
                                      run: r,
                                      lineId: l.id,
                                      allowances: editValues.allowances,
                                      deductions: editValues.deductions
                                    })
                                  }
                                >
                                  {t("common.save")}
                                </button>
                                <button
                                  className="text-gray-500 hover:underline text-xs font-semibold ms-3"
                                  onClick={() => setEditLine(null)}
                                >
                                  {t("common.cancel")}
                                </button>
                              </>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
