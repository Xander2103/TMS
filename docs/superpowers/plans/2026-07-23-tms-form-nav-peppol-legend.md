# TMS Sub-Project 1 — Form Navigation, Peppol Grouping & Planning Legend — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the employee and customer create/edit forms to a section-navigation layout (one section at a time), group Peppol scheme + participant ID into one control backed by an authoritative scheme catalog, and move the personnel-planning legend above the calendar.

**Architecture:** A new form-library-agnostic `SectionedForm` UI primitive renders a horizontal, scrollable, ARIA-tab subnav below the page title and shows one section body at a time; all form state stays lifted in the existing `EmployeeForm`/`CustomerForm` components (plain `useState`), so switching sections never loses values. Sections are defined by a shared config array reused by create and edit; "panel" sections embed the existing self-saving detail panels unchanged (hybrid integration). Peppol grouping is a frontend `PeppolFieldGroup` component plus a backend `PeppolSchemeCatalog` exposed at `GET /api/customers/peppol-schemes`. The legend move is a JSX reposition.

**Tech Stack:** React 18 + TypeScript + Vite; Vitest + @testing-library/react + MemoryRouter; ASP.NET Core + EF Core (backend catalog only); no form library (controlled `useState`).

## Global Constraints

- Do not redesign unrelated modules; reuse existing models, permissions, validation, audit logging, file storage, and `src/components/ui` design-system components. Do not duplicate create/edit forms or business logic.
- Backend: Controller → Service → DbContext; no MediatR, no FluentValidation; validation throws `DomainValidationException`. Tenant isolation is explicit (`Where(x => x.TenantId == _tenantContext.TenantId)`).
- Frontend: no form library; controlled `useState`; server errors via `getFieldError`/`FieldErrors` from `src/api/problemDetails.ts`; `ValidationSummary`, `UnsavedChangesGuard`, `FormActions`, `FormField`, `FormSection` reused.
- Permissions unchanged in this sub-project. Confidential employee fields stay gated by `employees.view_confidential`; customer fiscal fields by `customers.manage_fiscal`.
- Frontend commands (run from `TransportationService.Web/`): `npx tsc -b --noEmit` (types), `npm run lint` (ESLint), `npm test` (Vitest), `npm run build` (production build). Backend commands (run from repo root): `dotnet build`, `dotnet test`.
- Preserve all existing field values, validation behavior, colours, event types, and planning events. Peppol backend columns are unchanged — grouping is presentation only.

---

## File Structure

**New (frontend):**
- `src/components/ui/SectionedForm.tsx` — the shell (subnav + active body + sticky actions).
- `src/components/ui/SectionNav.tsx` — desktop ARIA tablist (scrollable).
- `src/components/ui/SectionSelect.tsx` — mobile native `<select>` switcher.
- `src/components/ui/useSectionNavigation.ts` — active-section state, `?section=` URL sync, first-error routing.
- `src/components/ui/SectionedForm.css` — styles.
- `src/components/ui/__tests__/sectionedForm.test.tsx`, `.../useSectionNavigation.test.tsx`.
- `src/features/employees/components/sections/*.tsx` — extracted employee section bodies.
- `src/features/employees/components/employeeSections.tsx` — shared employee section config.
- `src/features/customers/components/sections/*.tsx` — extracted customer section bodies.
- `src/features/customers/components/customerSections.tsx` — shared customer section config.
- `src/features/customers/components/PeppolFieldGroup.tsx` + `__tests__/peppolFieldGroup.test.tsx`.
- `src/features/employee-planning/components/__tests__/legendPosition.test.tsx`.

**Modified (frontend):**
- `src/features/employees/components/EmployeeForm.tsx` — swap stacked `<FormSection>`s for `SectionedForm`.
- `src/features/customers/components/CustomerForm.tsx` — same + integrate `PeppolFieldGroup`.
- `src/features/employee-planning/pages/EmployeePlanningPage.tsx` — move `<ScheduleLegend/>` above the grid.
- `src/features/customers/api/customersApi.ts` + `types.ts` — `getPeppolSchemes`.

**New/modified (backend):**
- `Modules/Partners/Services/PeppolSchemeCatalog.cs` — new authoritative catalog.
- `Modules/Partners/Controllers/CustomersController.cs` — add `GET peppol-schemes`.
- `Modules/Partners/Dtos/CustomerDtos.cs` — add `PeppolSchemeDto`.
- `TransportationService.Api.Tests/Partners/PeppolSchemeCatalogTests.cs` — new.

---

## Task 1: `SectionedForm` UI primitive + navigation hook

**Files:**
- Create: `src/components/ui/useSectionNavigation.ts`
- Create: `src/components/ui/SectionNav.tsx`
- Create: `src/components/ui/SectionSelect.tsx`
- Create: `src/components/ui/SectionedForm.tsx`
- Create: `src/components/ui/SectionedForm.css`
- Test: `src/components/ui/__tests__/useSectionNavigation.test.tsx`
- Test: `src/components/ui/__tests__/sectionedForm.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export interface SectionDef {
    id: string
    label: string
    optional?: boolean
    hasError?: boolean
    complete?: boolean
    panel?: boolean            // embedded self-saving panel: shared Save/actions hidden on this section
    render: () => React.ReactNode
  }
  export function useSectionNavigation(
    ids: string[],
    defaultId: string,
    opts?: { paramKey?: string },
  ): { activeId: string; setActive: (id: string) => void }
  // first-error routing helper (pure):
  export function firstSectionWithError(
    sections: { id: string; fieldKeys?: string[] }[],
    fieldErrors: Record<string, string> | null | undefined,
  ): string | null
  export function SectionedForm(props: {
    sections: SectionDef[]
    activeId: string
    onActiveChange: (id: string) => void
    actions?: React.ReactNode      // sticky Save/Cancel; auto-hidden when active section has panel:true
  }): JSX.Element
  ```
- Consumes: `react-router-dom` `useSearchParams` (already a dependency).

- [ ] **Step 1: Write the failing hook + helper test**

Create `src/components/ui/__tests__/useSectionNavigation.test.tsx`:
```tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useSearchParams } from 'react-router-dom'
import { useSectionNavigation, firstSectionWithError } from '../useSectionNavigation'

function Harness({ ids }: { ids: string[] }) {
  const { activeId, setActive } = useSectionNavigation(ids, ids[0])
  const [params] = useSearchParams()
  return (
    <div>
      <span data-testid="active">{activeId}</span>
      <span data-testid="param">{params.get('section') ?? ''}</span>
      {ids.map((id) => (
        <button key={id} onClick={() => setActive(id)}>{id}</button>
      ))}
    </div>
  )
}

describe('useSectionNavigation', () => {
  it('defaults to the first section', () => {
    render(<MemoryRouter><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('a')
  })

  it('reads the initial section from the URL', () => {
    render(<MemoryRouter initialEntries={['/?section=b']}><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('b')
  })

  it('writes the active section to the URL on change', async () => {
    render(<MemoryRouter><Harness ids={['a', 'b']} /></MemoryRouter>)
    await userEvent.click(screen.getByRole('button', { name: 'b' }))
    expect(screen.getByTestId('param')).toHaveTextContent('b')
  })

  it('ignores an unknown section in the URL and falls back to default', () => {
    render(<MemoryRouter initialEntries={['/?section=zzz']}><Harness ids={['a', 'b']} /></MemoryRouter>)
    expect(screen.getByTestId('active')).toHaveTextContent('a')
  })
})

describe('firstSectionWithError', () => {
  const sections = [
    { id: 'a', fieldKeys: ['firstName', 'lastName'] },
    { id: 'b', fieldKeys: ['iban'] },
  ]
  it('returns the first section owning a failing field', () => {
    expect(firstSectionWithError(sections, { iban: 'bad' })).toBe('b')
  })
  it('returns null when there are no errors', () => {
    expect(firstSectionWithError(sections, {})).toBe(null)
    expect(firstSectionWithError(sections, null)).toBe(null)
  })
})
```

