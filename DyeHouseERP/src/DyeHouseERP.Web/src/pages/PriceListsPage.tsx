import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  PriceListsApi,
  ProductionStagesApi,
  CustomersApi,
  type StageCostRate,
  type CustomerServicePrice,
  type UnitOfMeasure
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Field, Badge } from "@/components/ui";
import { TableStateRow, TableWrap, errorMessage } from "@/components/ui";
import { useI18n } from "@/i18n";
import { Tag, Plus, Pencil, Power } from "lucide-react";

/**
 * The two commercial lists of spec section 34, kept visibly apart:
 *
 *   * ACTUAL COST LIST      - stage + unit -> what the stage costs US
 *   * CUSTOMER SERVICE LIST - stage + customer + unit -> what the CUSTOMER is charged
 *
 * Two things the screen has to make obvious, because conflating them is exactly
 * what the spec forbids: the general default price is a real, distinct row (no
 * customer), and editing either list never changes a Job Order that was already
 * priced - that order carries its own snapshot.
 *
 * There is no delete: an inactive rate still has to resolve for the orders it
 * priced, so the lifecycle is deactivate/reactivate.
 */

type Tab = "cost" | "price";

interface CostFormState {
  stageDefinitionId: string;
  unit: UnitOfMeasure;
  costPerUnit: string;
  notes: string;
}

interface PriceFormState {
  stageDefinitionId: string;
  customerId: string;
  unit: UnitOfMeasure;
  pricePerUnit: string;
  notes: string;
}

const emptyCostForm: CostFormState = { stageDefinitionId: "", unit: "KG", costPerUnit: "", notes: "" };
const emptyPriceForm: PriceFormState = { stageDefinitionId: "", customerId: "", unit: "KG", pricePerUnit: "", notes: "" };

