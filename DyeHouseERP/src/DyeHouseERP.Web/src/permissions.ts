// Mirrors DyeHouseERP.Domain.Common.Permissions exactly (spec section 41).
//
// The backend model is intentionally a FLAT list of permission strings stored
// per user (User.Roles) and checked server-side by PermissionAuthorizationHandler;
// "admin" is the catch-all. There is no Role table, and this file does not add
// one - it only makes the existing model usable: previously the UI exposed just
// three of the ninety-five permissions, so most granular permissions could not
// be granted to anyone through the app at all.
//
// `value` must match the backend constant string byte-for-byte - it is the
// claim name the API checks. Keep this list in sync with Permissions.All.

export interface PermissionOption {
  value: string;
  group: string;
  label: string;
}

export const PermissionGroups: string[] = [
  "عام",
  "العملاء",
  "الأصناف",
  "استلام الخام",
  "الإنتاج",
  "المخزون",
  "المخزون الجاهز",
  "الفواتير",
  "الخزينة",
  "التشكيل",
  "الشيكات",
  "الموردون",
  "المشتريات",
  "الرواتب",
  "تكاليف أوامر التشغيل",
  "مستلزمات التشغيل",
  "بيع المواد",
  "مركز الاعتمادات",
  "المرفقات",
  "التقارير",
  "المستخدمون والأدوار",
  "الإعدادات",
  "سجل التدقيق"
];

