import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UsersApi, type User } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge, Select } from "@/components/ui";
import { PermissionGroups, Permissions } from "@/permissions";

/**
 * Users and permissions (spec section 41).
 *
 * One flat permission list per login, exactly as the backend stores it
 * (User.Roles) - no Role table is introduced here. What changed is that the
 * picker now exposes every permission the API actually checks (grouped by
 * module) and that an existing login's set can be edited, instead of only
 * being set once at creation.
 */
function PermissionPicker({
  selected,
  onToggle
}: {
  selected: string[];
  onToggle: (value: string) => void;
}) {
  const [query, setQuery] = useState("");

  const groups = useMemo(
    () =>
      PermissionGroups.map((group) => ({
        group,
        items: Permissions.filter((p) => p.group === group)
      })).filter((g) => g.items.length > 0),
    []
  );

  const q = query.trim().toLowerCase();
  const filtered = groups.filter(
    (g) => !q || g.group.toLowerCase().includes(q) || g.items.some((i) => i.label.includes(q) || i.value.toLowerCase().includes(q))
  );

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3">
        <Input
          className="w-64"
          placeholder="بحث في الصلاحيات..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <span className="text-xs text-gray-500">
          المحدد: <span className="ltr-nums font-semibold">{selected.length}</span> من {Permissions.length}
        </span>
      </div>

      <div className="space-y-3 max-h-80 overflow-y-auto border border-gray-100 rounded-lg p-3">
        {filtered.map((g) => (
          <div key={g.group}>
            <div className="text-xs font-bold text-slate-500 mb-1">{g.group}</div>
            <div className="flex flex-wrap gap-x-4 gap-y-2">
              {g.items.map((p) => (
                <label key={p.value} className="flex items-center gap-2 text-sm text-gray-700">
                  <input type="checkbox" checked={selected.includes(p.value)} onChange={() => onToggle(p.value)} />
                  <span>{p.label}</span>
                  <span className="text-[10px] text-gray-400 ltr-nums">{p.value}</span>
                </label>
              ))}
            </div>
          </div>
        ))}
        {filtered.length === 0 && <p className="text-sm text-gray-400">لا توجد صلاحيات مطابقة</p>}
      </div>
    </div>
  );
}

