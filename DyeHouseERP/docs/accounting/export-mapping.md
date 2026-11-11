# ربط أعمدة التصدير المحاسبي (Part 2)

التصدير للقراءة فقط من `GET /api/accounting/export` بمدى تاريخي إجباري
(`from` / `to`) وفلتر اختياري لعميل (`customerId`) أو مورد (`supplierId`)
 واحد فقط في كل مرة، وبصيغة `csv` (ملف واحد لكل مجموعة، بترميز UTF-8 مع
BOM حتى تفتح العربية في Excel بشكل سليم) أو `xlsx` (ورقة لكل مجموعة).

> ⚠️ يجب مراجعة هذا الربط مع المحاسب واعتماده قبل أول استيراد فعلي.
> لا ندّعي التوافق مع أي برنامج محاسبي بعينه — الجداول أدناه تصف فقط
> مصدر كل عمود داخل النظام.

## المجموعات الخمس

| المجموعة (`set`) | ورقة XLSX | مصدر الصفوف |
|---|---|---|
| `customer-invoices` | customer-invoices | سطر لكل سطر فاتورة عميل (التاريخ ضمن المدى) |
| `supplier` | supplier | فواتير الموردين + مدفوعاتهم (التاريخ ضمن المدى) |
| `treasury` | treasury | حركة خزينة لكل حساب (التاريخ ضمن المدى) |
| `payroll` | payroll | مسير رواتب لكل شهر (الفترة ضمن المدى) |
| `cheques` | cheques | حركة شيك (التاريخ ضمن المدى) |

## 1. فواتير العملاء مع السطور (`customer-invoices`)

صف لكل سطر فاتورة (فاتورة بلا سطور تظهر بصف واحد بكميات فارغة).
حالة الفاتورة تُصدَّر كما هي (مسودة/مصدرة/...) — التصدير لا يفلتر
بالحالة.

| العمود | المصدر في النظام |
|---|---|
| Row | رقم الصف في الملف (وليس بيانًا محاسبيًا) |
| InvoiceNumber | `Invoice.InvoiceNumber` (رقم المستند المصدر) |
| InvoiceDate | `Invoice.InvoiceDate` |
| CustomerCode / CustomerName | `Customer.Code` / `Customer.Name` |
| ItemCode | `Item.Code` لسطر الفاتورة |
| Quantity / Unit / UnitPrice | `InvoiceLine.Quantity` / `Unit` / `ProcessingPrice` |
| LineTotal | `Quantity × ProcessingPrice` لنفس الصف |
| InvoiceSubtotal | مجموع سطور نفس الفاتورة |
| InvoiceDiscount / InvoiceTax | `Invoice.Discount` / `Invoice.Tax` |
| InvoiceTotal | `Subtotal - Discount + Tax` لنفس الفاتورة |
| Status | `Invoice.Status` |
| LedgerEntryId | قيد `CustomerLedgerEntry` المدين للفاتورة (`SourceDocumentId` = الفاتورة، `Debit > 0`) — مفتاح التتبع لدفتر العميل |

صف `TOTAL` أسفل الملف: مجموع عمود `LineTotal` على كل الصفوف، ومجموع
عمود `InvoiceTotal` على الفواتير المميزة (وليس على الصفوف حتى لا
تتضاعف الفاتورة متعددة السطور).

## 2. فواتير الموردين ومدفوعاتهم (`supplier`)

صفوف من نوعين يميزها عمود `RowType` (`Invoice` أو `Payment`).

| العمود | المصدر في النظام |
|---|---|
| RowType | `Invoice` = فاتورة مورد، `Payment` = مدفوعة مورد |
| DocumentNumber | `SupplierInvoice.InvoiceNumber` (رقم المورد) أو `SupplierPayment.PaymentNumber` (رقم المستند المصدر) |
| DocumentDate | `SupplierInvoice.InvoiceDate` أو `SupplierPayment.PaymentDate` |
| SupplierCode | `Supplier.Code` |
| Details | عدد سطور الفاتورة، أو وصف/طريقة المدفوعة |
| Subtotal / Discount / Tax | `SupplierInvoice` (فارغة لصفوف المدفوعات) |
| InvoiceTotal | إجمالي الفاتورة (فارغ لصفوف المدفوعات) |
| PaidAmount | `SupplierPayment.Amount` (فارغ لصفوف الفواتير) |
| Status | `SupplierInvoice.Status`، أو `Posted` إن كان للمدفوعة حساب خزينة و`Recorded` إن لم يكن |
| LinkedDocument | رقم فاتورة المورد المرتبطة بالمدفوعة (إن وُجدت) |
| LedgerEntryId | قيد `SupplierLedgerEntry` للفاتورة (`Debit > 0`) أو للمدفوعة (`Credit > 0`) — مفتاح التتبع لدفتر المورد |