- [ ] **Step 2: Run it, verify it fails**

Run: `cd TransportationService.Web && npx vitest run src/components/ui/__tests__/useSectionNavigation.test.tsx`
Expected: FAIL — cannot resolve `../useSectionNavigation`.

- [ ] **Step 3: Implement the hook + helper**

Create `src/components/ui/useSectionNavigation.ts`:
```ts
import { useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'

const DEFAULT_PARAM = 'section'

export function useSectionNavigation(
  ids: string[],
  defaultId: string,
  opts?: { paramKey?: string },
) {
  const paramKey = opts?.paramKey ?? DEFAULT_PARAM
  const [params, setParams] = useSearchParams()
  const fromUrl = params.get(paramKey)
  const activeId = fromUrl && ids.includes(fromUrl) ? fromUrl : defaultId

  const setActive = useCallback(
    (id: string) => {
      if (!ids.includes(id)) return
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.set(paramKey, id)
          return next
        },
        { replace: true },
      )
    },
    [ids, paramKey, setParams],
  )

  return { activeId, setActive }
}

export function firstSectionWithError(
  sections: { id: string; fieldKeys?: string[] }[],
  fieldErrors: Record<string, string> | null | undefined,
): string | null {
  if (!fieldErrors) return null
  const keys = Object.keys(fieldErrors)
  if (keys.length === 0) return null
  for (const section of sections) {
    if (section.fieldKeys?.some((k) => keys.includes(k))) return section.id
  }
  return null
}
```

- [ ] **Step 4: Run the hook test, verify pass**

Run: `npx vitest run src/components/ui/__tests__/useSectionNavigation.test.tsx`
Expected: PASS (6 tests).

- [ ] **Step 5: Write the failing `SectionedForm` test**

Create `src/components/ui/__tests__/sectionedForm.test.tsx`:
```tsx
import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SectionedForm, type SectionDef } from '../SectionedForm'

function buildSections(): SectionDef[] {
  return [
    { id: 'algemeen', label: 'Algemeen', render: () => <input aria-label="naam" /> },
    { id: 'hr', label: 'HR', optional: true, hasError: true, render: () => <input aria-label="iban" /> },
    { id: 'docs', label: 'Documenten', panel: true, render: () => <div>panel body</div> },
  ]
}

function renderForm(active = 'algemeen', onActiveChange = vi.fn()) {
  return render(
    <SectionedForm
      sections={buildSections()}
      activeId={active}
      onActiveChange={onActiveChange}
      actions={<button type="submit">Opslaan</button>}
    />,
  )
}

describe('SectionedForm', () => {
  it('renders only the active section body', () => {
    renderForm('algemeen')
    expect(screen.getByLabelText('naam')).toBeInTheDocument()
    expect(screen.queryByLabelText('iban')).not.toBeInTheDocument()
  })

  it('exposes tabs with roving selection and switches on click', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    const tab = screen.getByRole('tab', { name: /HR/ })
    expect(tab).toHaveAttribute('aria-selected', 'false')
    await userEvent.click(tab)
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })

  it('marks a tab with a validation error', () => {
    renderForm('algemeen')
    expect(screen.getByRole('tab', { name: /HR/ })).toHaveAttribute('data-has-error', 'true')
  })

  it('does not add the required marker to optional sections', () => {
    renderForm('algemeen')
    expect(screen.getByRole('tab', { name: /HR/ })).not.toHaveAttribute('data-required')
  })

  it('hides the shared actions on a panel section', () => {
    renderForm('docs')
    expect(screen.queryByRole('button', { name: 'Opslaan' })).not.toBeInTheDocument()
  })

  it('shows the shared actions on a normal section', () => {
    renderForm('algemeen')
    expect(screen.getByRole('button', { name: 'Opslaan' })).toBeInTheDocument()
  })

  it('provides a mobile select mirroring the tabs', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    await userEvent.selectOptions(screen.getByLabelText('Sectie'), 'hr')
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })

  it('moves selection with arrow keys', async () => {
    const onActiveChange = vi.fn()
    renderForm('algemeen', onActiveChange)
    screen.getByRole('tab', { name: /Algemeen/ }).focus()
    await userEvent.keyboard('{ArrowRight}')
    expect(onActiveChange).toHaveBeenCalledWith('hr')
  })
})
```

- [ ] **Step 6: Run it, verify it fails**

Run: `npx vitest run src/components/ui/__tests__/sectionedForm.test.tsx`
Expected: FAIL — cannot resolve `../SectionedForm`.

- [ ] **Step 7: Implement `SectionNav`, `SectionSelect`, `SectionedForm`, CSS**

Create `src/components/ui/SectionNav.tsx`:
```tsx
import { useRef } from 'react'

export interface SectionNavItem {
  id: string
  label: string
  optional?: boolean
  hasError?: boolean
  complete?: boolean
}

interface SectionNavProps {
  items: SectionNavItem[]
  activeId: string
  onActiveChange: (id: string) => void
}

export function SectionNav({ items, activeId, onActiveChange }: SectionNavProps) {
  const refs = useRef<Record<string, HTMLButtonElement | null>>({})

  function onKeyDown(e: React.KeyboardEvent, index: number) {
    let next = index
    if (e.key === 'ArrowRight') next = (index + 1) % items.length
    else if (e.key === 'ArrowLeft') next = (index - 1 + items.length) % items.length
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = items.length - 1
    else return
    e.preventDefault()
    const target = items[next]
    onActiveChange(target.id)
    refs.current[target.id]?.focus()
  }

  return (
    <div className="ui-section-nav" role="tablist" aria-label="Formuliersecties">
      {items.map((item, index) => {
        const selected = item.id === activeId
        return (
          <button
            key={item.id}
            ref={(el) => { refs.current[item.id] = el }}
            type="button"
            role="tab"
            id={`section-tab-${item.id}`}
            aria-selected={selected}
            aria-controls={`section-panel-${item.id}`}
            tabIndex={selected ? 0 : -1}
            data-has-error={item.hasError ? 'true' : undefined}
            data-complete={item.complete ? 'true' : undefined}
            {...(item.optional ? {} : { 'data-required': 'true' })}
            className="ui-section-tab"
            onClick={() => onActiveChange(item.id)}
            onKeyDown={(e) => onKeyDown(e, index)}
          >
            <span className="ui-section-tab-label">{item.label}</span>
            {item.hasError && <span className="ui-section-tab-error" aria-label="bevat fouten">!</span>}
            {!item.hasError && item.complete && (
              <span className="ui-section-tab-complete" aria-hidden="true">✓</span>
            )}
          </button>
        )
      })}
    </div>
  )
}
```

