import { Badge } from "@/components/ui";
import { useI18n } from "@/i18n";
import { Lock } from "lucide-react";
import type { StageExecution } from "@/api/client";

const stageStatusLabel: Record<string, string> = {
  Pending: "بالانتظار", InProgress: "جارية", Completed: "مكتملة", Skipped: "تم تخطيها"
};
const stageStatusTone: Record<string, "gray" | "yellow" | "green" | "blue"> = {
  Pending: "gray", InProgress: "yellow", Completed: "green", Skipped: "blue"
};

/**
 * One stage in the order's history - READ ONLY.
 *
 * This row used to carry its own "start stage" and "complete stage" buttons.
 * That was the separate "End Stage" path the specification removes (spec section
 * 14): a stage is now ended only by transferring the order to the next one the
 * user chooses, which is what records the output, locks the stage and calculates
 * the loss from its baseline. Leaving the old buttons in place would let a stage
 * be closed without ever choosing a next stage, leaving the order with no active
 * stage at all - so they are gone, and this component only reports history.
 */
export default function StageRow({ stage }: { stage: StageExecution }) {
  const { t } = useI18n();

  const baseline = stage.baselineKg != null ? `${stage.baselineKg} كجم` : stage.baselineMeter != null ? `${stage.baselineMeter} متر` : "—";
  const output = stage.outputKg != null ? `${stage.outputKg} كجم` : stage.outputMeter != null ? `${stage.outputMeter} متر` : null;
  const loss = stage.lossKg != null ? `${stage.lossKg} كجم` : stage.lossMeter != null ? `${stage.lossMeter} متر` : null;
  const lossPct = stage.lossPercentKg ?? stage.lossPercentMeter;
  // Phase 4: the figures the operation recorded beyond its output - the ACTUAL input the operator
  // entered, the separates (reprocessable material, explicitly not waste) and the second-grade
  // quantity (its own figure, neither output nor waste). They are shown back here so the floor can
  // read what it entered, and so the same figures the delivery statement reads are visible first.
  const input = stage.inputKg != null ? `${stage.inputKg} كجم` : stage.inputMeter != null ? `${stage.inputMeter} متر` : null;
  const separates = stage.separatesKg != null ? `${stage.separatesKg} كجم` : stage.separatesMeter != null ? `${stage.separatesMeter} متر` : null;
  const secondGrade = stage.secondGradeKg != null ? `${stage.secondGradeKg} كجم` : stage.secondGradeMeter != null ? `${stage.secondGradeMeter} متر` : null;
  // الهالك وزن: أصل الخام بالكيلو − وزن الجاهز بالكيلو − المنفصلات بالكيلو، محسوب على الخادم. يُعرض فقط
  // عندما تكون الأرقام التي يعتمد عليها مسجلة فعلًا - لا يُختلق صفر ولا تُعرض قيمة سالبة، ولا يوجد
  // «هالك بالمتر» لأن المتر طول وليس وزنًا.
  const waste = stage.wasteKg != null ? `${stage.wasteKg} كجم` : null;

  return (
    <div className="rounded-lg border border-line p-3">
      <div className="flex items-center justify-between gap-2">
        <div className="min-w-0">
          <span className="ltr-nums me-2 text-xs text-ink-subtle">#{stage.sequence}</span>
          <span className="font-medium">{stage.stageName}</span>
        </div>
        <Badge tone={stageStatusTone[stage.status]}>{stageStatusLabel[stage.status]}</Badge>
      </div>

      <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs sm:grid-cols-4">
        <div>
          <span className="text-ink-subtle">{t("stage.baseline")}: </span>
          <span className="ltr-nums">{baseline}</span>
        </div>
        {input && (
          <div>
            <span className="text-ink-subtle">{t("stage.inputKg")}: </span>
            <span className="ltr-nums">{input}</span>
          </div>
        )}
        {output && (
          <div>
            <span className="text-ink-subtle">{t("stage.outputKg")}: </span>
            <span className="ltr-nums">{output}</span>
          </div>
        )}
        {separates && (
          <div>
            <span className="text-ink-subtle">{t("stage.separatesKg")}: </span>
            <span className="ltr-nums">{separates}</span>
          </div>
        )}
        {secondGrade && (
          <div>
            <span className="text-ink-subtle">{t("stage.secondGradeKg")}: </span>
            <span className="ltr-nums">{secondGrade}</span>
          </div>
        )}
        {stage.secondGradePieceCount != null && (
          <div>
            <span className="text-ink-subtle">{t("stage.secondGradePieceCount")}: </span>
            <span className="ltr-nums">{stage.secondGradePieceCount}</span>
          </div>
        )}
        {waste && (
          <div>
            <span className="text-ink-subtle">{t("stage.waste")}: </span>
            <span className="ltr-nums">{waste}</span>
          </div>
        )}
        {loss && (
          <div>
            <span className="text-ink-subtle">{t("stage.loss")}: </span>
            <span className="ltr-nums">{loss}</span>
          </div>
        )}
        {lossPct != null && (
          <div>
            <span className="text-ink-subtle">{t("stage.lossPercent")}: </span>
            <span className="ltr-nums">{lossPct}%</span>
          </div>
        )}
      </div>

      {/* A closed stage keeps its figures; they are corrected with a compensating
          movement, never by re-entering them here (spec sections 15 and 18). */}
      {stage.status === "Completed" && (
        <p className="mt-2 flex items-center gap-1 text-2xs text-ink-subtle">
          <Lock size={12} /> {t("stage.locked")}
        </p>
      )}
    </div>
  );
}