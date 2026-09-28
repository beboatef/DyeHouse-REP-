import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AttachmentsApi, type Attachment } from "@/api/client";
import { Button, Card, Input } from "@/components/ui";
import { useI18n } from "@/i18n";
import { Paperclip, Trash2, Download } from "lucide-react";

/**
 * Attachments for any business document (spec section 47).
 *
 * Deliberately one shared component instead of a bespoke uploader per screen:
 * the permission model, the size limit and the download route are identical
 * everywhere, so duplicating them would guarantee they eventually drift.
 *
 * Downloads never use a plain <a href>: the bytes live behind the API, which
 * checks the user's permissions on every request.
 */
export default function AttachmentsPanel({
  entityType,
  entityId,
  title
}: {
  entityType: string;
  entityId: string;
  title?: string;
}) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const fileRef = useRef<HTMLInputElement>(null);
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryKey = ["attachments", entityType, entityId];

  const { data: attachments, isLoading } = useQuery({
    queryKey,
    queryFn: () => AttachmentsApi.list(entityType, entityId),
    enabled: Boolean(entityId)
  });

  const upload = useMutation({
    mutationFn: async (file: File) => AttachmentsApi.upload(entityType, entityId, file, description || undefined),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey });
      setDescription("");
      if (fileRef.current) fileRef.current.value = "";
      setError(null);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const remove = useMutation({
    mutationFn: (id: string) => AttachmentsApi.remove(id),
    onSuccess: () => qc.invalidateQueries({ queryKey })
  });

  const sizeLabel = (bytes: number) =>
    bytes >= 1024 * 1024
      ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
      : `${Math.max(1, Math.round(bytes / 1024))} KB`;

  return (
    <Card className="p-5">
      <div className="flex items-center gap-2 mb-1">
        <Paperclip size={16} className="text-gray-400" />
        <div className="text-sm font-semibold text-gray-700">{title ?? t("att.title")}</div>
      </div>
      <p className="text-xs text-gray-400 mb-4">{t("att.private")}</p>

      <div className="flex flex-col sm:flex-row gap-3 sm:items-end mb-4">
        <div className="flex-1">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("att.file")}</label>
          <input
            ref={fileRef}
            type="file"
            className="w-full text-sm text-gray-600 file:me-3 file:rounded-lg file:border-0 file:bg-gray-100 file:px-3 file:py-2 file:text-xs file:font-semibold file:text-gray-700"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (!file) return;
              if (file.size > 25 * 1024 * 1024) {
                setError(t("att.tooLarge"));
                return;
              }
              upload.mutate(file);
            }}
          />
        </div>
        <div className="flex-1">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("att.description")}</label>
          <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
        </div>
      </div>

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      {isLoading && <p className="text-sm text-gray-400">{t("common.loading")}</p>}
      {!isLoading && attachments?.length === 0 && (
        <p className="text-sm text-gray-400">{t("att.empty")}</p>
      )}

      <ul className="divide-y divide-gray-100">
        {attachments?.map((a: Attachment) => (
          <li key={a.id} className="py-3 flex items-start justify-between gap-4">
            <div className="min-w-0">
              <div className="text-sm font-medium text-gray-800 truncate">{a.fileName}</div>
              <div className="text-xs text-gray-400 mt-0.5">
                {sizeLabel(a.sizeBytes)} · {t("att.uploadedBy")} {a.uploadedBy} ·{" "}
                {new Date(a.uploadedAtUtc).toLocaleString("en-GB")}
              </div>
              {a.description && <div className="text-xs text-gray-500 mt-1">{a.description}</div>}
            </div>
            <div className="flex items-center gap-2 shrink-0">
              <Button
                variant="secondary"
                onClick={() => AttachmentsApi.download(a.id, a.fileName)}
                title={t("att.download")}
              >
                <Download size={14} />
              </Button>
              <Button
                variant="ghost"
                onClick={() => {
                  if (window.confirm(t("att.deleteConfirm"))) remove.mutate(a.id);
                }}
              >
                <Trash2 size={14} className="text-red-500" />
              </Button>
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}