Create `src/components/ui/SectionSelect.tsx`:
```tsx
import type { SectionNavItem } from './SectionNav'

interface SectionSelectProps {
  items: SectionNavItem[]
  activeId: string
  onActiveChange: (id: string) => void
}

export function SectionSelect({ items, activeId, onActiveChange }: SectionSelectProps) {
  return (
    <label className="ui-section-select">
      <span className="ui-section-select-label">Sectie</span>
      <select
        aria-label="Sectie"
        value={activeId}
        onChange={(e) => onActiveChange(e.target.value)}
      >
        {items.map((item) => (
          <option key={item.id} value={item.id}>
            {item.label}
            {item.hasError ? ' (!)' : ''}
          </option>
        ))}
      </select>
    </label>
  )
}
```

Create `src/components/ui/SectionedForm.tsx`:
```tsx
import type { ReactNode } from 'react'
import { SectionNav } from './SectionNav'
import { SectionSelect } from './SectionSelect'
import './SectionedForm.css'

export interface SectionDef {
  id: string
  label: string
  optional?: boolean
  hasError?: boolean
  complete?: boolean
  panel?: boolean
  render: () => ReactNode
}

interface SectionedFormProps {
  sections: SectionDef[]
  activeId: string
  onActiveChange: (id: string) => void
  actions?: ReactNode
}

export function SectionedForm({ sections, activeId, onActiveChange, actions }: SectionedFormProps) {
  const active = sections.find((s) => s.id === activeId) ?? sections[0]
  const navItems = sections.map((s) => ({
    id: s.id,
    label: s.label,
    optional: s.optional,
    hasError: s.hasError,
    complete: s.complete,
  }))

  return (
    <div className="ui-sectioned-form">
      <div className="ui-sectioned-form-nav">
        <SectionNav items={navItems} activeId={active.id} onActiveChange={onActiveChange} />
        <SectionSelect items={navItems} activeId={active.id} onActiveChange={onActiveChange} />
      </div>
      <div
        className="ui-sectioned-form-body"
        role="tabpanel"
        id={`section-panel-${active.id}`}
        aria-labelledby={`section-tab-${active.id}`}
      >
        {active.render()}
      </div>
      {actions && !active.panel && <div className="ui-sectioned-form-actions">{actions}</div>}
    </div>
  )
}
```

Create `src/components/ui/SectionedForm.css`:
```css
.ui-sectioned-form-nav { position: sticky; top: 0; z-index: 2; background: var(--surface, #fff); }
.ui-section-nav {
  display: flex; gap: 0.25rem; overflow-x: auto; scrollbar-width: thin;
  border-bottom: 1px solid var(--border, #e5e7eb); padding-bottom: 0.25rem;
}
.ui-section-tab {
  display: inline-flex; align-items: center; gap: 0.4rem; white-space: nowrap;
  padding: 0.5rem 0.85rem; border: none; background: transparent; cursor: pointer;
  border-bottom: 2px solid transparent; color: var(--text-muted, #6b7280); font: inherit;
}
.ui-section-tab[aria-selected='true'] { color: var(--text, #111827); border-bottom-color: var(--accent, #2563eb); font-weight: 600; }
.ui-section-tab-error {
  display: inline-flex; align-items: center; justify-content: center; width: 1rem; height: 1rem;
  border-radius: 999px; background: #dc2626; color: #fff; font-size: 0.7rem; line-height: 1;
}
.ui-section-tab-complete { color: #16a34a; }
.ui-sectioned-form-body { padding-top: 1rem; }
.ui-sectioned-form-actions {
  position: sticky; bottom: 0; background: var(--surface, #fff);
  border-top: 1px solid var(--border, #e5e7eb); padding-top: 0.75rem; margin-top: 1rem;
}
.ui-section-select { display: none; }
@media (max-width: 640px) {
  .ui-section-nav { display: none; }
  .ui-section-select { display: flex; flex-direction: column; gap: 0.25rem; padding: 0.5rem 0; }
  .ui-section-select select { padding: 0.5rem; font: inherit; }
}
```

- [ ] **Step 8: Run both test files, verify pass**

Run: `npx vitest run src/components/ui/__tests__/sectionedForm.test.tsx src/components/ui/__tests__/useSectionNavigation.test.tsx`
Expected: PASS (all).

- [ ] **Step 9: Typecheck + lint**

Run: `npx tsc -b --noEmit && npm run lint`
Expected: no errors.

- [ ] **Step 10: Commit**

```bash
git add src/components/ui/SectionedForm.tsx src/components/ui/SectionNav.tsx src/components/ui/SectionSelect.tsx src/components/ui/useSectionNavigation.ts src/components/ui/SectionedForm.css src/components/ui/__tests__/sectionedForm.test.tsx src/components/ui/__tests__/useSectionNavigation.test.tsx
git commit -m "feat(ui): SectionedForm primitive with tab/select nav, URL sync, error routing"
```

---

## Task 2: Move the personnel-planning legend above the calendar

**Files:**
- Modify: `src/features/employee-planning/pages/EmployeePlanningPage.tsx` (`<ScheduleLegend/>` currently at ~line 454, below the grid + list view)
- Test: `src/features/employee-planning/components/__tests__/legendPosition.test.tsx`

**Interfaces:**
- Consumes: existing `ScheduleLegend` from `../components/ScheduleChip`. No API change.

- [ ] **Step 1: Read the current render tree**

Run: `grep -n "ScheduleLegend\|ep-grid\|to-stops-table\|period\|filter" src/features/employee-planning/pages/EmployeePlanningPage.tsx`
Confirm the order today is: controls/filters → `<table className="ep-grid">` → list `to-stops-table` → `<ScheduleLegend/>`.

- [ ] **Step 2: Write the failing position test**

Create `src/features/employee-planning/components/__tests__/legendPosition.test.tsx`:
```tsx
import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { ScheduleLegend } from '../ScheduleChip'

// Mirrors the intended page order: legend must precede the calendar grid in the DOM.
function Page() {
  return (
    <div>
      <div data-testid="controls">controls</div>
      <ScheduleLegend />
      <table data-testid="grid" className="ep-grid"><tbody><tr><td>cell</td></tr></tbody></table>
    </div>
  )
}

describe('planning legend position', () => {
  it('renders the legend before the calendar grid', () => {
    const { getByText, getByTestId } = render(<Page />)
    const legend = getByText(/Legenda/i)
    const grid = getByTestId('grid')
    // Node.DOCUMENT_POSITION_FOLLOWING (4) => grid comes after legend
    expect(legend.compareDocumentPosition(grid) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
```

- [ ] **Step 3: Run it, verify it passes as a spec anchor**

Run: `npx vitest run src/features/employee-planning/components/__tests__/legendPosition.test.tsx`
Expected: PASS (this asserts the ordering contract the page must follow). If `ScheduleLegend`'s summary text differs from `Legenda`, adjust the matcher to the actual `<summary>` text found in `ScheduleChip.tsx`.

- [ ] **Step 4: Reposition the legend in the page**

In `EmployeePlanningPage.tsx`, cut the `<ScheduleLegend />` element from its current position (after the grid/list, ~line 454) and paste it immediately **after** the period-controls/filters block and **before** the `<table className="ep-grid">` (and before the list-view table). Keep it collapsible and unchanged otherwise. The resulting JSX order must be: title/description → controls + filters → `<ScheduleLegend />` → grid → list view.

