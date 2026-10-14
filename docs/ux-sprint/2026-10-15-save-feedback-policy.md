# Save feedback policy (B31, UX wave 3A)

One save gets one kind of feedback: a toast or an inline save state, never both.

## Inline save state (`SaveStatus`)

Use it for **in-place edits**: the panel stays on screen and the user may edit and save again.

| State  | Text                                   | Role     |
| ------ | -------------------------------------- | -------- |
| saving | `Opslaan…`                             | `status` |
| saved  | `✓ Opgeslagen · 14:22`                 | `status` |
| dirty  | `Niet-opgeslagen wijzigingen`          | `status` |
| error  | `Opslaan mislukt · Opnieuw proberen`   | `alert`  |

- Put it next to the save button, or in the section header when the save button is far away. On a
  long form it goes in the sticky action bar.
- The saved line stays visible after the moment a toast would have disappeared.
- An error replaces the toast. It shows the specific message when there is one, and
  `Opnieuw proberen` re-runs the same save.
- Pending edits (`dirty`) replace an older `Opgeslagen`. A failure always outranks `dirty`.
- If the input is refused (400/422) or there is a version conflict (409), the field errors, the
  validation summary or the conflict banner explain it. A retry would not help there, so the inline
  status goes back to neutral (`isRetryableSaveError`).

Building blocks: `src/components/ui/SaveStatus.tsx`, `src/components/ui/useSaveStatus.ts`
(`useSaveStatus()` → `{ state, setState, run, retry }`, `isRetryableSaveError(err)`).

## Toast

A toast stays the right tool when:

- the user **leaves the context**: the dialog or editor closes, or the page navigates away
  (e.g. customer edit form → read view, PO-number dialog, price-rule dialog);
- an **object is created** (new dossier, order, customer, employee, document upload);
- a **destructive action** completes (delete, remove price, archive, deactivate);
- a **background job** finishes (backup created, bulk update, import);
- something **needs attention** outside the panel (e.g. client-side validation that blocks the
  save on a settings table).

## Paths that now follow the policy

| Path | File | Before | Now |
| --- | --- | --- | --- |
| Trip assignment (driver/vehicle/trailer/orders) | `features/planning/pages/TripDetailPage.tsx` | own "Opgeslagen" line **plus** a success toast; errors as a toast | shared `SaveStatus` in the assignment header; error inline with retry; no toasts |
| Dossier route | `features/dossiers/components/DossierRouteEditor.tsx` | "Route opgeslagen." toast | `SaveStatus` beside "Route opslaan"; API failure → "Opslaan mislukt · Opnieuw proberen" (details stay in the validation summary); conflict → banner only |
| Agreed price (order) | `features/dossiers/pricing/DossierOrderPricePanel.tsx` | toast | `SaveStatus` beside the price field; retry re-sends the last amount |
| Agreed price (activity) | `features/dossiers/components/DossierActivityPricePanel.tsx` | toast | idem. "Confirm as free" and "remove price" keep their toasts (confirmed actions) |
| Activity sales lines | `features/dossiers/pricing/ActivityPriceLinesEditor.tsx` | toast | `SaveStatus` with `dirty` next to the line actions |
| Company settings | `features/settings/pages/SettingsPage.tsx` | success and error toasts | `SaveStatus` in the sticky action bar, error with retry |
| Document rules / charge policies | `features/settings/pages/DocumentRulesPage.tsx`, `ChargePoliciesPage.tsx` | success and error toasts | `SaveStatus` beside "Opslaan"; client validation keeps its toast |
| Customer diesel surcharge, PO policy | `features/customers/components/CustomerBillingPanel.tsx` | toasts | `SaveStatus` beside the save button / under the policy select |
| Employee profile (edit) | `features/employees/pages/EmployeeDetailPage.tsx` (+ `saveStatus` slot in `EmployeeForm`) | "Medewerker bijgewerkt" toast | `SaveStatus` in the form's sticky action bar; retry re-submits the last values |

These keep their toasts on purpose: customer edit (the form closes), all create, delete and
dialog flows, trip status transitions, and backups.
