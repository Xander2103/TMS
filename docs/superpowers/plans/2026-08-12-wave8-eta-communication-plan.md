# Wave 8 — ETA + Customer Communication Implementation Note

Scope check against the existing ETA machinery (route-provider seam, ETA statuses/overrides/
history, outside-window customer messaging, FR/EN templates since Wave 2): three gaps remained.

## 1. Historical stop-duration estimates

`EtaService.RecalculateTripAsync` now derives handling minutes per LOCATION from measured
`StopExecution` actuals (DepartedAt − ArrivedAt, last 90 days, ≥3 samples, clamped 5..240
min); the tenant defaults stay the fallback. Dispatcher overrides and the provider seam are
untouched.

## 2. ETA-shift threshold messaging

`TenantSettings.EtaShiftNotifyMinutes` (nullable, additive; null = off): when set, an ETA
move of at least X minutes messages the customer (same EtaUpdate template, same outbox,
localized since Wave 2) — also while the stop is still ON TIME. The existing outside-window
messaging is unchanged. Configuration: tenant setting (API/beheer); no separate UI field yet
(bewuste keuze — het veld is tenant-breed en zeldzaam gewijzigd).

## 3. Portal ETA

`PortalOrderDetailDto.ExpectedDeliveryEta` (additive): the live ETA of the last unloading
stop, straight from the dispatcher's ETA rows (overrides included), shown on the portal
order detail in NL/FR/EN ("Verwachte levering / Livraison prévue / Expected delivery").

Migration: EtaShiftThreshold (TenantSettings column) — applied.