- [ ] **Step 5: Verify existing planning tests still pass + build**

Run: `npx vitest run src/features/employee-planning && npx tsc -b --noEmit && npm run lint`
Expected: PASS, no errors.

- [ ] **Step 6: Commit**

```bash
git add src/features/employee-planning/pages/EmployeePlanningPage.tsx src/features/employee-planning/components/__tests__/legendPosition.test.tsx
git commit -m "feat(planning): move schedule legend above the calendar grid"
```

---

## Task 3: Backend Peppol scheme catalog + endpoint

**Files:**
- Create: `TransportationService.Api/Modules/Partners/Services/PeppolSchemeCatalog.cs`
- Modify: `TransportationService.Api/Modules/Partners/Dtos/CustomerDtos.cs` (add `PeppolSchemeDto`)
- Modify: `TransportationService.Api/Modules/Partners/Controllers/CustomersController.cs` (add `GET peppol-schemes`)
- Test: `TransportationService.Api.Tests/Partners/PeppolSchemeCatalogTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record PeppolSchemeInfo(string Code, string Label, string? CountryCode);
  public static class PeppolSchemeCatalog {
      public static IReadOnlyList<PeppolSchemeInfo> All { get; }
      public static bool IsKnown(string code);
      public static string? InferSchemeForCountry(string? countryCode); // e.g. "BE" -> "0208"
  }
  public sealed record PeppolSchemeDto(string Code, string Label, string? CountryCode);
  ```
- Consumes: nothing new.

- [ ] **Step 1: Write the failing catalog test**

Create `TransportationService.Api.Tests/Partners/PeppolSchemeCatalogTests.cs`:
```csharp
using TransportationService.Api.Modules.Partners.Services;
using Xunit;

public class PeppolSchemeCatalogTests
{
    [Fact]
    public void All_contains_known_belgian_and_dutch_schemes()
    {
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "0208"); // BE enterprise number
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "9925"); // BE VAT
        Assert.Contains(PeppolSchemeCatalog.All, s => s.Code == "0106"); // NL KvK
    }

    [Fact]
    public void All_codes_are_four_ascii_digits()
    {
        Assert.All(PeppolSchemeCatalog.All, s =>
            Assert.Matches("^[0-9]{4}$", s.Code));
    }

    [Fact]
    public void IsKnown_matches_catalog_membership()
    {
        Assert.True(PeppolSchemeCatalog.IsKnown("0208"));
        Assert.False(PeppolSchemeCatalog.IsKnown("0000"));
    }

    [Fact]
    public void InferSchemeForCountry_returns_belgian_enterprise_scheme_for_BE()
    {
        Assert.Equal("0208", PeppolSchemeCatalog.InferSchemeForCountry("be"));
        Assert.Null(PeppolSchemeCatalog.InferSchemeForCountry("ZZ"));
        Assert.Null(PeppolSchemeCatalog.InferSchemeForCountry(null));
    }
}
```

- [ ] **Step 2: Run it, verify it fails**

Run: `dotnet test TransportationService.Api.Tests --filter PeppolSchemeCatalogTests`
Expected: FAIL — `PeppolSchemeCatalog` does not exist.

- [ ] **Step 3: Implement the catalog**

Create `TransportationService.Api/Modules/Partners/Services/PeppolSchemeCatalog.cs`:
```csharp
namespace TransportationService.Api.Modules.Partners.Services;

/// <summary>One Peppol EAS scheme entry (subset relevant to this tenant base).</summary>
public sealed record PeppolSchemeInfo(string Code, string Label, string? CountryCode);

/// <summary>
/// Authoritative list of Peppol scheme (EAS) codes offered in the UI, mirroring the
/// single-source-of-truth pattern of <see cref="VatTreatmentCatalog"/>. Not exhaustive —
/// extend as tenants require. Codes are the 4-digit EAS identifiers.
/// </summary>
public static class PeppolSchemeCatalog
{
    public static IReadOnlyList<PeppolSchemeInfo> All { get; } = new List<PeppolSchemeInfo>
    {
        new("0208", "Belgisch ondernemingsnummer (KBO/BCE)", "BE"),
        new("9925", "Belgisch BTW-nummer", "BE"),
        new("0106", "Nederlands KvK-nummer", "NL"),
        new("9944", "Nederlands BTW-nummer", "NL"),
        new("0088", "GLN (EAN Location Code)", null),
        new("9930", "Duits BTW-nummer", "DE"),
        new("0009", "Frans SIRET", "FR"),
    };

    private static readonly HashSet<string> Codes =
        All.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string code) => Codes.Contains(code);

    /// <summary>Best-effort default scheme for a country; null when there is no single obvious choice.</summary>
    public static string? InferSchemeForCountry(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        return countryCode.Trim().ToUpperInvariant() switch
        {
            "BE" => "0208",
            "NL" => "0106",
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Run the catalog test, verify pass**

Run: `dotnet test TransportationService.Api.Tests --filter PeppolSchemeCatalogTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Add the DTO + endpoint**

In `Modules/Partners/Dtos/CustomerDtos.cs`, add near the other lookup DTOs:
```csharp
public sealed record PeppolSchemeDto(string Code, string Label, string? CountryCode);
```

In `Modules/Partners/Controllers/CustomersController.cs`, add an action next to the existing `GET vat-treatments` (reuse its `[RequirePermission]` attribute value verbatim so authorization matches the VAT-treatment endpoint):
```csharp
[HttpGet("peppol-schemes")]
public ActionResult<IReadOnlyList<PeppolSchemeDto>> GetPeppolSchemes()
    => Ok(PeppolSchemeCatalog.All
        .Select(s => new PeppolSchemeDto(s.Code, s.Label, s.CountryCode))
        .ToList());
```
Add `using TransportationService.Api.Modules.Partners.Services;` if not already present.

- [ ] **Step 6: Build + full backend test suite**

Run: `dotnet build && dotnet test`
Expected: build succeeds; all tests pass (new catalog tests included).

- [ ] **Step 7: Commit**

```bash
git add TransportationService.Api/Modules/Partners/Services/PeppolSchemeCatalog.cs TransportationService.Api/Modules/Partners/Dtos/CustomerDtos.cs TransportationService.Api/Modules/Partners/Controllers/CustomersController.cs TransportationService.Api.Tests/Partners/PeppolSchemeCatalogTests.cs
git commit -m "feat(partners): authoritative Peppol scheme catalog + GET peppol-schemes endpoint"
```

---

## Task 4: Frontend `PeppolFieldGroup` component

**Files:**
- Modify: `src/features/customers/api/customersApi.ts` (add `getPeppolSchemes`)
- Modify: `src/features/customers/types.ts` (add `PeppolScheme`, `PeppolStatus`)
- Create: `src/features/customers/components/PeppolFieldGroup.tsx`
- Test: `src/features/customers/components/__tests__/peppolFieldGroup.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export type PeppolStatus = 'auto' | 'manual' | 'not-found' | 'not-validated'
  export interface PeppolScheme { code: string; label: string; countryCode: string | null }
  export function getPeppolSchemes(): Promise<PeppolScheme[]>
  export function PeppolFieldGroup(props: {
    scheme: string
    participantId: string
    status: PeppolStatus
    schemes: PeppolScheme[]
    disabled?: boolean
    onChange: (next: { scheme: string; participantId: string }) => void
  }): JSX.Element
  ```
