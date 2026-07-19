# KPI Definitions

Authoritative definitions for the management KPI dashboard (`GET /api/kpi/dashboard`), the
trip-profitability report (`GET /api/kpi/trip-profitability`) and the XLSX exports. All
values are computed server-side in `Modules/Reporting/Services/KpiQueryService.cs` and unit
tested in `TransportationService.Api.Tests/Reporting/KpiQueryServiceTests.cs`. Every ratio is
zero-denominator-safe: when the denominator is 0 the KPI is `null` (rendered as "—"), never a
division error.

## Scope and filters

- **Date range** (`from`..`to`, inclusive, max 366 days) filters trips on `TripDate` and fuel
  transactions on `TransactionDate`. Bounded at 5 000 trips per query.
- **Cancelled trips are excluded** from every KPI (they occupy no capacity and carry no
  revenue); cancelled orders are excluded from revenue.
- **Customer filter**: only trips carrying ≥1 order of that customer count. Revenue is
  restricted to the matching orders; trip cost is allocated by the matched revenue share
  (equal split across orders when the trip has zero revenue).
- **Driver/vehicle filters** restrict on the trip's assigned driver/vehicle; fuel KPIs follow
  the same filters via the transaction's vehicle/driver.
- **"Vandaag" cards** evaluate `TripDate == today` within the selected range; if today lies
  outside the range they are 0 by definition.

## Financial

| KPI | Definition |
|---|---|
| Revenue (period/today) | Σ `AgreedPrice ?? 0` of non-cancelled orders on non-cancelled trips in range. |
| Trip cost | `FinalCost` when finalized, else `ProjectedTotal` (per-cost-type actual-over-estimate merge), else `EstimatedTotal`, from `trip_cost_summaries`; 0 when no costing exists. |
| Profit (period/today) | Revenue − cost (allocated by customer share when filtered). |
| Average margin % | Σ profit / Σ revenue × 100 — the volume-weighted margin, NOT the average of per-trip margins. `null` when revenue is 0. |
| Profit per trip | Σ profit / trip count; `null` when no trips. |
| Cost overrun | Per completed trip with a frozen `FinalCost` and `EstimatedTotal > 0`: `(final − estimated) / estimated × 100`. The dashboard shows the count of trips with `final > estimated` and the average overrun % over all eligible trips. |

## Fleet & kilometres

| KPI | Definition |
|---|---|
| Total km | Σ per trip `ActualDistanceKm ?? PlannedDistanceKm ?? 0`. |
| Empty km | Σ per trip `ActualEmptyKm ?? PlannedEmptyKm ?? 0`. |
| Empty-km % | empty / total × 100; `null` when total is 0. |
| Vehicle utilisation % | Σ active trip hours / available hours × 100. Active hours per trip: planning-entry `ActualStart..ActualEnd` when known, else `PlannedStart..PlannedEnd`, else the trip contributes 0. Available hours = active vehicles × Mon–Fri workdays in the range × 8 h (fixed convention). `null` when available hours is 0. |
| Km per driver | Total km grouped by the trip's driver, top 10, with the same hours definition. |
| Fuel litres / cost | Σ `Litres` / Σ `TotalAmount` of fuel transactions in range (vehicle/driver filters apply). |
| CO₂ (kg) | Σ litres × factor by the vehicle's fuel type: Diesel → `Co2KgPerLitreDiesel` (default 2.68), Electric/Hydrogen → 0, everything else → `Co2KgPerLitreOther` (default 2.31). Factors come from the rate card effective on the range end date. |
| Open damage cases | Damage reports with status other than Repaired/Closed (not date-filtered — open right now; vehicle filter applies). |

## Execution

| KPI | Definition |
|---|---|
| Delivery reliability % | Terminal-`Completed` stop executions on unloading stops / all unloading stops of non-cancelled trips in range × 100. Skipped/Failed/PartiallyCompleted do not count as successful. `null` when there are no unloading stops. |
| On-time arrival % | Of stop executions with an `ArrivedAt` AND an applicable window (`ConfirmedTo ?? RequestedTo ?? PlannedTo`): arrivals with `ArrivedAt <= window` / eligible arrivals × 100. Stops without any window are excluded. |
| Avg ETA deviation (min) | Per arrived stop: `ArrivedAt − Eta` of the newest `StopEtaHistory` snapshot recorded at/before arrival (positive = later than promised). Stops without an ETA history snapshot are excluded. Average over eligible stops; `null` when none. |
| Failed / partial deliveries | Count of stop executions with status `Failed` / `PartiallyCompleted` on trips in range. |
| Open operational exceptions | Execution exceptions on trips in range with a non-terminal status (not Resolved/Rejected). |

## Trip profitability report

One row per non-cancelled trip in range: revenue, estimated/projected/final cost, profit
(revenue − final-else-projected-else-estimated), margin % (`null` at zero revenue), total and
empty km, driver, vehicle, customers and finalized flag. The same rows feed the XLSX exports.
