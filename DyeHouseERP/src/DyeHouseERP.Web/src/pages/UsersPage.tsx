import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UsersApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { Permissions } from "@/permissions";

export default function UsersPage() {
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newPasswordById, setNewPasswordById] = useState<Record<string, string>>({});

  const { data: users, isLoading } = useQuery({ queryKey: ["users"], queryFn: () => UsersApi.list() });

  const createMutation = useMutation({
    mutationFn: () => UsersApi.create({ username, password, displayName, roles }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users"] });
      setShowForm(false); setUsername(""); setPassword(""); setDisplayName(""); setRoles([]); setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ - تأكد أن حسابك يملك صلاحية admin")
  });

  const deactivateMutation = useMutation({ mutationFn: UsersApi.deactivate, onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }) });
  const changePasswordMutation = useMutation({
    mutationFn: (vars: { id: string; newPassword: string }) => UsersApi.changePassword(vars.id, vars.newPassword),
    onSuccess: () => setNewPasswordById({})
  });

  const toggleRole = (role: string) => setRoles((r) => (r.includes(role) ? r.filter((x) => x !== role) : [...r, role]));

  return (
    <>
      <PageHeader
        title="المستخدمون"
        subtitle="إدارة حسابات الدخول والصلاحيات - متاحة فقط لمن يملك دور admin"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مستخدم جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div><label className="block text-xs font-medium text-gray-600 mb-1">اسم المستخدم</label><Input value={username} onChange={(e) => setUsername(e.target.value)} required /></div>
              <div><label className="block text-xs font-medium text-gray-600 mb-1">الاسم الظاهر</label><Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required /></div>
              <div><label className="block text-xs font-medium text-gray-600 mb-1">كلمة المرور</label><Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} /></div>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-2">الأدوار / الصلاحيات</label>
              <div className="flex flex-wrap gap-3">
                {Permissions.map((p) => (
                  <label key={p.value} className="flex items-center gap-2 text-sm text-gray-700">
                    <input type="checkbox" checked={roles.includes(p.value)} onChange={() => toggleRole(p.value)} />
                    {p.label}
                  </label>
                ))}
              </div>
            </div>
            <Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}</Button>
            {error && <p className="text-sm text-red-600">{error}</p>}
          </form>
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">اسم المستخدم</th><th className="text-start px-4 py-3 font-medium">الاسم</th>
            <th className="text-start px-4 py-3 font-medium">الأدوار</th><th className="text-start px-4 py-3 font-medium">الحالة</th><th className="text-start px-4 py-3 font-medium"></th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {users?.map((u) => (
              <tr key={u.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50 align-top">
                <td className="px-4 py-3 font-medium ltr-nums">{u.username}</td>
                <td className="px-4 py-3">{u.displayName}</td>
                <td className="px-4 py-3 space-x-1 space-x-reverse">
                  {u.roles.length === 0 && <span className="text-gray-400">-</span>}
                  {u.roles.map((r) => <Badge key={r} tone="blue">{r}</Badge>)}
                </td>
                <td className="px-4 py-3"><Badge tone={u.isActive ? "green" : "gray"}>{u.isActive ? "نشط" : "معطل"}</Badge></td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
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