- Consumes: `apiClient` from `src/api` (same pattern as existing `getVatTreatments`).

- [ ] **Step 1: Write the failing component test**

Create `src/features/customers/components/__tests__/peppolFieldGroup.test.tsx`:
```tsx
import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PeppolFieldGroup } from '../PeppolFieldGroup'
import type { PeppolScheme } from '../types'

const schemes: PeppolScheme[] = [
  { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
  { code: '9925', label: 'Belgisch BTW-nummer', countryCode: 'BE' },
]

function setup(overrides: Partial<Parameters<typeof PeppolFieldGroup>[0]> = {}) {
  const onChange = vi.fn()
  render(
    <PeppolFieldGroup
      scheme="0208"
      participantId="0123456789"
      status="manual"
      schemes={schemes}
      onChange={onChange}
      {...overrides}
    />,
  )
  return { onChange }
}

describe('PeppolFieldGroup', () => {
  it('renders scheme and participant id as one grouped control', () => {
    setup()
    expect(screen.getByRole('group', { name: /Peppol/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/Schema/i)).toHaveValue('0208')
    expect(screen.getByLabelText(/Participant-ID/i)).toHaveValue('0123456789')
  })

  it('emits both values when the scheme changes', async () => {
    const { onChange } = setup()
    await userEvent.selectOptions(screen.getByLabelText(/Schema/i), '9925')
    expect(onChange).toHaveBeenCalledWith({ scheme: '9925', participantId: '0123456789' })
  })

  it('emits both values when the id changes', async () => {
    const { onChange } = setup({ participantId: '' })
    await userEvent.type(screen.getByLabelText(/Participant-ID/i), '9')
    expect(onChange).toHaveBeenCalledWith({ scheme: '0208', participantId: '9' })
  })

  it('shows the auto-retrieved status', () => {
    setup({ status: 'auto' })
    expect(screen.getByText(/automatisch opgehaald/i)).toBeInTheDocument()
  })

  it('shows the not-found status', () => {
    setup({ status: 'not-found' })
    expect(screen.getByText(/niet gevonden/i)).toBeInTheDocument()
  })

  it('disables inputs when disabled', () => {
    setup({ disabled: true })
    expect(screen.getByLabelText(/Schema/i)).toBeDisabled()
    expect(screen.getByLabelText(/Participant-ID/i)).toBeDisabled()
  })
})
```

- [ ] **Step 2: Run it, verify it fails**

Run: `npx vitest run src/features/customers/components/__tests__/peppolFieldGroup.test.tsx`
Expected: FAIL — cannot resolve `../PeppolFieldGroup`.

- [ ] **Step 3: Add the API + types**

In `src/features/customers/types.ts`, add:
```ts
export type PeppolStatus = 'auto' | 'manual' | 'not-found' | 'not-validated'
export interface PeppolScheme {
  code: string
  label: string
  countryCode: string | null
}
```

In `src/features/customers/api/customersApi.ts`, add (mirroring the existing `getVatTreatments`):
```ts
import type { PeppolScheme } from '../types'

export function getPeppolSchemes(): Promise<PeppolScheme[]> {
  return apiClient.get<PeppolScheme[]>('/api/customers/peppol-schemes')
}
```
(Match the exact `apiClient` call style already used in this file for `getVatTreatments`.)

- [ ] **Step 4: Implement the component**

Create `src/features/customers/components/PeppolFieldGroup.tsx`:
```tsx
import { FormField } from '../../../components/ui/FormField'
import type { PeppolScheme, PeppolStatus } from '../types'

const STATUS_TEXT: Record<PeppolStatus, string> = {
  auto: 'Automatisch opgehaald',
  manual: 'Handmatig ingevoerd',
  'not-found': 'Niet gevonden',
  'not-validated': 'Niet gevalideerd',
}

interface PeppolFieldGroupProps {
  scheme: string
  participantId: string
  status: PeppolStatus
  schemes: PeppolScheme[]
  disabled?: boolean
  onChange: (next: { scheme: string; participantId: string }) => void
}

export function PeppolFieldGroup({
  scheme,
  participantId,
  status,
  schemes,
  disabled,
  onChange,
}: PeppolFieldGroupProps) {
  return (
    <fieldset className="peppol-group" role="group" aria-label="Peppol">
      <legend className="peppol-group-legend">
        Peppol
        <span className={`peppol-status peppol-status-${status}`}>{STATUS_TEXT[status]}</span>
      </legend>
      <div className="peppol-group-fields">
        <FormField label="Schema" htmlFor="peppol-scheme">
          <select
            id="peppol-scheme"
            value={scheme}
            disabled={disabled}
            onChange={(e) => onChange({ scheme: e.target.value, participantId })}
          >
            <option value="">—</option>
            {schemes.map((s) => (
              <option key={s.code} value={s.code}>
                {s.code} — {s.label}
              </option>
            ))}
          </select>
        </FormField>
        <FormField label="Participant-ID" htmlFor="peppol-id" hint="Zonder schema, bv. 0123456789.">
          <input
            id="peppol-id"
            value={participantId}
            maxLength={64}
            disabled={disabled}
            onChange={(e) => onChange({ scheme, participantId: e.target.value })}
          />
        </FormField>
      </div>
    </fieldset>
  )
}
```
Add matching styles to `src/features/customers/customers.css` (status chip colours per state; grid for `.peppol-group-fields`).

- [ ] **Step 5: Run the component test, verify pass**

Run: `npx vitest run src/features/customers/components/__tests__/peppolFieldGroup.test.tsx`
Expected: PASS (6 tests).

- [ ] **Step 6: Typecheck + lint**

Run: `npx tsc -b --noEmit && npm run lint`
Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add src/features/customers/components/PeppolFieldGroup.tsx src/features/customers/components/__tests__/peppolFieldGroup.test.tsx src/features/customers/api/customersApi.ts src/features/customers/types.ts src/features/customers/customers.css
git commit -m "feat(customers): grouped PeppolFieldGroup control with lookup status"
```

---

## Task 5: Convert `EmployeeForm` to `SectionedForm`

**Files:**
- Create: `src/features/employees/components/sections/AlgemeenSection.tsx`, `DienstverbandSection.tsx`, `HrSection.tsx`, `NoodcontactenSection.tsx`, `NotitiesSection.tsx` (extracted bodies; confidential subset stays inside `HrSection`)
- Create: `src/features/employees/components/employeeSections.tsx` (shared config builder)
- Modify: `src/features/employees/components/EmployeeForm.tsx` (render `SectionedForm` instead of stacked `<FormSection>`s)
- Test: `src/features/employees/components/__tests__/employeeSectionedForm.test.tsx`

**Interfaces:**
- Consumes: `SectionedForm`, `SectionDef`, `useSectionNavigation`, `firstSectionWithError` from Task 1.
- Produces:
  ```ts
  // employeeSections.tsx
  export interface EmployeeSectionContext { /* the existing EmployeeForm state + setters + flags */ }
  export function buildEmployeeSections(
    ctx: EmployeeSectionContext,
    opts: { mode: 'create' | 'edit'; canSeeConfidential: boolean; fieldErrors: Record<string, string> | null; extraSections?: SectionDef[] },
  ): SectionDef[]
  ```

- [ ] **Step 1: Extract section bodies without behavior change**

Move the JSX currently inside each `<FormSection>` of `EmployeeForm.tsx` into a section component that receives the relevant state + setters as props. Preserve every input, label, `FormField`, validation hookup, and confidential gating exactly. Map the existing 7 form sections to the config's shared-submit sections:
- Persoonlijk + Contact&adres → **Algemeen** (`AlgemeenSection`)
- Dienstverband → **Dienstverband**
- Persoonlijk/HR + Identiteit&bank (confidential subset, still gated) → **HR** (`HrSection`)
- Noodcontacten → **Noodcontacten**
- Notities → **Notities**

Each section component keeps state in the parent — it receives values + `onChange` callbacks, it does not own `useState`.

- [ ] **Step 2: Write the shared section config**

Create `src/features/employees/components/employeeSections.tsx`:
```tsx
import type { SectionDef } from '../../../components/ui/SectionedForm'
import { AlgemeenSection } from './sections/AlgemeenSection'
import { DienstverbandSection } from './sections/DienstverbandSection'
import { HrSection } from './sections/HrSection'
import { NoodcontactenSection } from './sections/NoodcontactenSection'
import { NotitiesSection } from './sections/NotitiesSection'

