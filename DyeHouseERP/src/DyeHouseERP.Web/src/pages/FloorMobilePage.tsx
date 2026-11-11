import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeftRight, Pause, Play, RefreshCw } from "lucide-react";
import {
  ProductionFloorApi,
  ProductionOrdersApi,
  ProductionStagesApi,
  type ProductionFloorRow
} from "@/api/client";
import { Alert, Badge, Button, SearchInput, Select, Input, errorMessage } from "@/components/ui";
import RequestFailure from "@/components/RequestFailure";
import { useI18n } from "@/i18n";

/**
 * /floor/m - the phone-first floor screen for workers and supervisors.
 *
 * A deliberately small surface: the live orders on the worker's stage, the
 * single next action for each one, and nothing else. No admin tables, no
 * wide grids - one card per order, large touch targets (.btn-floor is 48px),
 * and every write keeps its values on screen when the network drops so a
 * retry posts exactly what the worker typed.
 */
export default function FloorMobilePage() {
  const { t } = useI18n();
  const [stageId, setStageId] = useState("");
  const [search, setSearch] = useState("");

  const stagesQuery = useQuery({
    queryKey: ["floor-m-stages"],
    queryFn: () => ProductionStagesApi.list({ activeOnly: true })
  });
  const floorQuery = useQuery({
    queryKey: ["floor-m", stageId, search],
    queryFn: () =>
      ProductionFloorApi.get({
        stageDefinitionId: stageId || undefined,
        search: search.trim() || undefined
      }),
    refetchInterval: 30_000
  });

  const rows = (floorQuery.data ?? []).filter((r) => !r.isDelivered);

  return (
    <div className="mx-auto w-full max-w-2xl p-4 sm:p-6">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-ink">{t("floor.m.title", "شاشة الأرضية")}</h1>
          <p className="mt-0.5 text-sm text-ink-subtle">{t("floor.m.subtitle", "أوامر التشغيل الحالية على المرحلة")}</p>
        </div>
        <Button
          variant="secondary"
          className="btn-floor shrink-0"
          onClick={() => void floorQuery.refetch()}
          disabled={floorQuery.isFetching}
          aria-label={t("floor.m.refresh", "تحديث")}
        >
          <RefreshCw size={18} className={floorQuery.isFetching ? "animate-spin" : ""} />
        </Button>
      </div>

      <div className="mt-4 grid gap-2">
        <SearchInput
          placeholder={t("floor.m.searchPlaceholder", "رقم الأمر / العميل / الصنف")}
          aria-label={t("common.search")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <Select
          value={stageId}
          onChange={(e) => setStageId(e.target.value)}
          aria-label={t("floor.stage")}
        >
          <option value="">{t("floor.m.allStages", "كل المراحل")}</option>
          {(stagesQuery.data ?? []).map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </Select>
      </div>

      <div className="mt-4">
        {floorQuery.error ? (
          <RequestFailure error={floorQuery.error} onRetry={() => void floorQuery.refetch()} />
        ) : floorQuery.isLoading ? (
          <div className="p-8 text-center text-ink-subtle">{t("common.loading")}</div>
        ) : rows.length === 0 ? (
          <div className="card card-pad p-8 text-center text-ink-subtle">{t("floor.empty")}</div>
        ) : (
          <ol className="grid gap-3">
            {rows.map((row) => (
              <li key={row.productionOrderId}>
                <FloorCard row={row} />
              </li>
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}

/** Minutes on a stage as a compact duration; numerals stay LTR in both languages. */
function StageAge({ minutes }: { minutes: number | null }) {
  if (minutes === null || minutes === undefined) return <span>-</span>;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return <span dir="ltr">{h > 0 ? `${h}h ${m}m` : `${m}m`}</span>;
}

function FloorCard({ row }: { row: ProductionFloorRow }) {
  const { t } = useI18n();
  const [panel, setPanel] = useState<"output" | "pause" | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const isPaused = row.orderStatus === "Paused";
  const canRecord = !isPaused && !row.awaitingStart && row.currentStageStatus === "InProgress" && row.stageExecutionId;

  return (
    <article className="card card-pad" aria-label={row.orderNumber}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-2xl font-bold tracking-tight text-ink" dir="ltr">{row.orderNumber}</div>
          <div className="mt-0.5 truncate text-sm text-ink-subtle">
            {row.customerName} - {row.itemName}
          </div>
        </div>
        {isPaused ? (
          <Badge tone="warning">{t("floor.m.pausedBadge", "موقوف مؤقتًا")}</Badge>
        ) : row.awaitingStart ? (
          <Badge tone="info">{t("floor.awaitingStart")}</Badge>
        ) : (
          <Badge tone="success">{t("floor.running")}</Badge>
        )}
      </div>

      <dl className="mt-3 grid grid-cols-2 gap-2 text-sm">
        <div>
          <dt className="text-ink-subtle">{t("floor.m.stage", "المرحلة")}</dt>
          <dd className="font-medium text-ink">{row.currentStageName}</dd>
        </div>
        <div>
          <dt className="text-ink-subtle">{t("floor.inStageFor")}</dt>
          <dd className="font-medium text-ink"><StageAge minutes={row.minutesInStage} /></dd>
        </div>
        <div>
          <dt className="text-ink-subtle">{t("floor.in")}</dt>
          <dd className="font-medium text-ink" dir="ltr">
            {row.inputKg ?? "-"} {t("unit.kg")} / {row.inputMeter ?? "-"} {t("unit.meter")}
          </dd>
        </div>
        <div>
          <dt className="text-ink-subtle">{t("floor.out")}</dt>
          <dd className="font-medium text-ink" dir="ltr">
            {row.outputKg ?? "-"} {t("unit.kg")} / {row.outputMeter ?? "-"} {t("unit.meter")}
          </dd>
        </div>
      </dl>

      {notice && (
        <Alert variant="success" className="mt-3">
          {notice}
        </Alert>
      )}

      {/* The next action. One primary control per state so the worker never
          hunts through a menu: record and pause act here, everything that
          needs a stage or lot choice opens the order (second tap). */}
      <div className="mt-4 grid gap-2">
        {isPaused || row.awaitingStart ? (
          <Link
            to={`/production-orders/${row.productionOrderId}`}
            className="btn btn-primary btn-floor w-full text-base"
          >
            <Play size={18} />
            {isPaused ? t("floor.m.resume", "استئناف") : t("floor.m.start", "بدء")}
          </Link>
        ) : (
          <>
            {canRecord && (
              <div className="grid grid-cols-2 gap-2">
                <Button
                  className="btn-floor text-base"
                  onClick={() => { setPanel(panel === "output" ? null : "output"); setNotice(null); }}
                  aria-expanded={panel === "output"}
                >
                  {t("floor.m.recordOutput", "تسجيل إنتاج")}
                </Button>
                <Button
                  variant="secondary"
                  className="btn-floor text-base"
                  onClick={() => { setPanel(panel === "pause" ? null : "pause"); setNotice(null); }}
                  aria-expanded={panel === "pause"}
                >
                  <Pause size={18} />
                  {t("floor.m.pause", "إيقاف مؤقت")}
                </Button>
              </div>
            )}
            <Link
              to={`/production-orders/${row.productionOrderId}`}
              className="btn btn-secondary btn-floor w-full text-base"
            >
              <ArrowLeftRight size={18} />
              {t("floor.m.transfer", "نقل / التفاصيل")}
            </Link>
          </>
        )}
      </div>

      {panel === "output" && row.stageExecutionId && (
        <OutputForm
          stageExecutionId={row.stageExecutionId}
          onDone={(msg) => { setPanel(null); setNotice(msg); }}
        />
      )}
      {panel === "pause" && (
        <PauseForm
          orderId={row.productionOrderId}
          onDone={(msg) => { setPanel(null); setNotice(msg); }}
        />
      )}
    </article>
  );
}

/** Parse a phone-keyboard decimal: Arabic and Western separators both occur. */
function parseDecimal(raw: string): number | undefined {
  const normalized = raw.trim().replace(/,/g, ".").replace(/٫/g, ".");
  if (!normalized) return undefined;
  const n = Number(normalized);
  return Number.isFinite(n) && n >= 0 ? n : undefined;
}

function OutputForm({ stageExecutionId, onDone }: {
  stageExecutionId: string;
  onDone: (message: string) => void;
}) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [kg, setKg] = useState("");
  const [meter, setMeter] = useState("");
  // The confirm step repeats the quantity back before anything posts, and the
  // submit button disables while the request is in flight - two deliberate
  // taps, so a double tap cannot post twice.
  const [confirming, setConfirming] = useState(false);

  const mutation = useMutation({
    mutationFn: () =>
      ProductionOrdersApi.updateStageOutput(stageExecutionId, {
        outputKg: parseDecimal(kg),
        outputMeter: parseDecimal(meter)
      })
  });

  const qtyKg = parseDecimal(kg);
  const qtyMeter = parseDecimal(meter);
  const hasQuantity = qtyKg !== undefined || qtyMeter !== undefined;
  const invalid = (kg.trim() !== "" && qtyKg === undefined) || (meter.trim() !== "" && qtyMeter === undefined);

  function submit() {
    mutation.mutate(undefined, {
      onSuccess: () => {
        // Values clear only after the server confirms: a dropped network
        // keeps everything the worker typed for the retry.
        setKg(""); setMeter(""); setConfirming(false);
        void queryClient.invalidateQueries({ queryKey: ["floor-m"] });
        onDone(t("floor.m.saved", "تم حفظ الإنتاج"));
      }
    });
  }

  return (
    <div className="mt-4 rounded-xl border border-line bg-surface-sunken p-3">
      {!confirming ? (
        <div className="grid gap-3">
          <label className="grid gap-1 text-sm">
            <span className="font-medium text-ink">
              {t("floor.m.outputKg", "الإنتاج (كجم)")}
            </span>
            <Input
              inputMode="decimal"
              autoComplete="off"
              dir="ltr"
              className="min-h-[48px] text-lg"
              value={kg}
              onChange={(e) => setKg(e.target.value)}
              placeholder="0"
            />
          </label>
          <label className="grid gap-1 text-sm">
            <span className="font-medium text-ink">
              {t("floor.m.outputMeter", "الإنتاج (متر)")}
            </span>
            <Input
              inputMode="decimal"
              autoComplete="off"
              dir="ltr"
              className="min-h-[48px] text-lg"
              value={meter}
              onChange={(e) => setMeter(e.target.value)}
              placeholder="0"
            />
          </label>
          {invalid && (
            <Alert variant="danger">{t("floor.m.invalidQty", "الكمية يجب أن تكون رقمًا موجبًا")}</Alert>
          )}
          <Button
            className="btn-floor text-base"
            disabled={!hasQuantity || invalid}
            onClick={() => setConfirming(true)}
          >
            {t("floor.m.review", "مراجعة")}
          </Button>
        </div>
      ) : (
        <div className="grid gap-3">
          <p className="text-sm font-semibold text-ink">{t("floor.m.confirmTitle", "تأكيد الكمية")}</p>
          <p className="text-lg text-ink" dir="ltr">
            {qtyKg !== undefined && <span>{qtyKg} {t("unit.kg")}</span>}
            {qtyKg !== undefined && qtyMeter !== undefined && <span> + </span>}
            {qtyMeter !== undefined && <span>{qtyMeter} {t("unit.meter")}</span>}
          </p>
          {mutation.error && (
            <Alert variant="danger">
              <div>{errorMessage(mutation.error, t("floor.m.saveFailed", "تعذّر الحفظ. تحقق من الاتصال ثم أعد المحاولة — القيم المدخلة محفوظة."))}</div>
              <Button
                variant="secondary"
                className="btn-floor mt-2 w-full"
                disabled={mutation.isPending}
                onClick={submit}
              >
                {t("floor.m.retry", "إعادة المحاولة")}
              </Button>
            </Alert>
          )}
          <div className="grid grid-cols-2 gap-2">
            <Button
              className="btn-floor text-base"
              disabled={mutation.isPending}
              onClick={submit}
            >
              {mutation.isPending ? t("common.saving") : t("floor.m.confirmSave", "تأكيد الحفظ")}
            </Button>
            <Button
              variant="secondary"
              className="btn-floor text-base"
              disabled={mutation.isPending}
              onClick={() => setConfirming(false)}
            >
              {t("common.cancel")}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

function PauseForm({ orderId, onDone }: {
  orderId: string;
  onDone: (message: string) => void;
}) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");

  const mutation = useMutation({
    mutationFn: () => ProductionOrdersApi.pause(orderId, { reason: reason.trim() })
  });

  function submit() {
    if (!reason.trim()) return;
    mutation.mutate(undefined, {
      onSuccess: () => {
        setReason("");
        void queryClient.invalidateQueries({ queryKey: ["floor-m"] });
        onDone(t("floor.m.paused", "تم الإيقاف المؤقت"));
      }
    });
  }

  return (
    <div className="mt-4 grid gap-3 rounded-xl border border-line bg-surface-sunken p-3">
      <label className="grid gap-1 text-sm">
        <span className="font-medium text-ink">{t("floor.m.pauseReason", "سبب الإيقاف")}</span>
        <Input
          className="min-h-[48px] text-base"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={t("floor.m.pauseReasonPlaceholder", "مثال: انتظار الصيانة")}
          maxLength={1000}
        />
      </label>
      {mutation.error && (
        <Alert variant="danger">
          <div>{errorMessage(mutation.error, t("floor.m.saveFailed", "تعذّر الحفظ. تحقق من الاتصال ثم أعد المحاولة — القيم المدخلة محفوظة."))}</div>
          <Button
            variant="secondary"
            className="btn-floor mt-2 w-full"
            disabled={mutation.isPending}
            onClick={submit}
          >
            {t("floor.m.retry", "إعادة المحاولة")}
          </Button>
        </Alert>
      )}
      <Button
        className="btn-floor text-base"
        disabled={!reason.trim() || mutation.isPending}
        onClick={submit}
      >
        {mutation.isPending ? t("common.saving") : t("floor.m.pause", "إيقاف مؤقت")}
      </Button>
    </div>
  );
}
