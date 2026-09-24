import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { SettingsApi } from "@/api/client";
import { PageHeader, Card, Button, Input } from "@/components/ui";
import { UploadCloud, Trash2 } from "lucide-react";

export default function SettingsPage() {
  const qc = useQueryClient();
  const { data: settings, isLoading } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  const [companyNameAr, setCompanyNameAr] = useState("");
  const [companyNameEn, setCompanyNameEn] = useState("");
  const [logoDataUrl, setLogoDataUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (settings) {
      setCompanyNameAr(settings.companyNameAr);
      setCompanyNameEn(settings.companyNameEn);
      setLogoDataUrl(settings.logoDataUrl);
    }
  }, [settings]);

  const saveMutation = useMutation({
    mutationFn: () => SettingsApi.update({ companyNameAr, companyNameEn, logoDataUrl }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["company-settings"] });
      setError(null);
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ - تأكد أن حسابك يملك صلاحية settings.manage")
  });

  const onFileSelected = (file: File | undefined) => {
    if (!file) return;
    if (file.size > 2_000_000) {
      setError("حجم الصورة كبير جدًا - الحد الأقصى تقريبًا 2 ميجا");
      return;
    }
    const reader = new FileReader();
    reader.onload = () => setLogoDataUrl(reader.result as string);
    reader.readAsDataURL(file);
  };

  if (isLoading) return <Card className="p-6 text-center text-gray-400">جارٍ التحميل...</Card>;

  return (
    <>
      <PageHeader title="إعدادات الشركة" subtitle="اسم الشركة والشعار - يظهران في شاشة الدخول والقائمة الجانبية وسيُستخدمان لاحقًا في رأس المستندات المطبوعة" />

      <Card className="p-6 max-w-2xl">
        <div className="mb-6">
          <label className="block text-xs font-medium text-gray-600 mb-2">شعار الشركة</label>
          <div className="flex items-center gap-4">
            <div className="w-20 h-20 rounded-xl border border-dashed border-gray-300 flex items-center justify-center bg-gray-50 overflow-hidden">
              {logoDataUrl ? (
                <img src={logoDataUrl} alt="شعار الشركة" className="w-full h-full object-contain" />
              ) : (
                <span className="text-[10px] text-gray-400 text-center px-1">لا يوجد شعار</span>
              )}
            </div>
            <div className="flex flex-col gap-2">
              <label className="inline-flex items-center gap-2 text-sm font-medium text-brand-700 cursor-pointer hover:underline">
                <UploadCloud size={16} />
                رفع شعار جديد
                <input type="file" accept="image/png,image/jpeg,image/svg+xml,image/webp" className="hidden" onChange={(e) => onFileSelected(e.target.files?.[0])} />
              </label>
              {logoDataUrl && (
                <button type="button" onClick={() => setLogoDataUrl(null)} className="inline-flex items-center gap-2 text-sm text-red-600 hover:underline w-fit">
                  <Trash2 size={16} />
                  إزالة الشعار
                </button>
              )}
              <p className="text-[11px] text-gray-400">PNG أو SVG أو JPG، حتى 2 ميجا تقريبًا</p>
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">اسم الشركة (عربي)</label>
            <Input value={companyNameAr} onChange={(e) => setCompanyNameAr(e.target.value)} required />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">اسم الشركة (إنجليزي)</label>
            <Input value={companyNameEn} onChange={(e) => setCompanyNameEn(e.target.value)} required dir="ltr" />
          </div>
        </div>

        <Button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}>
          {saveMutation.isPending ? "جارٍ الحفظ..." : saved ? "تم الحفظ ✓" : "حفظ"}
        </Button>
        {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
      </Card>
    </>
  );
}