// Field-key ownership drives the error badge + first-error routing.
const FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['firstName', 'lastName', 'dateOfBirth', 'email', 'street', 'postalCode', 'city', 'countryCode'],
  dienstverband: ['employmentStartDate', 'employmentStatus', 'departmentId', 'contractTypeId'],
  hr: ['dependentChildren', 'nationalRegisterNumber', 'iban', 'bic', 'identityCardNumber'],
  noodcontacten: ['emergencyContacts'],
  notities: [],
}

export interface EmployeeSectionContext {
  // The exact state slices + setters EmployeeForm already holds; passed straight through.
  values: import('../../../types/employee').EmployeeInput | Record<string, unknown>
  setField: (patch: Record<string, unknown>) => void
  fieldErrors: Record<string, string> | null
  // plus the collection helpers already in EmployeeForm (emergency contacts add/remove, job functions, etc.)
  [key: string]: unknown
}

export function buildEmployeeSections(
  ctx: EmployeeSectionContext,
  opts: {
    mode: 'create' | 'edit'
    canSeeConfidential: boolean
    fieldErrors: Record<string, string> | null
    extraSections?: SectionDef[]
  },
): SectionDef[] {
  const err = opts.fieldErrors ?? {}
  const has = (id: string) => (FIELD_KEYS[id] ?? []).some((k) => k in err)

  const core: SectionDef[] = [
    { id: 'algemeen', label: 'Algemeen', hasError: has('algemeen'), render: () => <AlgemeenSection ctx={ctx} /> },
    { id: 'dienstverband', label: 'Dienstverband', hasError: has('dienstverband'), render: () => <DienstverbandSection ctx={ctx} /> },
    { id: 'hr', label: 'HR', optional: true, hasError: has('hr'), render: () => <HrSection ctx={ctx} canSeeConfidential={opts.canSeeConfidential} /> },
    { id: 'noodcontacten', label: 'Noodcontacten', optional: true, hasError: has('noodcontacten'), render: () => <NoodcontactenSection ctx={ctx} /> },
  ]
  const extras = opts.extraSections ?? []
  const notities: SectionDef = { id: 'notities', label: 'Notities', optional: true, render: () => <NotitiesSection ctx={ctx} /> }
  return [...core, ...extras, notities]
}

export { FIELD_KEYS as EMPLOYEE_SECTION_FIELD_KEYS }
```

- [ ] **Step 3: Wire `SectionedForm` into `EmployeeForm.tsx`**

Replace the stacked `<FormSection>` render block with:
```tsx
const sections = buildEmployeeSections(ctx, { mode, canSeeConfidential, fieldErrors, extraSections })
const { activeId, setActive } = useSectionNavigation(sections.map((s) => s.id), sections[0].id)

// after a failed submit sets fieldErrors, route to the first errored section:
useEffect(() => {
  const target = firstSectionWithError(
    sections.map((s) => ({ id: s.id, fieldKeys: EMPLOYEE_SECTION_FIELD_KEYS[s.id] })),
    fieldErrors,
  )
  if (target) setActive(target)
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, [submitAttemptId])   // increment submitAttemptId in handleSubmit so this only runs per submit

return (
  <form onSubmit={handleSubmit}>
    <ValidationSummary errors={/* existing */} />
    <SectionedForm
      sections={sections}
      activeId={activeId}
      onActiveChange={setActive}
      actions={<FormActions /* existing Save/Cancel props */ />}
    />
    <UnsavedChangesGuard when={isDirty} />
  </form>
)
```
Keep `handleSubmit`, validation, dirty tracking, `EmployeeInput` building, and the confidential gate exactly as before. `extraSections` (driver profile + qualifications) continues to come from `NewEmployeePage` and is inserted before Notities as `SectionDef`s (wrap the existing extra JSX in `render`). Follow the existing lint rule about set-state-in-effect — increment a `submitAttemptId` counter inside `handleSubmit` and depend on it, rather than depending on `fieldErrors` directly.

- [ ] **Step 4: Write the sectioned-form behavior test**

Create `src/features/employees/components/__tests__/employeeSectionedForm.test.tsx`:
```tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EmployeeForm } from '../EmployeeForm'

// Minimal auth/permission + query providers as used by other employee tests;
// reuse the existing test helpers/mocks already present in this feature's tests.
function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      {/* wrap with the same providers the other EmployeeForm tests use */}
      <EmployeeForm mode="create" onSubmit={async () => {}} {...props} />
    </MemoryRouter>,
  )
}

describe('EmployeeForm section navigation', () => {
  it('shows one section at a time and preserves values across switches', async () => {
    renderForm()
    await userEvent.type(screen.getByLabelText(/Voornaam/i), 'Jan')
    await userEvent.click(screen.getByRole('tab', { name: /Dienstverband/i }))
    expect(screen.queryByLabelText(/Voornaam/i)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/Voornaam/i)).toHaveValue('Jan')
  })

  it('opens the first section containing a validation error on failed submit', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Noodcontacten/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    // required Voornaam is empty -> routes back to Algemeen
    expect(screen.getByRole('tab', { name: /Algemeen/i })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: /Algemeen/i })).toHaveAttribute('data-has-error', 'true')
  })

  it('does not mark optional sections as required', () => {
    renderForm()
    expect(screen.getByRole('tab', { name: /Noodcontacten/i })).not.toHaveAttribute('data-required')
  })

  it('hides confidential fields without the permission', () => {
    renderForm(/* permission: employees.view_confidential = false via provider mock */)
    // HR section: NRN/IBAN absent
    expect(screen.queryByLabelText(/Rijksregisternummer/i)).not.toBeInTheDocument()
  })
})
```
Reuse whatever provider/permission mock the existing employee tests use (check `src/features/employees/**/__tests__` for the established pattern before writing this — match it exactly rather than inventing a new mock).

- [ ] **Step 5: Run employee tests, verify pass**

Run: `npx vitest run src/features/employees`
Expected: PASS (new + existing).

- [ ] **Step 6: Typecheck + lint + build**

Run: `npx tsc -b --noEmit && npm run lint && npm run build`
Expected: no errors; production build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/features/employees/components/sections src/features/employees/components/employeeSections.tsx src/features/employees/components/EmployeeForm.tsx src/features/employees/components/__tests__/employeeSectionedForm.test.tsx
git commit -m "feat(employees): section-navigation employee create/edit form"
```