export default function UsersPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newPasswordById, setNewPasswordById] = useState<Record<string, string>>({});

  const [editing, setEditing] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editRoles, setEditRoles] = useState<string[]>([]);
  const [editError, setEditError] = useState<string | null>(null);

  const { data: users, isLoading } = useQuery({ queryKey: ["users"], queryFn: () => UsersApi.list() });

  const invalidate = () => qc.invalidateQueries({ queryKey: ["users"] });

  const createMutation = useMutation({
    mutationFn: () => UsersApi.create({ username, password, displayName, roles }),
    onSuccess: () => {
      invalidate();
      setShowForm(false); setUsername(""); setPassword(""); setDisplayName(""); setRoles([]); setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ - تأكد أن حسابك يملك صلاحية admin")
  });

  const updateMutation = useMutation({
    mutationFn: (userId: string) => UsersApi.update(userId, { displayName: editName, roles: editRoles }),
    onSuccess: () => { invalidate(); setEditing(null); setEditError(null); },
    onError: (err: any) =>
      setEditError(err?.response?.data?.detail ?? err?.response?.data?.title ?? "تعذر حفظ الصلاحيات")
  });

  const deactivateMutation = useMutation({ mutationFn: UsersApi.deactivate, onSuccess: invalidate });
  const changePasswordMutation = useMutation({
    mutationFn: (vars: { id: string; newPassword: string }) => UsersApi.changePassword(vars.id, vars.newPassword),
    onSuccess: () => setNewPasswordById({})
  });

  const toggleRole = (role: string) => setRoles((r) => (r.includes(role) ? r.filter((x) => x !== role) : [...r, role]));
  const toggleEditRole = (role: string) => setEditRoles((r) => (r.includes(role) ? r.filter((x) => x !== role) : [...r, role]));

  const startEdit = (u: User) => {
    setEditing(u.id);
    setEditName(u.displayName);
    setEditRoles(u.roles);
    setEditError(null);
  };

  return (
    <>
      <PageHeader
        title="المستخدمون"
        subtitle="إدارة حسابات الدخول والصلاحيات - متاحة فقط لمن يملك دور admin"
        action={
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => setShowForm(true)}>+ مستخدم جديد</Button>
            <Select className="w-44" value={editing ?? ""} onChange={(e) => { const u = users?.find((x) => x.id === e.target.value); if (u) startEdit(u); else setEditing(null); }}>
              <option value="">تعديل صلاحيات مستخدم...</option>
              {users?.map((u) => <option key={u.id} value={u.id}>{u.username}</option>)}
            </Select>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div><label className="field-label">اسم المستخدم</label><Input value={username} onChange={(e) => setUsername(e.target.value)} required /></div>
              <div><label className="field-label">الاسم الظاهر</label><Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required /></div>
              <div><label className="field-label">كلمة المرور</label><Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} /></div>
            </div>
            <div>
              <label className="field-label">الأدوار / الصلاحيات</label>
              <PermissionPicker selected={roles} onToggle={toggleRole} />
            </div>
            <div className="flex gap-2">
              <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}</Button>
              <Button type="button" variant="secondary" onClick={() => { setShowForm(false); setError(null); }}>إلغاء</Button>
            </div>
            {error && <p className="form-error">{error}</p>}
          </form>
        </Card>
      )}

      {editing && (
        <Card className="p-5 mb-6 border-brand-200">
          <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
            <h3 className="font-semibold">
              تعديل صلاحيات: <span className="ltr-nums">{users?.find((u) => u.id === editing)?.username}</span>
            </h3>
            <Button variant="secondary" onClick={() => { setEditing(null); setEditError(null); }}>إغلاق</Button>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-4">
            <div>
              <label className="field-label">الاسم الظاهر</label>
              <Input value={editName} onChange={(e) => setEditName(e.target.value)} />
            </div>
          </div>
          <PermissionPicker selected={editRoles} onToggle={toggleEditRole} />
          <div className="flex gap-2 mt-4">
            <Button disabled={!editName || updateMutation.isPending} onClick={() => updateMutation.mutate(editing)}>
              {updateMutation.isPending ? "جارٍ الحفظ..." : "حفظ الصلاحيات"}
            </Button>
          </div>
          <p className="text-xs text-gray-500 mt-2">
            لا يمكن للمستخدم تعديل صلاحيات نفسه، ولا يمكن سحب صلاحية admin من آخر مدير نظام نشط - لتجنب فقدان الوصول.
          </p>
          {editError && <p className="form-error mt-2">{editError}</p>}
        </Card>
      )}

      <Card>
        <table className="table">
          <thead><tr>
            <th>اسم المستخدم</th><th>الاسم</th>
            <th>الأدوار</th><th>الحالة</th><th></th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {users?.map((u) => (
              <tr key={u.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50 align-top">
                <td className="font-medium ltr-nums">{u.username}</td>
                <td>{u.displayName}</td>
                <td>
                  <div className="flex flex-wrap gap-1 max-w-md">
                    {u.roles.length === 0 && <span className="text-gray-400">-</span>}
                    {u.roles.map((r) => <Badge key={r} tone="blue">{r}</Badge>)}
                  </div>
                </td>
                <td><Badge tone={u.isActive ? "green" : "gray"}>{u.isActive ? "نشط" : "معطل"}</Badge></td>
                <td>
                  <div className="flex items-center gap-2">
                    <Button variant="ghost" onClick={() => startEdit(u)}>الصلاحيات</Button>
                    <Input
                      placeholder="كلمة مرور جديدة"
                      type="password"
                      value={newPasswordById[u.id] ?? ""}
                      onChange={(e) => setNewPasswordById((p) => ({ ...p, [u.id]: e.target.value }))}
                      className="w-32"
                    />
                    <Button
                      variant="ghost"
                      disabled={(newPasswordById[u.id]?.length ?? 0) < 8}
                      onClick={() => changePasswordMutation.mutate({ id: u.id, newPassword: newPasswordById[u.id] })}
                    >
                      تغيير
                    </Button>
                    {u.isActive && <Button variant="ghost" onClick={() => deactivateMutation.mutate(u.id)}>تعطيل</Button>}
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