export default function PriceListsPage() {
  const { t } = useI18n();
  const qc = useQueryClient();

  const [tab, setTab] = useState<Tab>("cost");
  const [activeOnly, setActiveOnly] = useState(false);
  const [stageFilter, setStageFilter] = useState("");
  const [customerFilter, setCustomerFilter] = useState("");
  const [generalOnly, setGeneralOnly] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [costForm, setCostForm] = useState<CostFormState | null>(null);
  const [priceForm, setPriceForm] = useState<PriceFormState | null>(null);

  const unitLabel = (unit: UnitOfMeasure) =>
    unit === "KG" ? t("common.kg", "Kilogram") : t("common.meter", "Meter");

  const { data: stages } = useQuery({
    queryKey: ["production-stages", "all"],
    queryFn: () => ProductionStagesApi.list()
  });

  const { data: customers } = useQuery({
    queryKey: ["customers", "all"],
    queryFn: () => CustomersApi.list({ activeOnly: true })
  });

  const costQuery = useQuery({
    queryKey: ["price-lists", "cost-rates", activeOnly, stageFilter],
    queryFn: () =>
      PriceListsApi.costRates({ activeOnly: activeOnly || undefined, stageDefinitionId: stageFilter || undefined }),
    enabled: tab === "cost"
  });

  const priceQuery = useQuery({
    queryKey: ["price-lists", "service-prices", activeOnly, stageFilter, customerFilter, generalOnly],
    queryFn: () =>
      PriceListsApi.servicePrices({
        activeOnly: activeOnly || undefined,
        stageDefinitionId: stageFilter || undefined,
        // generalOnly and a specific customer are mutually exclusive by design.
        generalOnly: generalOnly ? true : undefined,
        customerId: !generalOnly && customerFilter ? customerFilter : undefined
      }),
    enabled: tab === "price"
  });

  const invalidateCost = () => qc.invalidateQueries({ queryKey: ["price-lists", "cost-rates"] });
  const invalidatePrice = () => qc.invalidateQueries({ queryKey: ["price-lists", "service-prices"] });

  const saveCost = useMutation({
    mutationFn: (state: CostFormState) =>
      PriceListsApi.setCostRate({
        stageDefinitionId: state.stageDefinitionId,
        unit: state.unit,
        costPerUnit: Number(state.costPerUnit),
        notes: state.notes.trim() || undefined
      }),
    onSuccess: () => {
      invalidateCost();
      setCostForm(null);
      setFormError(null);
    },
    onError: (err) => setFormError(errorMessage(err, t("common.error")))
  });

  const savePrice = useMutation({
    mutationFn: (state: PriceFormState) =>
      PriceListsApi.setServicePrice({
        stageDefinitionId: state.stageDefinitionId,
        // An empty customer is the general default, not a missing value.
        customerId: state.customerId || null,
        unit: state.unit,
        pricePerUnit: Number(state.pricePerUnit),
        notes: state.notes.trim() || undefined
      }),
    onSuccess: () => {
      invalidatePrice();
      setPriceForm(null);
      setFormError(null);
    },
    onError: (err) => setFormError(errorMessage(err, t("common.error")))
  });

  const toggleCost = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      PriceListsApi.setCostRateActive(id, isActive),
    onSuccess: invalidateCost
  });

  const togglePrice = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      PriceListsApi.setServicePriceActive(id, isActive),
    onSuccess: invalidatePrice
  });

  const stageName = (id: string) => stages?.find((s) => s.id === id)?.name ?? "";

  const costRates: StageCostRate[] = costQuery.data ?? [];
  const servicePrices: CustomerServicePrice[] = priceQuery.data ?? [];

  /** Only stages that make sense as a priceable service - everything active. */
  const stageOptions = useMemo(() => stages?.filter((s) => s.isActive) ?? [], [stages]);

  const openCostForm = (rate?: StageCostRate) =>
    setCostForm(
      rate
        ? {
            stageDefinitionId: rate.stageDefinitionId,
            unit: rate.unit,
            costPerUnit: String(rate.costPerUnit),
            notes: rate.notes ?? ""
          }
        : { ...emptyCostForm }
    );

  const openPriceForm = (price?: CustomerServicePrice) =>
    setPriceForm(
      price
        ? {
            stageDefinitionId: price.stageDefinitionId,
            customerId: price.customerId ?? "",
            unit: price.unit,
            pricePerUnit: String(price.pricePerUnit),
            notes: price.notes ?? ""
          }
        : { ...emptyPriceForm }
    );

  const costFormValid =
    !!costForm && costForm.stageDefinitionId !== "" && costForm.costPerUnit !== "" && Number(costForm.costPerUnit) >= 0;
  const priceFormValid =
    !!priceForm && priceForm.stageDefinitionId !== "" && priceForm.pricePerUnit !== "" && Number(priceForm.pricePerUnit) >= 0;

  return (
    <div>
      <PageHeader
        title={t("pl.title", "Price lists")}
        subtitle={t("pl.subtitle")}
        action={
          tab === "cost" ? (
            <Button onClick={() => openCostForm()}>
              <Plus size={16} /> {t("pl.setCostRate")}
            </Button>
          ) : (
            <Button onClick={() => openPriceForm()}>
              <Plus size={16} /> {t("pl.setServicePrice")}
            </Button>
          )
        }
      />

      {/* Two lists, two tabs - the separation is the point, not a layout detail. */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Button variant={tab === "cost" ? "primary" : "secondary"} size="sm" onClick={() => setTab("cost")}>
          {t("pl.tabCost")}
        </Button>
        <Button variant={tab === "price" ? "primary" : "secondary"} size="sm" onClick={() => setTab("price")}>
          {t("pl.tabPrice")}
        </Button>
      </div>

      <Card className="mb-4">
        <div className="flex flex-wrap items-end gap-3">
          <Field label={t("pl.stage")}>
            <Select value={stageFilter} onChange={(e) => setStageFilter(e.target.value)}>
              <option value="">{t("common.all", "All")}</option>
              {stageOptions.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </Field>

          {tab === "price" && (
            <>
              <Field label={t("pl.customer")}>
                <Select
                  value={customerFilter}
                  disabled={generalOnly}
                  onChange={(e) => setCustomerFilter(e.target.value)}
                >
                  <option value="">{t("common.all", "All")}</option>
                  {(customers ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code} - {c.name}
                    </option>
                  ))}
                </Select>
              </Field>
              <label className="flex items-center gap-2 pb-2 text-sm">
                <input
                  type="checkbox"
                  checked={generalOnly}
                  onChange={(e) => setGeneralOnly(e.target.checked)}
                />
                {t("pl.generalDefault")}
              </label>
            </>
          )}

          <label className="flex items-center gap-2 pb-2 text-sm">
            <input type="checkbox" checked={activeOnly} onChange={(e) => setActiveOnly(e.target.checked)} />
            {t("pl.activeOnly")}
          </label>
        </div>
      </Card>

      {/* -------- Actual cost list -------- */}
      {tab === "cost" && (
        <>
          {costForm && (
            <Card className="mb-4">
              <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-ink">
                <Tag size={16} /> {t("pl.setCostRate")}
              </div>
              <div className="grid gap-3 sm:grid-cols-3">
                <Field label={t("pl.stage")}>
                  <Select
                    value={costForm.stageDefinitionId}
                    onChange={(e) => setCostForm({ ...costForm, stageDefinitionId: e.target.value })}
                  >
                    <option value="">{t("pl.selectStage")}</option>
                    {stageOptions.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label={t("common.unit")}>
                  <Select
                    value={costForm.unit}
                    onChange={(e) => setCostForm({ ...costForm, unit: e.target.value as UnitOfMeasure })}
                  >
                    <option value="KG">{t("common.kg", "Kilogram")}</option>
                    <option value="Meter">{t("common.meter", "Meter")}</option>
                  </Select>
                </Field>
                <Field label={t("pl.costPerUnit")}>
                  <Input
                    type="number"
                    min={0}
                    step="0.01"
                    value={costForm.costPerUnit}
                    onChange={(e) => setCostForm({ ...costForm, costPerUnit: e.target.value })}
                  />
                </Field>
              </div>
              <Field label={t("pl.notes")}>
                <Input value={costForm.notes} onChange={(e) => setCostForm({ ...costForm, notes: e.target.value })} />
              </Field>
              <p className="mt-2 text-xs text-ink-subtle">{t("pl.duplicateHint")}</p>
              {formError && <p className="mt-2 text-sm text-danger-ink">{formError}</p>}
              <div className="mt-3 flex gap-2">
                <Button
                  disabled={!costFormValid || saveCost.isPending}
                  onClick={() => costForm && saveCost.mutate(costForm)}
                >
                  {t("common.save")}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setCostForm(null);
                    setFormError(null);
                  }}
                >
                  {t("common.close")}
                </Button>
              </div>
            </Card>
          )}

          <Card>
            <TableWrap>
              <table className="table">
                <thead>
                  <tr>
                    <th>{t("pl.stage")}</th>
                    <th>{t("common.unit")}</th>
                    <th>{t("pl.costPerUnit")}</th>
                    <th>{t("common.status", "Status")}</th>
                    <th>{t("common.actions", "Actions")}</th>
                  </tr>
                </thead>
                <tbody>
                  <TableStateRow
                    colSpan={5}
                    loading={costQuery.isLoading}
                    error={costQuery.error}
                    isEmpty={costRates.length === 0}
                    emptyText={t("pl.emptyCostRates")}
                    loadingText={t("common.loading")}
                  />
                  {costRates.map((rate) => (
                    <tr key={rate.id}>
                      <td>
                        <div className="font-medium">{rate.stageName}</div>
                        <div className="text-xs text-ink-subtle">{rate.stageCode}</div>
                      </td>
                      <td>{unitLabel(rate.unit)}</td>
                      <td className="ltr-nums">{rate.costPerUnit}</td>
                      <td>
                        <Badge tone={rate.isActive ? "success" : "neutral"}>
                          {rate.isActive ? t("common.active", "Active") : t("common.inactive", "Inactive")}
                        </Badge>
                      </td>
                      <td>
                        <div className="flex gap-2">
                          <Button size="sm" variant="secondary" onClick={() => openCostForm(rate)}>
                            <Pencil size={14} /> {t("common.edit")}
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => toggleCost.mutate({ id: rate.id, isActive: !rate.isActive })}
                          >
                            <Power size={14} />
                            {rate.isActive ? t("common.deactivate", "Deactivate") : t("common.activate", "Activate")}
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableWrap>
          </Card>
        </>
      )}

      {/* -------- Customer service price list -------- */}
      {tab === "price" && (
        <>
          {priceForm && (
            <Card className="mb-4">
              <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-ink">
                <Tag size={16} /> {t("pl.setServicePrice")}
              </div>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <Field label={t("pl.stage")}>
                  <Select
                    value={priceForm.stageDefinitionId}
                    onChange={(e) => setPriceForm({ ...priceForm, stageDefinitionId: e.target.value })}
                  >
                    <option value="">{t("pl.selectStage")}</option>
                    {stageOptions.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label={t("pl.customer")}>
                  <Select
                    value={priceForm.customerId}
                    onChange={(e) => setPriceForm({ ...priceForm, customerId: e.target.value })}
                  >
                    {/* Empty = the general default row, which is why it is labelled, not "none". */}
                    <option value="">{t("pl.generalDefault")}</option>
                    {(customers ?? []).map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} - {c.name}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label={t("common.unit")}>
                  <Select
                    value={priceForm.unit}
                    onChange={(e) => setPriceForm({ ...priceForm, unit: e.target.value as UnitOfMeasure })}
                  >
                    <option value="KG">{t("common.kg", "Kilogram")}</option>
                    <option value="Meter">{t("common.meter", "Meter")}</option>
                  </Select>
                </Field>
                <Field label={t("pl.pricePerUnit")}>
                  <Input
                    type="number"
                    min={0}
                    step="0.01"
                    value={priceForm.pricePerUnit}
                    onChange={(e) => setPriceForm({ ...priceForm, pricePerUnit: e.target.value })}
                  />
                </Field>
              </div>
              <Field label={t("pl.notes")}>
                <Input value={priceForm.notes} onChange={(e) => setPriceForm({ ...priceForm, notes: e.target.value })} />
              </Field>
              <p className="mt-2 text-xs text-ink-subtle">{t("pl.generalHint")}</p>
              {formError && <p className="mt-2 text-sm text-danger-ink">{formError}</p>}
              <div className="mt-3 flex gap-2">
                <Button
                  disabled={!priceFormValid || savePrice.isPending}
                  onClick={() => priceForm && savePrice.mutate(priceForm)}
                >
                  {t("common.save")}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setPriceForm(null);
                    setFormError(null);
                  }}
                >
                  {t("common.close")}
                </Button>
              </div>
            </Card>
          )}

          <Card>
            <TableWrap>
              <table className="table">
                <thead>
                  <tr>
                    <th>{t("pl.stage")}</th>
                    <th>{t("pl.customer")}</th>
                    <th>{t("common.unit")}</th>
                    <th>{t("pl.pricePerUnit")}</th>
                    <th>{t("common.status", "Status")}</th>
                    <th>{t("common.actions", "Actions")}</th>
                  </tr>
                </thead>
                <tbody>
                  <TableStateRow
                    colSpan={6}
                    loading={priceQuery.isLoading}
                    error={priceQuery.error}
                    isEmpty={servicePrices.length === 0}
                    emptyText={t("pl.emptyPrices")}
                    loadingText={t("common.loading")}
                  />
                  {servicePrices.map((price) => (
                    <tr key={price.id}>
                      <td>
                        <div className="font-medium">{price.stageName ?? stageName(price.stageDefinitionId)}</div>
                        <div className="text-xs text-ink-subtle">{price.stageCode}</div>
                      </td>
                      <td>
                        {price.isGeneralDefault ? (
                          <Badge tone="info">{t("pl.generalDefault")}</Badge>
                        ) : (
                          <span>
                            {price.customerCode} - {price.customerName}
                          </span>
                        )}
                      </td>
                      <td>{unitLabel(price.unit)}</td>
                      <td className="ltr-nums">{price.pricePerUnit}</td>
                      <td>
                        <Badge tone={price.isActive ? "success" : "neutral"}>
                          {price.isActive ? t("common.active", "Active") : t("common.inactive", "Inactive")}
                        </Badge>
                      </td>
                      <td>
                        <div className="flex gap-2">
                          <Button size="sm" variant="secondary" onClick={() => openPriceForm(price)}>
                            <Pencil size={14} /> {t("common.edit")}
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => togglePrice.mutate({ id: price.id, isActive: !price.isActive })}
                          >
                            <Power size={14} />
                            {price.isActive ? t("common.deactivate", "Deactivate") : t("common.activate", "Activate")}
                          </Button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableWrap>
          </Card>
        </>
      )}

      <p className="mt-4 text-xs text-ink-subtle">{t("pl.noDeleteNote")}</p>
      <p className="mt-1 text-xs text-ink-subtle">{t("pl.footerNote")}</p>
    </div>
  );
}