---

## Task 6: Convert `CustomerForm` to `SectionedForm` + integrate Peppol + embed panels

**Files:**
- Create: `src/features/customers/components/sections/*.tsx` (Algemeen, Fiscaal&Peppol, Bank, Facturatie, Notities extracted bodies)
- Create: `src/features/customers/components/customerSections.tsx` (shared config; Adressen/Contactpersonen/Communicatie/Tarieven are `panel: true` sections embedding existing panels on edit)
- Modify: `src/features/customers/components/CustomerForm.tsx` (render `SectionedForm`, load schemes, wire `PeppolFieldGroup`, registry-lookup populate-both + no-silent-overwrite)
- Test: `src/features/customers/components/__tests__/customerSectionedForm.test.tsx`
- Test: `src/features/customers/components/__tests__/customerPeppolLookup.test.tsx`

**Interfaces:**
- Consumes: `SectionedForm`, `useSectionNavigation`, `firstSectionWithError` (Task 1); `PeppolFieldGroup`, `getPeppolSchemes` (Task 4); existing panels `CustomerLocationsPanel`, `CustomerContactsPanel`, `CustomerCommunicationPanel`, `CustomerBillingPanel`.
- Produces:
  ```ts
  export function buildCustomerSections(ctx, opts: {
    mode: 'create' | 'edit'; canManageFiscal: boolean; fieldErrors: Record<string,string>|null;
    schemes: import('../types').PeppolScheme[]; peppolStatus: import('../types').PeppolStatus;
    onPeppolChange: (next: { scheme: string; participantId: string }) => void;
    customerId?: string; // enables panel sections on edit
  }): SectionDef[]
  ```

- [ ] **Step 1: Extract customer scalar sections**

Move the JSX inside `CustomerForm.tsx`'s shared-submit `<FormSection>`s into section components (Algemeen; Fiscaal&Peppol; Bank; Facturatie&vereisten; Notities), preserving every field, the fiscal gate (`disabled={!canManageFiscal}`), and the VAT-rate control logic. Replace the two loose Peppol inputs (current lines ~650–669) with `<PeppolFieldGroup .../>` inside the Fiscaal&Peppol section.

- [ ] **Step 2: Build the shared customer section config**

Create `src/features/customers/components/customerSections.tsx`:
```tsx
import type { SectionDef } from '../../../components/ui/SectionedForm'
import { AlgemeenSection } from './sections/AlgemeenSection'
import { FiscaalPeppolSection } from './sections/FiscaalPeppolSection'
import { BankSection } from './sections/BankSection'
import { FacturatieSection } from './sections/FacturatieSection'
import { NotitiesSection } from './sections/NotitiesSection'
import { CustomerLocationsPanel } from './CustomerLocationsPanel'
import { CustomerContactsPanel } from './CustomerContactsPanel'
import { CustomerCommunicationPanel } from './CustomerCommunicationPanel'
import { CustomerBillingPanel } from './CustomerBillingPanel'

const FIELD_KEYS: Record<string, string[]> = {
  algemeen: ['name', 'customerNumber', 'categoryId', 'email', 'defaultLanguageCode'],
  fiscaal: ['vatNumber', 'companyNumber', 'vatTreatment', 'defaultVatRatePercent', 'vatCountryCode', 'peppolId', 'peppolScheme'],
  bank: ['iban', 'bic'],
  facturatie: ['invoiceEmail', 'paymentTermDays', 'defaultLegalEntityId'],
}

export function buildCustomerSections(ctx: Record<string, unknown>, opts: {
  mode: 'create' | 'edit'
  canManageFiscal: boolean
  fieldErrors: Record<string, string> | null
  schemes: import('../types').PeppolScheme[]
  peppolStatus: import('../types').PeppolStatus
  onPeppolChange: (next: { scheme: string; participantId: string }) => void
  customerId?: string
}): SectionDef[] {
  const err = opts.fieldErrors ?? {}
  const has = (id: string) => (FIELD_KEYS[id] ?? []).some((k) => k in err)
  const isEdit = opts.mode === 'edit' && !!opts.customerId

  const sections: SectionDef[] = [
    { id: 'algemeen', label: 'Algemeen', hasError: has('algemeen'), render: () => <AlgemeenSection ctx={ctx} /> },
    {
      id: 'adressen', label: 'Adressen', panel: isEdit, hasError: false,
      render: () => isEdit
        ? <CustomerLocationsPanel customerId={opts.customerId!} />
        : <AlgemeenSection ctx={ctx} addressOnly />,   // create: inline primary address only
    },
    { id: 'contactpersonen', label: 'Contactpersonen', panel: isEdit, render: () => isEdit ? <CustomerContactsPanel customerId={opts.customerId!} /> : /* create: first-contact block */ <div /> },
    {
      id: 'fiscaal', label: 'Fiscaal & Peppol', hasError: has('fiscaal'),
      render: () => (
        <FiscaalPeppolSection
          ctx={ctx}
          canManageFiscal={opts.canManageFiscal}
          schemes={opts.schemes}
          peppolStatus={opts.peppolStatus}
          onPeppolChange={opts.onPeppolChange}
        />
      ),
    },
    { id: 'bank', label: 'Bank', optional: true, hasError: has('bank'), render: () => <BankSection ctx={ctx} canManageFiscal={opts.canManageFiscal} /> },
    { id: 'facturatie', label: 'Facturatie', hasError: has('facturatie'), render: () => <FacturatieSection ctx={ctx} /> },
    { id: 'communicatie', label: 'Communicatie', panel: true, render: () => isEdit ? <CustomerCommunicationPanel customerId={opts.customerId!} /> : <div className="section-hint">Beschikbaar na aanmaken.</div> },
    { id: 'tarieven', label: 'Tarieven & toeslagen', panel: true, render: () => isEdit ? <CustomerBillingPanel customerId={opts.customerId!} /> : <div className="section-hint">Beschikbaar na aanmaken.</div> },
    { id: 'notities', label: 'Notities', optional: true, render: () => <NotitiesSection ctx={ctx} /> },
  ]
  return sections
}

export { FIELD_KEYS as CUSTOMER_SECTION_FIELD_KEYS }
```
(Confirm the actual props each existing panel expects — `CustomerLocationsPanel`, `CustomerContactsPanel`, `CustomerCommunicationPanel`, `CustomerBillingPanel` — and pass them exactly as the detail page does today.)

- [ ] **Step 3: Wire schemes + Peppol status + registry lookup into `CustomerForm.tsx`**