صف `TOTAL`: مجاميع كل عمود نقدي على الصفوف المصدَّرة (الفواتير
والمدفوعات تُجمع كلٌّ في عمودها، فلا يختلط إجمالي الفواتير بمبالغ الدفع).

## 3. حركات الخزينة لكل حساب (`treasury`)

| العمود | المصدر في النظام |
|---|---|
| AccountCode / AccountName | `TreasuryAccount.Code` / `Name` |
| Date | `TreasuryTransaction.TransactionDate` |
| Direction | `In` (داخل) أو `Out` (خارج) |
| InAmount / OutAmount | `TreasuryTransaction.Amount` في عمود الاتجاه المناسب |
| DocumentNumber | `TreasuryTransaction.SourceDocumentNumber` (رقم المستند المصدر) |
| Description | `TreasuryTransaction.Description` |
| RunningBalance | الرصيد الجاري للحساب **محسوبًا في SQL**: مجموع كل حركات الحساب حتى هذا السطر مرتبةً بـ `(TransactionDate, Id)` — يشمل ما قبل المدى (رصيد الافتتاح)، لا المدى فقط |
| LedgerRowId | `TreasuryTransaction.Id` نفسه (دفتر الخزينة هو هذا الجدول) |

صف `TOTAL`: مجموع الداخل ومجموع الخارج على الصفوف المصدَّرة. الرصيد
الختامي لأي حساب = آخر `RunningBalance` له في الملف.

## 4. مسيرات الرواتب لكل شهر (`payroll`)

| العمود | المصدر في النظام |
|---|---|
| RunNumber | `PayrollRun.RunNumber` (رقم المستند المصدر) |
| Year / Month | `PayrollRun.PeriodYear` / `PeriodMonth` |
| Gross | مجموع مكونات الأجر (`BasicSalary + Allowances + Incentives + Bonuses + Commissions + OvertimeAmount + OtherEarnings`) |
| Deductions | `PayrollRunLine.Deductions` مجموعًا |
| Net | `Gross - Deductions - Tax - Insurance - Advances` مجموعًا |
| Status | `PayrollRun.Status` (حالة الاعتماد/الترحيل) |
| PostedAtUtc | `PayrollRun.PostedAtUtc` (فارغ إن لم يُرحَّل) |
| LedgerRowId | `PayrollRun.Id` — لا يوجد دفتر أستاذ للرواتب في النظام، فمفتاح التتبع هو المسير نفسه |

صف `TOTAL`: مجاميع `Gross` و`Deductions` و`Net` على المسيرات المصدَّرة.

## 5. حركات الشيكات (`cheques`)

صف لكل `CheckMovement` (التاريخ ضمن المدى).

| العمود | المصدر في النظام |
|---|---|
| MovementDate | `CheckMovement.MovementDate` |
| CheckNumber | `Check.CheckNumber` (رقم المستند المصدر) |
| Direction | `Check.Direction` (شيك عميل / شيك مورد) |
| BankName | `Check.BankName` |
| MovementType | `CheckMovement.MovementType` |
| FromHolder / ToHolder | `CheckMovement.FromHolder` / `ToHolder` |
| Amount | `Check.Amount` (مبلغ الشيك الأب، لا الحركة — الحركة لا تحمل مبلغًا) |
| MovementId | `CheckMovement.Id` — حركات الشيكات لا قيد دفتر لها؛ التتبع برقم الحركة ورقم الشيك، وصف النقود للشيك المحصَّل هو `TreasuryTransaction` الذي `SourceDocumentId` فيه هو الشيك |

صف `TOTAL`: مجموع عمود `Amount` على الحركات المصدَّرة (مجموع إخباري —
الشيك الواحد قد يظهر بعدة حركات).

## التدقيق (Audit)

كل تصدير ناجح يكتب قيد تدقيق واحد (`Action = AccountingExport`) يحمل
المدى والتاريخ والصيغة والمجموعات وعدد الصفوف لكل مجموعة — ولا يكتب
محتويات الملف أبدًا. التصدير لا يقفل فترة ولا يجمّد بيانات: للقراءة فقط.
