// Mirrors DyeHouseERP.Domain.Common.Permissions plus the "admin" role used
// for [Authorize(Roles = "admin")] on the Users controller.
export const Permissions = [
  { value: "admin", label: "مدير النظام (admin)" },
  { value: "inventory.allow_negative_stock", label: "تجاوز الرصيد السالب" },
  { value: "production.approve_stage", label: "اعتماد مراحل التشغيل" }
];