Load schemes once and manage Peppol status; extend the existing `applyRegistryResult` so a provider hit populates **both** fields, sets status `auto`, and — for non-empty existing manual values — asks for confirmation before overwriting:
```tsx
const [schemes, setSchemes] = useState<PeppolScheme[]>([])
const [peppolStatus, setPeppolStatus] = useState<PeppolStatus>(
  initial?.peppolId || initial?.peppolScheme ? 'manual' : 'not-validated',
)
useEffect(() => { getPeppolSchemes().then(setSchemes).catch(() => setSchemes([])) }, [])

function onPeppolChange(next: { scheme: string; participantId: string }) {
  setPeppolScheme(next.scheme)
  setPeppolId(next.participantId)
  setPeppolStatus('manual')
}

// inside applyRegistryResult(result): after applying VAT/company fields —
const hasManualPeppol = Boolean(peppolId || peppolScheme)
const providerPeppol = result.peppolId || result.peppolScheme
if (providerPeppol) {
  if (hasManualPeppol && (peppolId !== result.peppolId || peppolScheme !== result.peppolScheme)) {
    // eslint-disable-next-line no-alert
    if (!window.confirm('Bestaande Peppol-gegevens overschrijven met opgehaalde waarden?')) return
  }
  setPeppolId(result.peppolId ?? '')
  setPeppolScheme(result.peppolScheme ?? inferSchemeFromResult(result) ?? '')
  setPeppolStatus('auto')
} else if (result /* lookup ran but no peppol */) {
  setPeppolStatus('not-found')
}
```
where `inferSchemeFromResult` picks the catalog default for the result's country when the provider gave an id but no scheme (client mirror of `PeppolSchemeCatalog.InferSchemeForCountry`, or simply leave blank if you prefer server-only inference). Then render `SectionedForm` exactly as in Task 5 (same `useSectionNavigation` + first-error routing via `CUSTOMER_SECTION_FIELD_KEYS`, same sticky `FormActions`, unchanged `handleSubmit`).

- [ ] **Step 4: Write the section-navigation test**

Create `src/features/customers/components/__tests__/customerSectionedForm.test.tsx`:
```tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerForm } from '../CustomerForm'

function renderForm(props = {}) {
  return render(
    <MemoryRouter>
      {/* same providers the existing customer tests use */}
      <CustomerForm mode="create" onSubmit={async () => {}} {...props} />
    </MemoryRouter>,
  )
}

describe('CustomerForm section navigation', () => {
  it('preserves values when switching sections', async () => {
    renderForm()
    await userEvent.type(screen.getByLabelText(/^Naam/i), 'Acme')
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(screen.getByRole('tab', { name: /Algemeen/i }))
    expect(screen.getByLabelText(/^Naam/i)).toHaveValue('Acme')
  })

  it('renders scheme and id together in one Peppol group', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    const group = screen.getByRole('group', { name: /Peppol/i })
    expect(group).toBeInTheDocument()
    expect(screen.getByLabelText(/Schema/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/Participant-ID/i)).toBeInTheDocument()
  })

  it('routes to Algemeen when a required field fails on submit', async () => {
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Bank/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opslaan/i }))
    expect(screen.getByRole('tab', { name: /Algemeen/i })).toHaveAttribute('aria-selected', 'true')
  })
})
```

- [ ] **Step 5: Write the Peppol lookup test (populate both / not-found / no silent overwrite)**

Create `src/features/customers/components/__tests__/customerPeppolLookup.test.tsx` mocking `registryLookup` and `getPeppolSchemes`:
```tsx
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import * as api from '../../api/customersApi'
import { CustomerForm } from '../CustomerForm'

beforeEach(() => {
  vi.spyOn(api, 'getPeppolSchemes').mockResolvedValue([
    { code: '0208', label: 'Belgisch ondernemingsnummer', countryCode: 'BE' },
  ])
})

function renderForm(props = {}) {
  return render(<MemoryRouter><CustomerForm mode="create" onSubmit={async () => {}} {...props} /></MemoryRouter>)
}

describe('Customer Peppol registry lookup', () => {
  it('populates both scheme and id from a provider hit and marks them auto', async () => {
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true,
      result: { peppolId: '0123456789', peppolScheme: '0208', legalName: 'Acme NV' },
    } as never)
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opzoeken/i }))
    expect(await screen.findByText(/automatisch opgehaald/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/Participant-ID/i)).toHaveValue('0123456789')
    expect(screen.getByLabelText(/Schema/i)).toHaveValue('0208')
  })

  it('shows not-found when the provider returns no peppol data', async () => {
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true, result: { peppolId: null, peppolScheme: null, legalName: 'Acme NV' },
    } as never)
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.click(screen.getByRole('button', { name: /Opzoeken/i }))
    expect(await screen.findByText(/niet gevonden/i)).toBeInTheDocument()
  })

  it('does not overwrite existing manual peppol data without confirmation', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)
    vi.spyOn(api, 'registryLookup').mockResolvedValue({
      configured: true, result: { peppolId: '9999999999', peppolScheme: '9925' },
    } as never)
    renderForm()
    await userEvent.click(screen.getByRole('tab', { name: /Fiscaal & Peppol/i }))
    await userEvent.type(screen.getByLabelText(/Participant-ID/i), '0123456789')
    await userEvent.click(screen.getByRole('button', { name: /Opzoeken/i }))
    expect(confirmSpy).toHaveBeenCalled()
    expect(screen.getByLabelText(/Participant-ID/i)).toHaveValue('0123456789') // unchanged
  })
})
```
(Match the real lookup button label and `registryLookup` return shape found in `customersApi.ts`/`CustomerForm.tsx`; adjust names if they differ.)

- [ ] **Step 6: Run customer tests, verify pass**

Run: `npx vitest run src/features/customers`
Expected: PASS (new + existing).

- [ ] **Step 7: Full frontend gate**

Run: `npx tsc -b --noEmit && npm run lint && npm test && npm run build`
Expected: types clean, lint clean, all Vitest pass, production build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/features/customers/components/sections src/features/customers/components/customerSections.tsx src/features/customers/components/CustomerForm.tsx src/features/customers/components/__tests__/customerSectionedForm.test.tsx src/features/customers/components/__tests__/customerPeppolLookup.test.tsx
git commit -m "feat(customers): section-navigation form with grouped Peppol + embedded panels"
```

---

## Final verification (whole sub-project)

- [ ] **Backend:** from repo root, `dotnet build && dotnet test` — all green.
- [ ] **Frontend:** from `TransportationService.Web/`, `npx tsc -b --noEmit && npm run lint && npm test && npm run build` — all green.
- [ ] **Manual smoke (optional):** create + edit an employee and a customer, switch sections (values persist), trigger a validation error (routes to the right section), open the planning page (legend sits above the grid), run a registry lookup on a dev tenant (status chip reflects auto/not-found).
- [ ] **Worktree clean** except the pre-existing `20260721184002_UnitTypes.cs` change (leave untouched; flag in the report).

## Self-review notes (coverage against the spec)

- Employee nav (§1) → Tasks 1, 5. Customer nav (§2) → Tasks 1, 6. Peppol grouping (§3) → Tasks 3, 4, 6. Planning legend (§4) → Task 2.
- Testing requirements (§11 frontend subset): switching preserves values (T5/T6), same grouping create+edit (shared config T5/T6), validation opens correct section (T5/T6), optional sections don't block (T5), mobile selector (T1), permission-restricted fields hidden (T5), Peppol grouped/populate-both/not-found/no-silent-overwrite/manual-override (T4/T6), legend above calendar (T2).
- Stock/asset items (§5–§10) are intentionally **out of scope** for this plan — they are sub-project 2 (separate plan).
