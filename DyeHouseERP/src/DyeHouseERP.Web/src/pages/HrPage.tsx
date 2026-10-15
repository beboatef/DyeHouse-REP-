import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  HrApi, PayrollApi,
  type AttendanceStatus, type AdvanceStatus, type LaborCostRow,
  type AttendanceRule, type ContributionBase, type ContributionKind, type Employee,
  type LeaveStatus, type OvertimeCategory,
  type PayComponentCalculation, type PayComponentKind, type PayrollAccountKind
} from "@/api/client";
import { downloadFile } from "@/api/exports";
import { Badge, Button, Card, Input, PageHeader, Select, errorMessage } from "@/components/ui";
import { useI18n } from "@/i18n";

type Tab = "config" | "attendance" | "leave" | "overtime" | "advances" | "laborCost";

const toMinutes = (value: string) => {
  const [h, m] = value.split(":").map(Number);
  return Number.isFinite(h) && Number.isFinite(m) ? h * 60 + m : 0;
};
const fromMinutes = (value: number) =>
  `${String(Math.floor(value / 60)).padStart(2, "0")}:${String(value % 60).padStart(2, "0")}`;

const today = new Date().toISOString().slice(0, 10);
const monthStart = (year: number, month: number) => new Date(year, month - 1, 1).toISOString().slice(0, 10);
const monthEnd = (year: number, month: number) => new Date(year, month, 0).toISOString().slice(0, 10);

/**
 * HR, attendance and the CONFIGURABLE wage rules (spec sections 28-42).
 *
 * Everything the payroll calculation consumes is master data on this screen:
 * sections, jobs, cost centers, HR attendance shifts (deliberately separate from
 * the production execution shifts), the attendance policy, leave types, pay
 * components, statutory contribution rules and the accounting mappings. Nothing
 * factory-specific is hard-coded, so the factory configures its own rules.
 *
 * Leave and overtime are REQUESTED here and DECIDED by a different authenticated
 * user - the server refuses self-approval, and the buttons only mirror that.
 */