export const Permissions: PermissionOption[] = [
  { value: "admin", group: "عام", label: "مدير النظام (كل الصلاحيات)" },

  { value: "customers.view", group: "العملاء", label: "عرض العملاء" },
  { value: "customers.create", group: "العملاء", label: "إضافة عميل" },
  { value: "customers.edit", group: "العملاء", label: "تعديل عميل" },

  { value: "items.view", group: "الأصناف", label: "عرض الأصناف" },
  { value: "items.create", group: "الأصناف", label: "إضافة صنف" },
  { value: "items.edit", group: "الأصناف", label: "تعديل صنف" },

  { value: "raw.receive", group: "استلام الخام", label: "استلام الخام" },
  { value: "raw.inspect", group: "استلام الخام", label: "فحص الخام" },
  { value: "raw.consume", group: "استلام الخام", label: "استهلاك الخام" },
  { value: "raw.return", group: "استلام الخام", label: "إرجاع الخام" },
  { value: "raw.external_release", group: "استلام الخام", label: "تشغيل خارجي" },
  { value: "raw.transfer", group: "استلام الخام", label: "تحويل خام بين العملاء" },

  { value: "production.view", group: "الإنتاج", label: "عرض الإنتاج" },
  { value: "production.create", group: "الإنتاج", label: "إنشاء أمر تشغيل" },
  { value: "production.edit", group: "الإنتاج", label: "تعديل أمر تشغيل" },
  { value: "production.execute_stage", group: "الإنتاج", label: "تنفيذ مرحلة" },
  { value: "production.complete", group: "الإنتاج", label: "إكمال أمر التشغيل" },
  { value: "production.reprocess", group: "الإنتاج", label: "إعادة التشغيل" },
  { value: "production.approve_stage", group: "الإنتاج", label: "اعتماد مراحل التشغيل" },

  { value: "inventory.view", group: "المخزون", label: "عرض المخزون" },
  { value: "inventory.edit", group: "المخزون", label: "تعديل حركات المخزون" },
  { value: "inventory.delete", group: "المخزون", label: "حذف حركة مخزون" },
  { value: "inventory.adjust", group: "المخزون", label: "تسويات المخزون" },
  { value: "inventory.allow_negative_stock", group: "المخزون", label: "تجاوز الرصيد السالب" },
  { value: "inventory.approve_negative_stock", group: "المخزون", label: "اعتماد تجاوز الرصيد السالب" },

  { value: "ready.view", group: "المخزون الجاهز", label: "عرض المخزون الجاهز" },
  { value: "ready.transfer", group: "المخزون الجاهز", label: "ترحيل للمخزون الجاهز" },
  { value: "ready.deliver", group: "المخزون الجاهز", label: "تسليم الجاهز" },

  { value: "invoices.view", group: "الفواتير", label: "عرض الفواتير" },
  { value: "invoices.create", group: "الفواتير", label: "إنشاء فاتورة" },
  { value: "invoices.issue", group: "الفواتير", label: "إصدار فاتورة" },
  { value: "invoices.cancel", group: "الفواتير", label: "إلغاء فاتورة" },

  { value: "treasury.view", group: "الخزينة", label: "عرض الخزينة" },
  { value: "treasury.create", group: "الخزينة", label: "حركة خزينة" },

  { value: "formation.view", group: "التشكيل", label: "عرض طلبات التشكيل" },
  { value: "formation.create", group: "التشكيل", label: "إنشاء طلب تشكيل" },
  { value: "formation.edit", group: "التشكيل", label: "تعديل طلب التشكيل" },
  { value: "formation.submit", group: "التشكيل", label: "إرسال طلب التشكيل" },
  { value: "formation.approve", group: "التشكيل", label: "اعتماد طلب التشكيل" },
  { value: "formation.reject", group: "التشكيل", label: "رفض طلب التشكيل" },
  { value: "formation.cancel", group: "التشكيل", label: "إلغاء طلب التشكيل" },
  { value: "formation.convert", group: "التشكيل", label: "تحويل لطلب إلى أمر تشغيل" },
  { value: "formation.manage_groups", group: "التشكيل", label: "إدارة مجموعات التشكيل" },
  { value: "formation.manage_specifications", group: "التشكيل", label: "إدارة خلايا المواصفات" },
  { value: "formation.traceability", group: "التشكيل", label: "تتبع التشكيل" },
  { value: "formation.print", group: "التشكيل", label: "طباعة طلب التشكيل" },
  { value: "formation.export", group: "التشكيل", label: "تصدير طلبات التشكيل" },

  { value: "checks.view", group: "الشيكات", label: "عرض الشيكات" },
  { value: "checks.create", group: "الشيكات", label: "تسجيل شيك" },
  { value: "checks.edit", group: "الشيكات", label: "تعديل بيانات شيك" },
  { value: "checks.endorse", group: "الشيكات", label: "تظهير شيك لمورد" },
  { value: "checks.deposit", group: "الشيكات", label: "إيداع شيك" },
  { value: "checks.clear", group: "الشيكات", label: "تحصيل شيك" },
  { value: "checks.bounce", group: "الشيكات", label: "ارتداد شيك" },
  { value: "checks.cancel", group: "الشيكات", label: "إلغاء شيك" },
  { value: "checks.export", group: "الشيكات", label: "تصدير الشيكات" },

  { value: "suppliers.view", group: "الموردون", label: "عرض الموردين" },
  { value: "suppliers.create", group: "الموردون", label: "إضافة مورد" },
  { value: "suppliers.edit", group: "الموردون", label: "تعديل مورد" },

  { value: "purchases.view", group: "المشتريات", label: "عرض المشتريات" },
  { value: "purchases.create", group: "المشتريات", label: "إنشاء أمر توريد" },
  { value: "purchases.edit", group: "المشتريات", label: "تعديل أمر توريد" },
  { value: "purchases.submit", group: "المشتريات", label: "إرسال أمر توريد" },
  { value: "purchases.approve", group: "المشتريات", label: "اعتماد أمر توريد" },
  { value: "purchases.receive", group: "المشتريات", label: "استلام أصناف" },
  { value: "purchases.cancel", group: "المشتريات", label: "إلغاء مستند مشتريات" },
  { value: "purchases.invoice", group: "المشتريات", label: "فواتير الموردين" },
  { value: "purchases.pay", group: "المشتريات", label: "سداد مورد" },
  { value: "purchases.export", group: "المشتريات", label: "تصدير المشتريات" },

  { value: "payroll.view", group: "الرواتب", label: "عرض الرواتب" },
  { value: "payroll.employees", group: "الرواتب", label: "إدارة الموظفين والأقسام" },
  { value: "payroll.create", group: "الرواتب", label: "إنشاء مسير رواتب" },
  { value: "payroll.approve", group: "الرواتب", label: "اعتماد مسير الرواتب" },
  { value: "payroll.post", group: "الرواتب", label: "ترحيل الرواتب للخزينة" },
  { value: "payroll.cancel", group: "الرواتب", label: "إلغاء مسير الرواتب" },

  { value: "costing.view", group: "تكاليف أوامر التشغيل", label: "عرض التكاليف" },
  { value: "costing.edit_estimate", group: "تكاليف أوامر التشغيل", label: "تعديل التكلفة التقديرية" },
  { value: "costing.approve", group: "تكاليف أوامر التشغيل", label: "اعتماد التكلفة النهائية" },

  { value: "supplies.view", group: "مستلزمات التشغيل", label: "عرض مستلزمات التشغيل" },
  { value: "supplies.issue", group: "مستلزمات التشغيل", label: "صرف مستلزمات التشغيل" },
  { value: "supplies.cancel", group: "مستلزمات التشغيل", label: "إلغاء صرف مستلزمات" },

  { value: "material_sales.view", group: "بيع المواد", label: "عرض بيع المواد" },
  { value: "material_sales.create", group: "بيع المواد", label: "إنشاء بيع مواد" },
  { value: "material_sales.post", group: "بيع المواد", label: "ترحيل بيع المواد" },
  { value: "material_sales.cancel", group: "بيع المواد", label: "إلغاء بيع مواد" },

  { value: "approvals.view", group: "مركز الاعتمادات", label: "عرض مركز الاعتمادات" },

  { value: "attachments.view", group: "المرفقات", label: "عرض المرفقات" },
  { value: "attachments.manage", group: "المرفقات", label: "إضافة وحذف المرفقات" },

  { value: "reports.view", group: "التقارير", label: "عرض التقارير" },
  { value: "reports.export", group: "التقارير", label: "تصدير التقارير" },

  { value: "users.manage", group: "المستخدمون والأدوار", label: "إدارة المستخدمين" },
  { value: "roles.manage", group: "المستخدمون والأدوار", label: "إدارة الأدوار والصلاحيات" },

  { value: "settings.manage", group: "الإعدادات", label: "إعدادات الشركة" },
  { value: "audit.view", group: "سجل التدقيق", label: "عرض سجل التدقيق" }
];

/** Arabic group label for a permission value, falling back to the module prefix. */
export const permissionGroupOf = (value: string): string =>
  Permissions.find((p) => p.value === value)?.group ?? value;
