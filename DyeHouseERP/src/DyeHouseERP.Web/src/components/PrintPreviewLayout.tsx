import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { SettingsApi } from "@/api/client";
import { QRCodeSVG } from "qrcode.react";
import { Printer, ArrowRight } from "lucide-react";

export interface PrintColumn<T> {
  header: string;
  render: (row: T) => React.ReactNode;
  align?: "start" | "end";
}

/// <summary>
/// A real in-app print-preview screen (spec section 39): renders the
/// document as styled A4 HTML with the company logo/name, then lets the
/// browser's own print dialog (window.print()) handle actual printing or
/// "save as PDF" - no plugin, no server round-trip needed just to preview.
/// The toolbar (back/print buttons, sidebar-free full page) is hidden via
/// @media print so only the document itself prints.
/// </summary>
export default function PrintPreviewLayout<T>({
  title, documentNumber, subtitleLines, columns, rows, totals, qrValue, notes
}: {
  title: string;
  documentNumber: string;
  subtitleLines: { label: string; value: string }[];
  columns: PrintColumn<T>[];
  rows: T[];
  totals?: { label: string; value: string }[];
  qrValue?: string;
  notes?: string | null;
}) {
  const navigate = useNavigate();
  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  return (
    <div className="min-h-screen bg-surface-sunken">
      <div className="no-print sticky top-0 z-10 flex items-center justify-between border-b border-line bg-surface px-6 py-3">
        <button onClick={() => navigate(-1)} className="btn btn-ghost btn-sm">
          <ArrowRight size={16} />
          رجوع
        </button>
        <button onClick={() => window.print()} className="btn btn-primary">
          <Printer size={16} />
          طباعة
        </button>
      </div>

      {/* `paper` pins the LIGHT palette on this subtree, so the sheet is a white
          page with near-black ink even when the app is in dark mode - a
          document is a document in either theme, and printing from a dark app
          must never produce a dark page. */}
      <div className="paper max-w-[210mm] mx-auto my-6 bg-surface shadow-sm print:shadow-none print:my-0 print-page" dir="rtl">
        <div className="p-10 print:p-8">
          <div className="flex items-start justify-between border-b-2 border-ink pb-4 mb-6">
            <div className="flex items-center gap-3">
              {settings?.logoDataUrl ? (
                <img src={settings.logoDataUrl} alt="" className="w-14 h-14 object-contain" />
              ) : null}
              <div>
                <div className="text-lg font-bold">{settings?.companyNameAr || "DyeHouse ERP"}</div>
                <div className="text-xs text-ink-muted ltr-nums">{settings?.companyNameEn || "DyeHouse ERP"}</div>
              </div>
            </div>
            {qrValue && (
              <div className="text-center">
                <QRCodeSVG value={qrValue} size={64} />
                <div className="mt-1 text-[9px] text-ink-subtle">امسح لعرض المستند</div>
              </div>
            )}
          </div>

          <div className="flex items-start justify-between mb-6">
            <div>
              <h1 className="text-xl font-bold">{title}</h1>
              <div className="mt-1 text-sm text-ink-muted ltr-nums">{documentNumber}</div>
            </div>
            <div className="space-y-0.5 text-left text-sm text-ink-muted">
              {subtitleLines.map((l, i) => (
                <div key={i}><span className="text-ink-subtle">{l.label}: </span><span className="font-medium text-ink">{l.value}</span></div>
              ))}
            </div>
          </div>

          <table className="w-full text-sm border-collapse mb-6">
            <thead>
              <tr className="border-y border-line-strong bg-surface-sunken">
                {columns.map((c, i) => (
                  <th key={i} className={`px-3 py-2 font-semibold text-${c.align === "end" ? "end" : "start"}`}>{c.header}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, i) => (
                <tr key={i} className="border-b border-line">
                  {columns.map((c, j) => (
                    <td key={j} className={`px-3 py-2 ${c.align === "end" ? "text-end ltr-nums" : ""}`}>{c.render(row)}</td>
                  ))}
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td colSpan={columns.length} className="px-3 py-6 text-center text-ink-subtle">لا توجد بنود</td></tr>
              )}
            </tbody>
          </table>

          {totals && totals.length > 0 && (
            <div className="flex justify-start mb-6">
              <table className="text-sm">
                <tbody>
                  {totals.map((t, i) => (
                    <tr key={i}>
                      <td className="px-3 py-1 text-ink-muted">{t.label}</td>
                      <td className="px-3 py-1 font-semibold ltr-nums">{t.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {notes && (
            <div className="mb-8 border-t border-line pt-4 text-sm text-ink-muted">
              <span className="text-ink-subtle">ملاحظات: </span>{notes}
            </div>
          )}

          <div className="mt-16 grid grid-cols-2 gap-8 border-t border-line pt-8 text-sm text-ink-muted">
            <div>توقيع المستلم: ______________________</div>
            <div>توقيع المسؤول: ______________________</div>
          </div>
        </div>
      </div>

      <style>{`
        @media print {
          .no-print { display: none !important; }
          body { background: white !important; }
          .print-page { box-shadow: none !important; margin: 0 !important; max-width: 100% !important; }
          @page { size: A4; margin: 12mm; }
        }
      `}</style>
    </div>
  );
}