export default function HrPage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<Tab>("config");

  const tabs: { key: Tab; label: string }[] = [
    { key: "config", label: t("hr.tab.config") },
    { key: "attendance", label: t("hr.tab.attendance") },
    { key: "leave", label: t("hr.tab.leave") },
    { key: "overtime", label: t("hr.tab.overtime") },
    { key: "advances", label: t("hr.tab.advances") },
    { key: "laborCost", label: t("hr.tab.laborCost") }
  ];

  return (
    <>
      <PageHeader title={t("hr.title")} subtitle={t("hr.subtitle")} />

      <div className="tabs mb-6">
        {tabs.map((item) => (
          <button
            key={item.key}
            onClick={() => setTab(item.key)}
            className={`tab ${tab === item.key ? "tab-active" : ""}`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {tab === "config" && <ConfigTab />}
      {tab === "attendance" && <AttendanceTab />}
      {tab === "leave" && <LeaveTab />}
      {tab === "overtime" && <OvertimeTab />}
      {tab === "advances" && <AdvancesTab />}
      {tab === "laborCost" && <LaborCostTab />}
    </>
  );
}

/** The employees every workflow picks from, loaded once per tab. */
export function useEmployees() {
  return useQuery({ queryKey: ["employees", "hr"], queryFn: () => PayrollApi.employees({ activeOnly: true }) });
}

// --------------------------------------------------------------------- config

function ConfigTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const shifts = useQuery({ queryKey: ["hr", "shifts"], queryFn: () => HrApi.shifts() });
  const rules = useQuery({ queryKey: ["hr", "attendance-rules"], queryFn: () => HrApi.attendanceRules() });
  const leaveTypes = useQuery({ queryKey: ["hr", "leave-types"], queryFn: () => HrApi.leaveTypes() });
  const components = useQuery({ queryKey: ["hr", "pay-components"], queryFn: () => HrApi.payComponents() });
  const contributions = useQuery({ queryKey: ["hr", "contributions"], queryFn: () => HrApi.contributionRules() });
  const mappings = useQuery({ queryKey: ["hr", "account-mappings"], queryFn: () => HrApi.accountMappings() });
  const costCenters = useQuery({ queryKey: ["hr", "cost-centers"], queryFn: () => HrApi.costCenters() });
  const sections = useQuery({ queryKey: ["hr", "sections"], queryFn: () => HrApi.sections() });
  const jobs = useQuery({ queryKey: ["hr", "jobs"], queryFn: () => HrApi.jobs() });
  const departments = useQuery({ queryKey: ["departments"], queryFn: () => PayrollApi.departments() });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["hr"] });
  const onError = (err: unknown) => setError(errorMessage(err, t("hr.saveFailed")));

  const createShift = useMutation({
    mutationFn: HrApi.createShift,
    onSuccess: () => { invalidate(); setError(null); },
    onError
  });
  const createRule = useMutation({
    mutationFn: (body: Partial<AttendanceRule> & { name: string; activate?: boolean }) => HrApi.createAttendanceRule(body),
    onSuccess: () => { invalidate(); setError(null); },
    onError
  });
  const activateRule = useMutation({
    mutationFn: (rule: AttendanceRule) => HrApi.updateAttendanceRule(rule.id, { isActive: true }),
    onSuccess: () => { invalidate(); setError(null); },
    onError
  });
  const createLeaveType = useMutation({ mutationFn: HrApi.createLeaveType, onSuccess: () => { invalidate(); setError(null); }, onError });
  const createComponent = useMutation({
    mutationFn: (body: Partial<import("@/api/client").PayComponent> & { code: string; kind: PayComponentKind; calculation: PayComponentCalculation }) =>
      HrApi.createPayComponent(body),
    onSuccess: () => { invalidate(); setError(null); }, onError
  });
  const createContribution = useMutation({ mutationFn: HrApi.createContributionRule, onSuccess: () => { invalidate(); setError(null); }, onError });
  const upsertMapping = useMutation({ mutationFn: HrApi.upsertAccountMapping, onSuccess: () => { invalidate(); setError(null); }, onError });
  const createCostCenter = useMutation({ mutationFn: HrApi.createCostCenter, onSuccess: () => { invalidate(); setError(null); }, onError });
  const createSection = useMutation({ mutationFn: HrApi.createSection, onSuccess: () => { invalidate(); setError(null); }, onError });
  const createJob = useMutation({ mutationFn: HrApi.createJob, onSuccess: () => { invalidate(); setError(null); }, onError });

  const [shift, setShift] = useState({ code: "", nameAr: "", nameEn: "", start: "08:00", end: "16:00", break: "0", grace: "10", days: "0,1,2,3,4" });
  const [leaveType, setLeaveType] = useState({ code: "", nameAr: "", nameEn: "", isPaid: true, annualDays: "21" });
  const [component, setComponent] = useState<{ code: string; nameAr: string; nameEn: string; kind: PayComponentKind; calculation: PayComponentCalculation; amount: string; percent: string }>(
    { code: "", nameAr: "", nameEn: "", kind: "Allowance", calculation: "FixedAmount", amount: "0", percent: "0" });
  const [contribution, setContribution] = useState<{ code: string; nameAr: string; nameEn: string; kind: ContributionKind; base: ContributionBase; employee: string; employer: string }>(
    { code: "", nameAr: "", nameEn: "", kind: "Tax", base: "GrossPay", employee: "0", employer: "0" });
  const [mapping, setMapping] = useState<{ kind: PayrollAccountKind; accountCode: string; accountName: string }>(
    { kind: "BasicSalary", accountCode: "", accountName: "" });
  const [costCenter, setCostCenter] = useState({ code: "", nameAr: "", nameEn: "" });
  const [section, setSection] = useState({ departmentId: "", code: "", nameAr: "", nameEn: "" });
  const [job, setJob] = useState({ code: "", nameAr: "", nameEn: "", departmentId: "", defaultSalary: "" });

  // Assigning an employee to a section/job/cost center/attendance shift and its
  // pay components is what makes the configured rules reach a real payslip.
  const employees = useEmployees();
  const assignments = useQuery({
    queryKey: ["hr", "employee-pay-components"],
    queryFn: () => HrApi.employeePayComponents({ activeOnly: true })
  });
  const [profile, setProfile] = useState({
    employeeId: "", hrSectionId: "", jobPositionId: "", costCenterId: "", hrShiftId: "",
    taxExempt: false, insuranceExempt: false
  });
  const [assign, setAssign] = useState({
    employeeId: "", payComponentId: "", amount: "", percent: "", effectiveFrom: today
  });

  const saveProfile = useMutation({
    mutationFn: () => HrApi.setEmployeeProfile(profile.employeeId, {
      hrSectionId: profile.hrSectionId || null,
      jobPositionId: profile.jobPositionId || null,
      costCenterId: profile.costCenterId || null,
      hrShiftId: profile.hrShiftId || null,
      isTaxExempt: profile.taxExempt,
      isInsuranceExempt: profile.insuranceExempt
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["employees"] }); setError(null); },
    onError
  });

  const assignComponent = useMutation({
    mutationFn: () => HrApi.assignEmployeePayComponent({
      employeeId: assign.employeeId, payComponentId: assign.payComponentId,
      effectiveFrom: assign.effectiveFrom,
      amount: assign.amount ? Number(assign.amount) : undefined,
      percent: assign.percent ? Number(assign.percent) : undefined
    }),
    onSuccess: () => { invalidate(); setError(null); },
    onError
  });

  const removeAssignment = useMutation({
    mutationFn: (id: string) => HrApi.removeEmployeePayComponent(id),
    onSuccess: () => { invalidate(); setError(null); },
    onError
  });

  /** Loads the selected employee's current classification into the editor. */
  const pickEmployee = (employeeId: string) => {
    const employee = employees.data?.find((e) => e.id === employeeId);
    setProfile({
      employeeId,
      hrSectionId: employee?.hrSectionId ?? "",
      jobPositionId: employee?.jobPositionId ?? "",
      costCenterId: employee?.costCenterId ?? "",
      hrShiftId: employee?.hrShiftId ?? "",
      taxExempt: employee?.isTaxExempt ?? false,
      insuranceExempt: employee?.isInsuranceExempt ?? false
    });
  };

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      {/* HR attendance shift - separate from the production execution shift. */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.sec.shifts")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.shiftsHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-8 gap-2 items-end">
          <Input placeholder={t("common.code")} value={shift.code} onChange={(e) => setShift({ ...shift, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={shift.nameAr} onChange={(e) => setShift({ ...shift, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={shift.nameEn} onChange={(e) => setShift({ ...shift, nameEn: e.target.value })} />
          <Input type="time" value={shift.start} onChange={(e) => setShift({ ...shift, start: e.target.value })} />
          <Input type="time" value={shift.end} onChange={(e) => setShift({ ...shift, end: e.target.value })} />
          <Input placeholder={t("hr.break")} value={shift.break} onChange={(e) => setShift({ ...shift, break: e.target.value })} />
          <Input placeholder={t("hr.grace")} value={shift.grace} onChange={(e) => setShift({ ...shift, grace: e.target.value })} />
          <Button
            disabled={!shift.code || createShift.isPending}
            onClick={() => createShift.mutate({
              code: shift.code, nameAr: shift.nameAr || undefined, nameEn: shift.nameEn || undefined,
              startMinutes: toMinutes(shift.start), endMinutes: toMinutes(shift.end),
              breakMinutes: Number(shift.break) || 0, graceMinutes: Number(shift.grace) || 0, workingDays: shift.days
            })}
          >{t("common.add")}</Button>
        </div>
        <table className="table mt-3">
          <thead><tr>
            <th>{t("common.code")}</th><th>{t("common.nameAr")}</th><th>{t("hr.start")}</th><th>{t("hr.end")}</th>
            <th>{t("hr.workingDays")}</th><th>{t("hr.nightShift")}</th><th>{t("common.status")}</th>
          </tr></thead>
          <tbody>
            {shifts.data?.map((s) => (
              <tr key={s.id} className="border-b border-line last:border-0">
                <td className="ltr-nums">{s.code}</td><td>{s.nameAr}</td>
                <td className="ltr-nums">{fromMinutes(s.startMinutes)}</td><td className="ltr-nums">{fromMinutes(s.endMinutes)}</td>
                <td className="ltr-nums">{s.workingDays}</td>
                <td>{s.isNightShift ? t("common.yes") : t("common.no")}</td>
                <td><Badge tone={s.isActive ? "green" : "gray"}>{s.isActive ? t("common.active") : t("common.inactive")}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Attendance policy - standard hours, penalties and overtime multipliers. */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.sec.policy")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.policyHint")}</p>
        <div className="flex flex-wrap gap-2 mb-3">
          <Button
            disabled={createRule.isPending}
            onClick={() => createRule.mutate({ name: t("hr.defaultPolicy"), activate: true })}
          >{t("hr.createDefaultPolicy")}</Button>
          {rules.data?.filter((r) => !r.isActive).map((r) => (
            <Button key={r.id} variant="secondary" onClick={() => activateRule.mutate(r)}
              disabled={activateRule.isPending}>{t("hr.activate")}: {r.name}</Button>
          ))}
        </div>
        {/* 11 columns - wider than the card at laptop width, so it scrolls
            sideways instead of being clipped, matching the wide-table pattern
            used on the formation/customer-return detail screens. */}
        <div className="overflow-x-auto">
          <table className="table">
            <thead><tr>
              <th>{t("common.name")}</th><th>{t("hr.stdDailyHours")}</th><th>{t("hr.stdWorkingDays")}</th>
              <th>{t("hr.grace")}</th><th>{t("hr.latePenalty")}</th><th>{t("hr.earlyLeavePenalty")}</th>
              <th>{t("hr.absenceFactor")}</th><th>{t("hr.otNormal")}</th><th>{t("hr.otWeekend")}</th>
              <th>{t("hr.otHoliday")}</th><th>{t("common.status")}</th>
            </tr></thead>
            <tbody>
              {rules.data?.map((r) => (
                <tr key={r.id} className="border-b border-line last:border-0">
                  <td>{r.name}</td><td className="ltr-nums">{r.standardDailyHours}</td><td className="ltr-nums">{r.standardWorkingDaysPerMonth}</td>
                  <td className="ltr-nums">{r.graceMinutes}</td><td className="ltr-nums">{r.latePenaltyPerMinute}</td>
                  <td className="ltr-nums">{r.earlyLeavePenaltyPerMinute}</td><td className="ltr-nums">{r.absenceDayDeductionFactor}</td>
                  <td className="ltr-nums">{r.overtimeNormalMultiplier}</td><td className="ltr-nums">{r.overtimeWeekendMultiplier}</td>
                  <td className="ltr-nums">{r.overtimeHolidayMultiplier}</td>
                  <td><Badge tone={r.isActive ? "green" : "gray"}>{r.isActive ? t("common.active") : t("common.inactive")}</Badge></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Leave types */}
      <Card className="card-pad">
        <h2 className="card-title mb-3">{t("hr.sec.leaveTypes")}</h2>
        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end">
          <Input placeholder={t("common.code")} value={leaveType.code} onChange={(e) => setLeaveType({ ...leaveType, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={leaveType.nameAr} onChange={(e) => setLeaveType({ ...leaveType, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={leaveType.nameEn} onChange={(e) => setLeaveType({ ...leaveType, nameEn: e.target.value })} />
          <Input placeholder={t("hr.annualDays")} value={leaveType.annualDays} onChange={(e) => setLeaveType({ ...leaveType, annualDays: e.target.value })} />
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={leaveType.isPaid} onChange={(e) => setLeaveType({ ...leaveType, isPaid: e.target.checked })} />
            {t("hr.isPaid")}
          </label>
          <Button
            disabled={!leaveType.code || createLeaveType.isPending}
            onClick={() => createLeaveType.mutate({
              code: leaveType.code, nameAr: leaveType.nameAr || undefined, nameEn: leaveType.nameEn || undefined,
              isPaid: leaveType.isPaid, annualEntitlementDays: Number(leaveType.annualDays) || 0
            })}
          >{t("common.add")}</Button>
        </div>
        <table className="table mt-3">
          <thead><tr><th>{t("common.code")}</th><th>{t("common.nameAr")}</th><th>{t("hr.isPaid")}</th><th>{t("hr.annualDays")}</th></tr></thead>
          <tbody>
            {leaveTypes.data?.map((lt) => (
              <tr key={lt.id} className="border-b border-line last:border-0">
                <td className="ltr-nums">{lt.code}</td><td>{lt.nameAr}</td>
                <td>{lt.isPaid ? t("common.yes") : t("common.no")}</td><td className="ltr-nums">{lt.annualEntitlementDays}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Pay components: allowances, incentives, bonuses, commissions, deductions. */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.sec.payComponents")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.payComponentsHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-8 gap-2 items-end">
          <Input placeholder={t("common.code")} value={component.code} onChange={(e) => setComponent({ ...component, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={component.nameAr} onChange={(e) => setComponent({ ...component, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={component.nameEn} onChange={(e) => setComponent({ ...component, nameEn: e.target.value })} />
          <Select value={component.kind} onChange={(e) => setComponent({ ...component, kind: e.target.value as PayComponentKind })}>
            {(["Allowance", "Incentive", "Bonus", "Commission", "Deduction"] as PayComponentKind[]).map((k) => (
              <option key={k} value={k}>{t(`hr.kind.${k}`)}</option>
            ))}
          </Select>
          <Select value={component.calculation} onChange={(e) => setComponent({ ...component, calculation: e.target.value as PayComponentCalculation })}>
            {(["FixedAmount", "PercentOfBasic", "PercentOfGross", "PerAttendanceDay", "PerOvertimeHour"] as PayComponentCalculation[]).map((c) => (
              <option key={c} value={c}>{t(`hr.calc.${c}`)}</option>
            ))}
          </Select>
          <Input placeholder={t("hr.defaultAmount")} value={component.amount} onChange={(e) => setComponent({ ...component, amount: e.target.value })} />
          <Input placeholder={t("hr.defaultPercent")} value={component.percent} onChange={(e) => setComponent({ ...component, percent: e.target.value })} />
          <Button
            disabled={!component.code || createComponent.isPending}
            onClick={() => createComponent.mutate({
              code: component.code, nameAr: component.nameAr || undefined, nameEn: component.nameEn || undefined,
              kind: component.kind, calculation: component.calculation,
              defaultAmount: Number(component.amount) || 0, defaultPercent: Number(component.percent) || 0
            })}
          >{t("common.add")}</Button>
        </div>
        <table className="table mt-3">
          <thead><tr>
            <th>{t("common.code")}</th><th>{t("common.nameAr")}</th><th>{t("hr.kind")}</th><th>{t("hr.calculation")}</th>
            <th>{t("hr.defaultAmount")}</th><th>{t("hr.defaultPercent")}</th><th>{t("hr.taxable")}</th><th>{t("hr.insured")}</th>
          </tr></thead>
          <tbody>
            {components.data?.map((c) => (
              <tr key={c.id} className="border-b border-line last:border-0">
                <td className="ltr-nums">{c.code}</td><td>{c.nameAr}</td>
                <td>{t(`hr.kind.${c.kind}`)}</td><td>{t(`hr.calc.${c.calculation}`)}</td>
                <td className="ltr-nums">{c.defaultAmount}</td><td className="ltr-nums">{c.defaultPercent}</td>
                <td>{c.isTaxable ? t("common.yes") : t("common.no")}</td>
                <td>{c.isInsured ? t("common.yes") : t("common.no")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Tax & insurance rules */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.sec.contributions")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.contributionsHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-8 gap-2 items-end">
          <Input placeholder={t("common.code")} value={contribution.code} onChange={(e) => setContribution({ ...contribution, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={contribution.nameAr} onChange={(e) => setContribution({ ...contribution, nameAr: e.target.value })} />
          <Select value={contribution.kind} onChange={(e) => setContribution({ ...contribution, kind: e.target.value as ContributionKind })}>
            <option value="Tax">{t("hr.contributionKind.Tax")}</option>
            <option value="Insurance">{t("hr.contributionKind.Insurance")}</option>
          </Select>
          <Select value={contribution.base} onChange={(e) => setContribution({ ...contribution, base: e.target.value as ContributionBase })}>
            <option value="BasicSalary">{t("hr.base.BasicSalary")}</option>
            <option value="GrossPay">{t("hr.base.GrossPay")}</option>
          </Select>
          <Input placeholder={t("hr.employeeRate")} value={contribution.employee} onChange={(e) => setContribution({ ...contribution, employee: e.target.value })} />
          <Input placeholder={t("hr.employerRate")} value={contribution.employer} onChange={(e) => setContribution({ ...contribution, employer: e.target.value })} />
          <Button
            disabled={!contribution.code || createContribution.isPending}
            onClick={() => createContribution.mutate({
              code: contribution.code, nameAr: contribution.nameAr || undefined, nameEn: contribution.nameEn || undefined,
              kind: contribution.kind, base: contribution.base,
              employeeRatePercent: Number(contribution.employee) || 0, employerRatePercent: Number(contribution.employer) || 0
            })}
          >{t("common.add")}</Button>
        </div>
        <table className="table mt-3">
          <thead><tr>
            <th>{t("common.code")}</th><th>{t("common.nameAr")}</th><th>{t("hr.contributionKind")}</th>
            <th>{t("hr.base")}</th><th>{t("hr.employeeRate")}</th><th>{t("hr.employerRate")}</th>
          </tr></thead>
          <tbody>
            {contributions.data?.map((c) => (
              <tr key={c.id} className="border-b border-line last:border-0">
                <td className="ltr-nums">{c.code}</td><td>{c.nameAr}</td>
                <td>{t(`hr.contributionKind.${c.kind}`)}</td><td>{t(`hr.base.${c.base}`)}</td>
                <td className="ltr-nums">{c.employeeRatePercent}%</td><td className="ltr-nums">{c.employerRatePercent}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Accounting mappings */}
      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.sec.accounts")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.accountsHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-2 items-end">
          <Select value={mapping.kind} onChange={(e) => setMapping({ ...mapping, kind: e.target.value as PayrollAccountKind })}>
            {(["BasicSalary", "Allowance", "Deduction", "Tax", "Insurance", "Advance", "NetPay"] as PayrollAccountKind[]).map((k) => (
              <option key={k} value={k}>{t(`hr.accountKind.${k}`)}</option>
            ))}
          </Select>
          <Input placeholder={t("hr.accountCode")} value={mapping.accountCode} onChange={(e) => setMapping({ ...mapping, accountCode: e.target.value })} />
          <Input placeholder={t("hr.accountName")} value={mapping.accountName} onChange={(e) => setMapping({ ...mapping, accountName: e.target.value })} />
          <Button
            disabled={!mapping.accountCode || !mapping.accountName || upsertMapping.isPending}
            onClick={() => upsertMapping.mutate(mapping)}
          >{t("common.save")}</Button>
        </div>
        <table className="table mt-3">
          <thead><tr><th>{t("hr.accountKind")}</th><th>{t("hr.accountCode")}</th><th>{t("hr.accountName")}</th></tr></thead>
          <tbody>
            {mappings.data?.map((m) => (
              <tr key={m.id} className="border-b border-line last:border-0">
                <td>{t(`hr.accountKind.${m.kind}`)}</td><td className="ltr-nums">{m.accountCode}</td><td>{m.accountName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {/* Organisation: cost centers, sections, jobs */}
      <Card className="card-pad">
        <h2 className="card-title mb-3">{t("hr.sec.org")}</h2>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-2 items-end mb-4">
          <Input placeholder={t("common.code")} value={costCenter.code} onChange={(e) => setCostCenter({ ...costCenter, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={costCenter.nameAr} onChange={(e) => setCostCenter({ ...costCenter, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={costCenter.nameEn} onChange={(e) => setCostCenter({ ...costCenter, nameEn: e.target.value })} />
          <Button disabled={!costCenter.code || createCostCenter.isPending} onClick={() => createCostCenter.mutate(costCenter)}>{t("hr.addCostCenter")}</Button>
        </div>
        <div className="grid grid-cols-2 md:grid-cols-5 gap-2 items-end mb-4">
          <Select value={section.departmentId} onChange={(e) => setSection({ ...section, departmentId: e.target.value })}>
            <option value="">{t("common.department")}</option>
            {departments.data?.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </Select>
          <Input placeholder={t("common.code")} value={section.code} onChange={(e) => setSection({ ...section, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={section.nameAr} onChange={(e) => setSection({ ...section, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={section.nameEn} onChange={(e) => setSection({ ...section, nameEn: e.target.value })} />
          <Button
            disabled={!section.departmentId || !section.code || createSection.isPending}
            onClick={() => createSection.mutate({ ...section, nameAr: section.nameAr || undefined, nameEn: section.nameEn || undefined })}
          >{t("hr.addSection")}</Button>
        </div>
        <div className="grid grid-cols-2 md:grid-cols-5 gap-2 items-end">
          <Input placeholder={t("common.code")} value={job.code} onChange={(e) => setJob({ ...job, code: e.target.value })} />
          <Input placeholder={t("common.nameAr")} value={job.nameAr} onChange={(e) => setJob({ ...job, nameAr: e.target.value })} />
          <Input placeholder={t("common.nameEn")} value={job.nameEn} onChange={(e) => setJob({ ...job, nameEn: e.target.value })} />
          <Input placeholder={t("hr.defaultSalary")} value={job.defaultSalary} onChange={(e) => setJob({ ...job, defaultSalary: e.target.value })} />
          <Button
            disabled={!job.code || createJob.isPending}
            onClick={() => createJob.mutate({
              code: job.code, nameAr: job.nameAr || undefined, nameEn: job.nameEn || undefined,
              departmentId: job.departmentId || undefined,
              defaultBasicSalary: job.defaultSalary ? Number(job.defaultSalary) : undefined
            })}
          >{t("hr.addJob")}</Button>
        </div>
        {/* Employee classification & pay component assignment */}
        <h3 className="card-title mt-6 mb-1">{t("hr.sec.employeeProfile")}</h3>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.sec.employeeProfileHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-7 gap-2 items-end">
          <Select value={profile.employeeId} onChange={(e) => pickEmployee(e.target.value)}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Select value={profile.hrSectionId} onChange={(e) => setProfile({ ...profile, hrSectionId: e.target.value })}>
            <option value="">{t("hr.section")}</option>
            {sections.data?.map((s) => <option key={s.id} value={s.id}>{s.nameAr}</option>)}
          </Select>
          <Select value={profile.jobPositionId} onChange={(e) => setProfile({ ...profile, jobPositionId: e.target.value })}>
            <option value="">{t("hr.job")}</option>
            {jobs.data?.map((j) => <option key={j.id} value={j.id}>{j.nameAr}</option>)}
          </Select>
          <Select value={profile.costCenterId} onChange={(e) => setProfile({ ...profile, costCenterId: e.target.value })}>
            <option value="">{t("hr.costCenter")}</option>
            {costCenters.data?.map((c) => <option key={c.id} value={c.id}>{c.nameAr}</option>)}
          </Select>
          <Select value={profile.hrShiftId} onChange={(e) => setProfile({ ...profile, hrShiftId: e.target.value })}>
            <option value="">{t("hr.attendanceShift")}</option>
            {shifts.data?.map((s) => <option key={s.id} value={s.id}>{s.nameAr}</option>)}
          </Select>
          <div className="flex flex-col gap-1 text-sm">
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={profile.taxExempt}
                onChange={(e) => setProfile({ ...profile, taxExempt: e.target.checked })} />
              {t("hr.taxExempt")}
            </label>
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={profile.insuranceExempt}
                onChange={(e) => setProfile({ ...profile, insuranceExempt: e.target.checked })} />
              {t("hr.insuranceExempt")}
            </label>
          </div>
          <Button disabled={!profile.employeeId || saveProfile.isPending} onClick={() => saveProfile.mutate()}>
            {t("common.save")}
          </Button>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end mt-4">
          <Select value={assign.employeeId} onChange={(e) => setAssign({ ...assign, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Select value={assign.payComponentId} onChange={(e) => setAssign({ ...assign, payComponentId: e.target.value })}>
            <option value="">{t("hr.component")}</option>
            {components.data?.map((c) => <option key={c.id} value={c.id}>{c.nameAr} ({t(`hr.calc.${c.calculation}`)})</option>)}
          </Select>
          <Input placeholder={t("hr.amount")} value={assign.amount} onChange={(e) => setAssign({ ...assign, amount: e.target.value })} />
          <Input placeholder={t("hr.defaultPercent")} value={assign.percent} onChange={(e) => setAssign({ ...assign, percent: e.target.value })} />
          <Input type="date" value={assign.effectiveFrom} onChange={(e) => setAssign({ ...assign, effectiveFrom: e.target.value })} />
          <Button
            disabled={!assign.employeeId || !assign.payComponentId || assignComponent.isPending}
            onClick={() => assignComponent.mutate()}
          >{t("hr.assignComponent")}</Button>
        </div>

        <table className="table mt-3">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.component")}</th><th>{t("hr.amount")}</th>
            <th>{t("hr.defaultPercent")}</th><th>{t("hr.effectiveFrom")}</th><th>{t("common.actions")}</th>
          </tr></thead>
          <tbody>
            {assignments.data?.map((a) => (
              <tr key={a.id} className="border-b border-line last:border-0">
                <td>{a.employeeCode} - {a.employeeName}</td>
                <td>{a.payComponentName}</td>
                <td className="ltr-nums">{a.amount ?? "—"}</td>
                <td className="ltr-nums">{a.percent ?? "—"}</td>
                <td className="ltr-nums">{a.effectiveFrom.slice(0, 10)}</td>
                <td>
                  <Button variant="ghost" onClick={() => removeAssignment.mutate(a.id)}>{t("hr.deactivate")}</Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mt-4 text-sm">
          <div>
            <div className="font-medium text-ink mb-1">{t("hr.costCenters")}</div>
            {costCenters.data?.map((c) => <div key={c.id} className="text-ink-muted">{c.code} - {c.nameAr}</div>)}
          </div>
          <div>
            <div className="font-medium text-ink mb-1">{t("hr.sections")}</div>
            {sections.data?.map((s) => <div key={s.id} className="text-ink-muted">{s.code} - {s.nameAr} ({s.departmentName})</div>)}
          </div>
          <div>
            <div className="font-medium text-ink mb-1">{t("hr.jobs")}</div>
            {jobs.data?.map((j) => <div key={j.id} className="text-ink-muted">{j.code} - {j.nameAr}</div>)}
          </div>
        </div>
      </Card>
    </div>
  );
}

// ----------------------------------------------------------------- attendance

function AttendanceTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [from, setFrom] = useState(monthStart(now.getFullYear(), now.getMonth() + 1));
  const [to, setTo] = useState(monthEnd(now.getFullYear(), now.getMonth() + 1));
  const [error, setError] = useState<string | null>(null);
  const [generated, setGenerated] = useState<number | null>(null);

  const employees = useEmployees();
  const attendance = useQuery({
    queryKey: ["hr", "attendance", from, to],
    queryFn: () => HrApi.attendance({ fromDate: from, toDate: to })
  });
  const summary = useQuery({
    queryKey: ["hr", "attendance-summary", from, to],
    queryFn: () => HrApi.attendanceSummary({ fromDate: from, toDate: to })
  });

  const [record, setRecord] = useState({ employeeId: "", workDate: today, checkIn: "08:00", checkOut: "16:00" });
  const [nonWorking, setNonWorking] = useState<{ employeeId: string; workDate: string; status: AttendanceStatus }>(
    { employeeId: "", workDate: today, status: "Absent" });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["hr", "attendance"] });
    qc.invalidateQueries({ queryKey: ["hr", "attendance-summary"] });
  };
  const onError = (err: unknown) => setError(errorMessage(err, t("hr.saveFailed")));

  const recordMutation = useMutation({ mutationFn: HrApi.recordAttendance, onSuccess: () => { invalidate(); setError(null); }, onError });
  const nonWorkingMutation = useMutation({ mutationFn: HrApi.recordNonWorking, onSuccess: () => { invalidate(); setError(null); }, onError });
  const generateMutation = useMutation({
    mutationFn: HrApi.generateAttendance,
    onSuccess: (r) => { invalidate(); setGenerated(r.created); setError(null); },
    onError
  });
  const removeMutation = useMutation({ mutationFn: HrApi.removeAttendance, onSuccess: () => { invalidate(); setError(null); }, onError });

  const hours = (minutes: number) => Math.round((minutes / 60) * 100) / 100;

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.attendance.record")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.attendance.recordHint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end">
          <Select value={record.employeeId} onChange={(e) => setRecord({ ...record, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Input type="date" value={record.workDate} onChange={(e) => setRecord({ ...record, workDate: e.target.value })} />
          <Input type="time" value={record.checkIn} onChange={(e) => setRecord({ ...record, checkIn: e.target.value })} />
          <Input type="time" value={record.checkOut} onChange={(e) => setRecord({ ...record, checkOut: e.target.value })} />
          <Button
            disabled={!record.employeeId || recordMutation.isPending}
            onClick={() => recordMutation.mutate({
              employeeId: record.employeeId, workDate: record.workDate,
              checkInMinutes: toMinutes(record.checkIn), checkOutMinutes: toMinutes(record.checkOut)
            })}
          >{t("hr.attendance.save")}</Button>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-5 gap-2 items-end mt-4">
          <Select value={nonWorking.employeeId} onChange={(e) => setNonWorking({ ...nonWorking, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Input type="date" value={nonWorking.workDate} onChange={(e) => setNonWorking({ ...nonWorking, workDate: e.target.value })} />
          <Select value={nonWorking.status} onChange={(e) => setNonWorking({ ...nonWorking, status: e.target.value as AttendanceStatus })}>
            {(["Absent", "RestDay", "Holiday"] as AttendanceStatus[]).map((s) => (
              <option key={s} value={s}>{t(`hr.attendance.status.${s}`)}</option>
            ))}
          </Select>
          <Button
            variant="secondary"
            disabled={!nonWorking.employeeId || nonWorkingMutation.isPending}
            onClick={() => nonWorkingMutation.mutate(nonWorking)}
          >{t("hr.attendance.saveNonWorking")}</Button>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end mt-4">
          <Input type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} />
          <Input type="number" min={1} max={12} value={month} onChange={(e) => setMonth(Number(e.target.value))} />
          <Button
            variant="secondary"
            disabled={generateMutation.isPending}
            onClick={() => generateMutation.mutate({ periodYear: year, periodMonth: month })}
          >{t("hr.attendance.generate")}</Button>
          {generated !== null && <span className="text-sm text-ink-muted">{t("hr.attendance.generated")}: {generated}</span>}
        </div>
      </Card>

      <Card className="card-pad">
        <div className="flex flex-wrap items-end gap-2 mb-3">
          <div><label className="field-label">{t("hr.from")}</label>
            <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
          <div><label className="field-label">{t("hr.to")}</label>
            <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
        </div>
        <h2 className="card-title mb-2">{t("hr.attendance.summary")}</h2>
        <table className="table mb-6">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.attendance.present")}</th><th>{t("hr.attendance.late")}</th>
            <th>{t("hr.attendance.absent")}</th><th>{t("hr.attendance.onLeave")}</th>
            <th>{t("hr.attendance.lateMinutes")}</th><th>{t("hr.attendance.overtime")}</th><th>{t("hr.attendance.workedHours")}</th>
          </tr></thead>
          <tbody>
            {summary.data?.map((s) => (
              <tr key={s.employeeId} className="border-b border-line last:border-0">
                <td>{s.employeeCode} - {s.employeeName}</td>
                <td className="ltr-nums">{s.presentDays}</td><td className="ltr-nums">{s.lateDays}</td>
                <td className="ltr-nums">{s.absentDays}</td><td className="ltr-nums">{s.leaveDays}</td>
                <td className="ltr-nums">{s.lateMinutes}</td><td className="ltr-nums">{hours(s.overtimeMinutes)}</td>
                <td className="ltr-nums">{s.workedHours}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <h2 className="card-title mb-2">{t("hr.attendance.log")}</h2>
        <table className="table">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.workDate")}</th><th>{t("hr.shift")}</th>
            <th>{t("hr.checkIn")}</th><th>{t("hr.checkOut")}</th><th>{t("hr.attendance.statusLabel")}</th>
            <th>{t("hr.attendance.lateMinutes")}</th><th>{t("hr.attendance.overtime")}</th><th>{t("common.actions")}</th>
          </tr></thead>
          <tbody>
            {attendance.data?.slice(0, 200).map((a) => (
              <tr key={a.id} className="border-b border-line last:border-0">
                <td>{a.employeeCode} - {a.employeeName}</td>
                <td className="ltr-nums">{a.workDate.slice(0, 10)}</td>
                <td>{a.shiftName ?? "—"}</td>
                <td className="ltr-nums">{a.checkInMinutes != null ? fromMinutes(a.checkInMinutes) : "—"}</td>
                <td className="ltr-nums">{a.checkOutMinutes != null ? fromMinutes(a.checkOutMinutes) : "—"}</td>
                <td><Badge tone={a.status === "Absent" ? "red" : a.status === "Late" ? "yellow" : "green"}>
                  {t(`hr.attendance.status.${a.status}`)}</Badge></td>
                <td className="ltr-nums">{a.lateMinutes}</td>
                <td className="ltr-nums">{hours(a.overtimeMinutes)}</td>
                <td>
                  <Button variant="ghost" onClick={() => removeMutation.mutate(a.id)}>{t("common.delete")}</Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------------- leave

function LeaveTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const employees = useEmployees();
  const leaveTypes = useQuery({ queryKey: ["hr", "leave-types"], queryFn: () => HrApi.leaveTypes({ activeOnly: true }) });
  const requests = useQuery({ queryKey: ["hr", "leave-requests"], queryFn: () => HrApi.leaveRequests() });
  const [error, setError] = useState<string | null>(null);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [form, setForm] = useState({ employeeId: "", leaveTypeId: "", fromDate: today, toDate: today, days: "1", reason: "" });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["hr", "leave-requests"] });
    qc.invalidateQueries({ queryKey: ["hr", "attendance"] });
    qc.invalidateQueries({ queryKey: ["hr", "attendance-summary"] });
  };
  const onError = (err: unknown) => setError(errorMessage(err, t("hr.saveFailed")));

  const submit = useMutation({ mutationFn: HrApi.submitLeave, onSuccess: () => { invalidate(); setError(null); }, onError });
  const approve = useMutation({ mutationFn: (id: string) => HrApi.approveLeave(id), onSuccess: () => { invalidate(); setError(null); }, onError });
  const reject = useMutation({
    mutationFn: (v: { id: string; reason: string }) => HrApi.rejectLeave(v.id, v.reason),
    onSuccess: () => { invalidate(); setRejectingId(null); setRejectReason(""); setError(null); }, onError
  });
  const cancel = useMutation({ mutationFn: (id: string) => HrApi.cancelLeave(id, "cancelled"), onSuccess: () => { invalidate(); }, onError });

  const statusTone: Record<LeaveStatus, "yellow" | "green" | "red" | "gray"> = {
    Pending: "yellow", Approved: "green", Rejected: "red", Cancelled: "gray"
  };

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.leave.new")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.leave.sod")}</p>
        <div className="grid grid-cols-2 md:grid-cols-7 gap-2 items-end">
          <Select value={form.employeeId} onChange={(e) => setForm({ ...form, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Select value={form.leaveTypeId} onChange={(e) => setForm({ ...form, leaveTypeId: e.target.value })}>
            <option value="">{t("hr.leaveType")}</option>
            {leaveTypes.data?.map((lt) => <option key={lt.id} value={lt.id}>{lt.nameAr}{lt.isPaid ? "" : ` (${t("hr.unpaid")})`}</option>)}
          </Select>
          <Input type="date" value={form.fromDate} onChange={(e) => setForm({ ...form, fromDate: e.target.value })} />
          <Input type="date" value={form.toDate} onChange={(e) => setForm({ ...form, toDate: e.target.value })} />
          <Input placeholder={t("hr.days")} value={form.days} onChange={(e) => setForm({ ...form, days: e.target.value })} />
          <Input placeholder={t("hr.reason")} value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
          <Button
            disabled={!form.employeeId || !form.leaveTypeId || !form.reason || submit.isPending}
            onClick={() => submit.mutate({
              employeeId: form.employeeId, leaveTypeId: form.leaveTypeId,
              fromDate: form.fromDate, toDate: form.toDate, days: Number(form.days) || 1, reason: form.reason
            })}
          >{t("hr.submit")}</Button>
        </div>
      </Card>

      <Card className="card-pad">
        <table className="table">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.leaveType")}</th><th>{t("hr.from")}</th><th>{t("hr.to")}</th>
            <th>{t("hr.days")}</th><th>{t("hr.reason")}</th><th>{t("common.status")}</th><th>{t("common.actions")}</th>
          </tr></thead>
          <tbody>
            {requests.data?.map((l) => (
              <tr key={l.id} className="border-b border-line last:border-0 align-top">
                <td>{l.employeeCode} - {l.employeeName}</td>
                <td>{l.leaveTypeName}</td>
                <td className="ltr-nums">{l.fromDate.slice(0, 10)}</td>
                <td className="ltr-nums">{l.toDate.slice(0, 10)}</td>
                <td className="ltr-nums">{l.days}</td>
                <td className="text-ink-muted max-w-xs">{l.reason}</td>
                <td><Badge tone={statusTone[l.status]}>{t(`hr.status.${l.status}`)}</Badge>
                  {l.decisionReason && <div className="text-xs text-ink-subtle">{l.decisionReason}</div>}</td>
                <td>
                  {l.status === "Pending" ? (
                    rejectingId === l.id ? (
                      <div className="flex flex-col gap-2 min-w-[200px]">
                        <Input autoFocus placeholder={t("hr.rejectReason")} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
                        <div className="flex gap-2">
                          <Button variant="secondary" disabled={!rejectReason.trim() || reject.isPending}
                            onClick={() => reject.mutate({ id: l.id, reason: rejectReason.trim() })}>{t("hr.reject")}</Button>
                          <Button variant="ghost" onClick={() => { setRejectingId(null); setRejectReason(""); }}>{t("common.cancel")}</Button>
                        </div>
                      </div>
                    ) : (
                      <div className="flex gap-2">
                        <Button disabled={approve.isPending} onClick={() => approve.mutate(l.id)}>{t("hr.approve")}</Button>
                        <Button variant="secondary" onClick={() => { setRejectingId(l.id); setRejectReason(""); }}>{t("hr.reject")}</Button>
                      </div>
                    )
                  ) : l.status === "Approved" ? (
                    <Button variant="ghost" onClick={() => cancel.mutate(l.id)}>{t("hr.cancelLeave")}</Button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="text-2xs text-ink-subtle mt-2">{t("hr.leave.selfApprovalBlocked")}</p>
      </Card>
    </div>
  );
}

// ------------------------------------------------------------------- overtime

function OvertimeTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const employees = useEmployees();
  const entries = useQuery({ queryKey: ["hr", "overtime"], queryFn: () => HrApi.overtime() });
  const [error, setError] = useState<string | null>(null);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [form, setForm] = useState({ employeeId: "", workDate: today, hours: "1", category: "Normal" as OvertimeCategory, reason: "" });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["hr", "overtime"] });
  const onError = (err: unknown) => setError(errorMessage(err, t("hr.saveFailed")));

  const submit = useMutation({ mutationFn: HrApi.submitOvertime, onSuccess: () => { invalidate(); setError(null); }, onError });
  const approve = useMutation({ mutationFn: HrApi.approveOvertime, onSuccess: () => { invalidate(); setError(null); }, onError });
  const reject = useMutation({
    mutationFn: (v: { id: string; reason: string }) => HrApi.rejectOvertime(v.id, v.reason),
    onSuccess: () => { invalidate(); setRejectingId(null); setRejectReason(""); setError(null); }, onError
  });

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.overtime.new")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.overtime.sod")}</p>
        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end">
          <Select value={form.employeeId} onChange={(e) => setForm({ ...form, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Input type="date" value={form.workDate} onChange={(e) => setForm({ ...form, workDate: e.target.value })} />
          <Input placeholder={t("hr.hours")} value={form.hours} onChange={(e) => setForm({ ...form, hours: e.target.value })} />
          <Select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value as OvertimeCategory })}>
            {(["Normal", "Weekend", "Holiday"] as OvertimeCategory[]).map((c) => (
              <option key={c} value={c}>{t(`hr.overtimeCategory.${c}`)}</option>
            ))}
          </Select>
          <Input placeholder={t("hr.reason")} value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
          <Button
            disabled={!form.employeeId || !form.reason || submit.isPending}
            onClick={() => submit.mutate({
              employeeId: form.employeeId, workDate: form.workDate, hours: Number(form.hours) || 0,
              category: form.category, reason: form.reason
            })}
          >{t("hr.submit")}</Button>
        </div>
      </Card>

      <Card className="card-pad">
        <table className="table">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.workDate")}</th><th>{t("hr.hours")}</th>
            <th>{t("hr.category")}</th><th>{t("hr.multiplier")}</th><th>{t("hr.reason")}</th>
            <th>{t("common.status")}</th><th>{t("common.actions")}</th>
          </tr></thead>
          <tbody>
            {entries.data?.map((o) => (
              <tr key={o.id} className="border-b border-line last:border-0 align-top">
                <td>{o.employeeCode} - {o.employeeName}</td>
                <td className="ltr-nums">{o.workDate.slice(0, 10)}</td>
                <td className="ltr-nums">{o.hours}</td>
                <td>{t(`hr.overtimeCategory.${o.category}`)}</td>
                <td className="ltr-nums">{o.approvedMultiplier || "—"}</td>
                <td className="text-ink-muted max-w-xs">{o.reason}</td>
                <td><Badge tone={o.status === "Approved" ? "green" : o.status === "Rejected" ? "red" : "yellow"}>
                  {t(`hr.status.${o.status}`)}</Badge>
                  {o.payrollRunId && <div className="text-xs text-ink-subtle">{t("hr.overtime.paid")}</div>}
                  {o.decisionReason && <div className="text-xs text-ink-subtle">{o.decisionReason}</div>}</td>
                <td>
                  {o.status === "Pending" ? (
                    rejectingId === o.id ? (
                      <div className="flex flex-col gap-2 min-w-[200px]">
                        <Input autoFocus placeholder={t("hr.rejectReason")} value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} />
                        <div className="flex gap-2">
                          <Button variant="secondary" disabled={!rejectReason.trim() || reject.isPending}
                            onClick={() => reject.mutate({ id: o.id, reason: rejectReason.trim() })}>{t("hr.reject")}</Button>
                          <Button variant="ghost" onClick={() => { setRejectingId(null); setRejectReason(""); }}>{t("common.cancel")}</Button>
                        </div>
                      </div>
                    ) : (
                      <div className="flex gap-2">
                        <Button disabled={approve.isPending} onClick={() => approve.mutate(o.id)}>{t("hr.approve")}</Button>
                        <Button variant="secondary" onClick={() => { setRejectingId(o.id); setRejectReason(""); }}>{t("hr.reject")}</Button>
                      </div>
                    )
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
}

// ------------------------------------------------------------------- advances

function AdvancesTab() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const employees = useEmployees();
  const advances = useQuery({ queryKey: ["hr", "advances"], queryFn: () => HrApi.advances() });
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ employeeId: "", advanceDate: today, amount: "", instalments: "1", reason: "" });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["hr", "advances"] });
  const onError = (err: unknown) => setError(errorMessage(err, t("hr.saveFailed")));

  const create = useMutation({ mutationFn: HrApi.createAdvance, onSuccess: () => { invalidate(); setError(null); }, onError });
  const cancel = useMutation({
    mutationFn: (id: string) => HrApi.cancelAdvance(id, "cancelled by HR"),
    onSuccess: () => invalidate(), onError
  });

  const tone: Record<AdvanceStatus, "green" | "gray" | "red"> = { Active: "green", Closed: "gray", Cancelled: "red" };

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      <Card className="card-pad">
        <h2 className="card-title mb-1">{t("hr.advances.new")}</h2>
        <p className="text-xs text-ink-subtle mb-3">{t("hr.advances.hint")}</p>
        <div className="grid grid-cols-2 md:grid-cols-6 gap-2 items-end">
          <Select value={form.employeeId} onChange={(e) => setForm({ ...form, employeeId: e.target.value })}>
            <option value="">{t("hr.employee")}</option>
            {employees.data?.map((e) => <option key={e.id} value={e.id}>{e.code} - {e.name}</option>)}
          </Select>
          <Input type="date" value={form.advanceDate} onChange={(e) => setForm({ ...form, advanceDate: e.target.value })} />
          <Input placeholder={t("hr.amount")} value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
          <Input placeholder={t("hr.instalments")} value={form.instalments} onChange={(e) => setForm({ ...form, instalments: e.target.value })} />
          <Input placeholder={t("hr.reason")} value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
          <Button
            disabled={!form.employeeId || !form.amount || !form.reason || create.isPending}
            onClick={() => create.mutate({
              employeeId: form.employeeId, advanceDate: form.advanceDate,
              amount: Number(form.amount) || 0, instalmentCount: Number(form.instalments) || 1, reason: form.reason
            })}
          >{t("common.add")}</Button>
        </div>
      </Card>

      <Card className="card-pad">
        <table className="table">
          <thead><tr>
            <th>{t("hr.employee")}</th><th>{t("hr.date")}</th><th>{t("hr.amount")}</th>
            <th>{t("hr.instalmentAmount")}</th><th>{t("hr.recovered")}</th><th>{t("hr.remaining")}</th>
            <th>{t("common.status")}</th><th>{t("common.actions")}</th>
          </tr></thead>
          <tbody>
            {advances.data?.map((a) => (
              <tr key={a.id} className="border-b border-line last:border-0">
                <td>{a.employeeCode} - {a.employeeName}</td>
                <td className="ltr-nums">{a.advanceDate.slice(0, 10)}</td>
                <td className="ltr-nums">{a.amount}</td>
                <td className="ltr-nums">{a.instalmentAmount}</td>
                <td className="ltr-nums">{a.recoveredAmount}</td>
                <td className="ltr-nums">{a.remainingAmount}</td>
                <td><Badge tone={tone[a.status]}>{t(`hr.advanceStatus.${a.status}`)}</Badge></td>
                <td>
                  {a.status === "Active" && (
                    <Button variant="ghost" onClick={() => cancel.mutate(a.id)}>{t("common.cancel")}</Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------- labor cost

function LaborCostTab() {
  const { t } = useI18n();
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [groupByCostCenter, setGroupByCostCenter] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const report = useQuery({
    queryKey: ["hr", "labor-cost", year, month, groupByCostCenter],
    queryFn: () => HrApi.laborCost({ periodYear: year, periodMonth: month, groupByCostCenter })
  });

  const exportFile = async (format: "excel" | "pdf") => {
    setError(null);
    try {
      await downloadFile("/hr/labor-cost/export", `labor-cost-${year}${String(month).padStart(2, "0")}.${format === "pdf" ? "pdf" : "xlsx"}`, {
        periodYear: year, periodMonth: month, groupByCostCenter, format
      });
    } catch (err) {
      setError(errorMessage(err, t("hr.saveFailed")));
    }
  };

  return (
    <div className="space-y-6">
      {error && <p className="form-error">{error}</p>}

      <Card className="card-pad">
        <div className="flex flex-wrap items-end gap-3">
          <div><label className="field-label">{t("hr.year")}</label>
            <Input type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} /></div>
          <div><label className="field-label">{t("hr.month")}</label>
            <Input type="number" min={1} max={12} value={month} onChange={(e) => setMonth(Number(e.target.value))} /></div>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={groupByCostCenter} onChange={(e) => setGroupByCostCenter(e.target.checked)} />
            {t("hr.groupByCostCenter")}
          </label>
          <Button variant="secondary" onClick={() => void exportFile("excel")}>{t("hr.exportExcel")}</Button>
          <Button variant="secondary" onClick={() => void exportFile("pdf")}>{t("hr.exportPdf")}</Button>
        </div>
      </Card>

      <Card className="card-pad">
        <table className="table">
          <thead><tr>
            <th>{t("hr.group")}</th><th>{t("hr.employees")}</th><th>{t("hr.basic")}</th><th>{t("hr.allowances")}</th>
            <th>{t("hr.overtime")}</th><th>{t("hr.deductions")}</th><th>{t("hr.tax")}</th><th>{t("hr.insurance")}</th>
            <th>{t("hr.advances")}</th><th>{t("hr.gross")}</th><th>{t("hr.net")}</th><th>{t("hr.employerCost")}</th>
          </tr></thead>
          <tbody>
            {report.data?.rows.length === 0 && (
              <tr><td colSpan={12} className="px-4 py-6 text-center text-ink-subtle">{t("hr.noPayrollRun")}</td></tr>
            )}
            {report.data?.rows.map((row: LaborCostRow) => (
              <tr key={row.groupKey} className="border-b border-line last:border-0">
                <td>{row.costCenterName ?? row.departmentName ?? t("hr.unassigned")}</td>
                <td className="ltr-nums">{row.employeeCount}</td>
                <td className="ltr-nums">{row.basicSalary}</td><td className="ltr-nums">{row.allowances}</td>
                <td className="ltr-nums">{row.overtime}</td><td className="ltr-nums">{row.deductions}</td>
                <td className="ltr-nums">{row.tax}</td><td className="ltr-nums">{row.insurance}</td>
                <td className="ltr-nums">{row.advances}</td><td className="ltr-nums">{row.gross}</td>
                <td className="ltr-nums">{row.net}</td><td className="ltr-nums font-medium">{row.employerCost}</td>
              </tr>
            ))}
            {report.data && report.data.rows.length > 0 && (
              <tr className="font-semibold">
                <td>{t("hr.total")}</td><td className="ltr-nums">{report.data.rows.reduce((s, r) => s + r.employeeCount, 0)}</td>
                <td className="ltr-nums">{report.data.totalBasicSalary}</td><td />
                <td /><td /><td /><td /><td />
                <td className="ltr-nums">{report.data.totalGross}</td>
                <td className="ltr-nums">{report.data.totalNet}</td>
                <td className="ltr-nums">{report.data.totalEmployerCost}</td>
              </tr>
            )}
          </tbody>
        </table>
      </Card>
    </div>
  );
}

/** Re-exported for pages that need the employee type when building pickers. */
export type { Employee };
