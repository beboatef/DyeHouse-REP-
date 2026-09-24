import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { AuthApi, SettingsApi } from "@/api/client";
import { Card, Button, Input } from "@/components/ui";
import { Factory } from "lucide-react";

export default function LoginPage() {
  const navigate = useNavigate();
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  const loginMutation = useMutation({
    mutationFn: () => AuthApi.login(username, password),
    onSuccess: (result) => {
      localStorage.setItem("dyehouse_token", result.token);
      localStorage.setItem("dyehouse_user", JSON.stringify({ username: result.username, displayName: result.displayName, roles: result.roles }));
      navigate("/");
    },
    onError: () => setError("اسم المستخدم أو كلمة المرور غير صحيحة")
  });

  return (
    <div
      className="min-h-screen flex items-center justify-center"
      style={{ background: "linear-gradient(135deg, #312e81 0%, #4c1d95 50%, #6d28d9 100%)" }}
    >
      <Card className="w-full max-w-sm p-8">
        <div className="text-center mb-6">
          <div className="mx-auto mb-3 w-14 h-14 rounded-xl bg-indigo-50 flex items-center justify-center overflow-hidden">
            {settings?.logoDataUrl ? (
              <img src={settings.logoDataUrl} alt="" className="w-full h-full object-contain p-1.5" />
            ) : (
              <Factory size={24} className="text-brand-700" />
            )}
          </div>
          <div className="text-xl font-bold text-brand-700">{settings?.companyNameAr || "DyeHouse ERP"}</div>
          <div className="text-xs text-gray-500 mt-1">تسجيل الدخول</div>
        </div>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            setError(null);
            loginMutation.mutate();
          }}
        >
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">اسم المستخدم</label>
            <Input value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">كلمة المرور</label>
            <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <Button type="submit" className="w-full" disabled={loginMutation.isPending}>
            {loginMutation.isPending ? "جارٍ الدخول..." : "دخول"}
          </Button>
          {error && <p className="text-sm text-red-600 text-center">{error}</p>}
        </form>
        <p className="text-xs text-gray-400 text-center mt-6">
          بيئة التطوير: admin / Admin@12345
        </p>
      </Card>
    </div>
  );
}
