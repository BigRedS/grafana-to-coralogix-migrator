# Dashboard UI Health Investigation Evidence

## Purpose

This document records live reconnaissance done against the tenant UI using Playwright MCP before implementing migration validation tests.  
Targets investigated:

- Coralogix custom dashboards: `https://watcher.coralogix.com/#/dashboards/00135aed34dd4ee99e33a`
- Grafana (embedded): `https://watcher.coralogix.com/#/grafana`

## Evidence of Live Investigation

Observed during MCP session:

- Login page loaded and authenticated with `watcher.coralogix.com` credentials.
- Coralogix dashboard editor/list shell rendered with stable `data-test` attributes.
- Grafana rendered inside an iframe and required iframe-rooted selectors.
- Grafana dashboard list and folder flow were reachable at `/grafana/dashboards`.
- A concrete Grafana dashboard (`SampleDash`) opened from folder and showed panel states (`No data` markers present).
- A concrete Coralogix custom dashboard view loaded and exposed widget containers with stable IDs.

## Coralogix Custom Dashboards Structure (Observed)

Primary shell and navigation:

- Root container: `[data-test="custom-dashboards-container"]`
- Dashboard title: `[data-test="cd-title-text"]`
- Dashboard catalog tree: `[data-test="cd-dashboard-catalog"]`
- Search input: `[data-test="cd-sidebar-search-input"] [data-test="cx-input-field"]`

Widget model:

- Widget container: `[data-test^="cd-widget-container-"]`
- Widget body: `[data-test="cd-widget"]`
- Widget title: `[data-test="cd-widget-name"]`
- Widget controls: refresh/context/fullscreen controls are adjacent to widget headers

Health-related visible states:

- No-data signal: `There is no data matching your query`
- Error surfaces to watch: alert/toast/banner nodes and error-class nodes

## Grafana Structure (Observed)

Embedding and navigation:

- Grafana is hosted in iframe under Coralogix shell.
- Reliable frame strategy: locate `iframe[src*="grafana"]` with fallback to generic iframe.
- Main dashboards list route: `/grafana/dashboards`
- Dashboard open route: `/grafana/d/{uid}/{slug}?orgId=...`

Panel model:

- Panel wrapper candidates: `[data-testid="Panel"]`, `.panel-container`
- Header identity: `[data-testid^="Panel header "]`
- Breadcrumb/title signals: `[data-testid$="breadcrumb"]`, `h1`

Health-related visible states:

- No-data signals: `No data`, `No results`
- Error surfaces: panel error blocks, alert regions, error banners/messages

## Selector Inventory (Primary -> Fallback)

Coralogix:

- Dashboard root: `[data-test="custom-dashboards-container"]` -> `main`
- Dashboard title: `[data-test="cd-title-text"]` -> `h1`
- Widget containers: `[data-test^="cd-widget-container-"]` -> `[data-test="cd-widget"]`
- Widget title: `[data-test="cd-widget-name"]` -> widget header text (`h2/h3`)

Grafana:

- Frame root: `iframe[src*="grafana"]` -> `iframe[title*="Grafana"]` -> first `iframe`
- Dashboard route open: `/#/grafana/d/{uid}` (host wrapper) -> click from `/grafana/dashboards`
- Panel container: `[data-testid="Panel"]` -> `.panel-container`
- Panel title: `[data-testid^="Panel header "]` -> `h2/h3`

## Health Investigation Checklist

Zero visible error tolerance checks:

1. No visible alert/notification errors.
2. No panel-level error content (`query failed`, `runtime error`, `rendering error`, `exception`).
3. No dashboard-level error banners.

Data presence checks:

1. Dashboard has visible widgets/panels.
2. At least one panel is data-bearing (not pure no-data state).
3. Panels used for value comparison have extractable numeric values.

Loading completion checks:

1. Wait for dashboard container visible.
2. Wait for iframe body visible (Grafana).
3. Add post-load stability wait (1-2s) before extraction.

Console/runtime checks:

1. Capture `console.error` and `pageerror`.
2. Fail on non-benign runtime errors.
3. Maintain a minimal benign allowlist only when proven harmless.

## Re-Validation Procedure (Manual + Automated)

Manual investigation:

1. Open both target URLs and authenticate.
2. Verify dashboard list/search/open flows still work.
3. Open one known-good dashboard on each platform.
4. Confirm selectors in this doc still resolve.
5. Capture any changed no-data/error text variants.

Automated investigation:

1. Refresh storage state with `npm run e2e:auth` when session expires.
2. Configure dashboard names in `tests/e2e/dashboard-selection.json`.
3. Run `npm run e2e:test`.
4. Inspect artifacts in `tests/e2e/artifacts/<dashboard>/comparison.json` and screenshots.

## Known Risk Areas and Mitigations

Risks:

- Grafana iframe structure can shift after platform upgrades.
- Panel text extraction can include non-metric numbers (time/range labels).
- Different default time windows can inflate value deltas.

Mitigations:

- Use selector fallback chains and fail with explicit selector errors.
- Keep tolerance-based comparison with conservative thresholds.
- Keep artifact-first debugging (JSON + screenshots) on every failure.
- Re-run reconnaissance and update selector catalog when UI changes.
