# Grafana to Coralogix Custom Dashboard Converter

A .NET 9 CLI tool that converts Grafana dashboards into Coralogix custom dashboard format.

It supports:
- Single-file conversion (`convert`)
- Single-file conversion + upload (`push`)
- Bulk migration from live Grafana (`migrate`)
- Bulk import from local files (`import`)
- Conversion + round-trip validation (`verify`)

---

## Table of Contents

- [Quick Start — Interactive Migration](#quick-start--interactive-migration)
- [Step-by-Step Walkthrough](#step-by-step-walkthrough)
  - [Step 1 — Prerequisites](#step-1--prerequisites)
  - [Step 2 — Clone and configure](#step-2--clone-and-configure)
  - [Step 3 — Build](#step-3--build)
  - [Step 4 — Configure migration-settings.json](#step-4--configure-migration-settingsjson)
  - [Step 5 — Run interactive migration](#step-5--run-interactive-migration)
  - [Step 6 — Follow the guided prompts](#step-6--follow-the-guided-prompts)
  - [Step 7 — Monitor progress and resume](#step-7--monitor-progress-and-resume)
- [How It Works](#how-it-works)
- [Supported Panel Types](#supported-panel-types)
- [Supported Query Languages](#supported-query-languages)
- [Project Structure](#project-structure)
- [Migration Settings Reference](#migration-settings-reference)
- [Supported Regions](#supported-regions)
- [Environment Variables](#environment-variables)
- [Other Commands](#other-commands)
- [Playwright Migration Validation](#playwright-migration-validation-grafana-vs-coralogix)
- [Troubleshooting](#troubleshooting)

---

## Quick Start — Interactive Migration

The fastest way to migrate dashboards from Grafana to Coralogix:

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- migrate --interactive
```

This launches a fully guided terminal session — no flags to memorise. The tool walks you through selecting your Coralogix region, API key, Grafana API key, folder selection, folder mapping, and starts the migration.

---

## Step-by-Step Walkthrough

### Step 1 — Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A **Grafana API token** with read access to dashboards and folders
- A **Coralogix API key** with dashboard write permissions

### Step 2 — Clone and configure

```bash
git clone <your-repo-url>
cd grafana_to_cx_custom_converter
dotnet restore GrafanaToCx.sln
```

Export your credentials so the tool picks them up automatically (recommended):

```bash
export GRAFANA_API_KEY=glsa_xxxxxxxxxxxx
export CX_API_KEY=cxtp_xxxxxxxxxxxx
```

If you skip this step the prompts will ask for them during the session.

### Step 3 — Build

```bash
dotnet build GrafanaToCx.sln
```

### Step 4 — Configure migration-settings.json

The settings file controls which Grafana region and Coralogix region to connect to. Open `src/GrafanaToCx.Cli/migration-settings.json` and set at minimum:

```json
{
  "grafana": {
    "region": "eu1",
    "folders": []
  },
  "coralogix": {
    "region": "eu1"
  },
  "credentials": {
    "grafanaApiKey": "",
    "cxApiKey": ""
  },
  "migration": {
    "checkpointFile": "migration-checkpoint.json",
    "reportFile": "migration-report.txt",
    "maxRetries": 5,
    "initialRetryDelaySeconds": 2
  }
}
```

Set `grafana.region` and `coralogix.region` to the correct region codes (see [Supported Regions](#supported-regions)). Leave `folders` empty to migrate all folders, or list specific folder names to limit scope.

Leave `credentials` empty if you exported environment variables — those take priority.

### Step 5 — Run interactive migration

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- migrate --interactive
```

### Step 6 — Follow the guided prompts

The session walks you through a sequence of prompts. Here is exactly what to expect:

**a) Coralogix session setup**

```
Coralogix region [eu1/eu2/us1/us2/ap1/ap2/ap3/in1]:
Coralogix API key:
```

Enter your Coralogix region and API key. If `CX_API_KEY` is already exported, the key prompt is skipped.

**b) Main menu**

```
╔══════════════════════════════════════════╗
║  Grafana → Coralogix Dashboard Converter ║
╚══════════════════════════════════════════╝

> Migrate   ← select this
  Convert
  Push
  Import
  Settings
  Cleanup
  Exit
```

Use arrow keys to highlight **Migrate** and press Enter.

**c) Grafana API key**

```
Grafana API key: ****
```

Skipped automatically if `GRAFANA_API_KEY` is already exported.

**d) Settings file path**

```
Settings file [migration-settings.json]:
```

Press Enter to accept the default, or type a custom path.

**e) Grafana folder selection**

```
Fetching folders from Grafana...

Select folders to migrate  (use space to select, enter to confirm)
  ◉ General
  ◯ Observability
  ◯ Platform
```

Space-bar to toggle, Enter to confirm. Select all folders you want to migrate.

**f) Folder nesting strategy**

```
Folder nesting strategy
> Nest all under a parent CX folder (preserves structure)
  Map each Grafana folder individually
```

- **Nest all under a parent** — the tool creates (or reuses) a single root folder in Coralogix and places each Grafana folder as a sub-folder beneath it. Best for a clean, grouped import.
- **Map each individually** — you choose a specific Coralogix target folder (or create a new one, or none) for each Grafana folder. Best when you need precise placement.

**g) Parent folder selection (if nesting)**

```
Select or create parent CX folder
> + Create new folder
  Existing Folder A
  Existing Folder B
```

Choose an existing root folder or create a new one. If you create one, enter its name at the next prompt.

The tool then creates sub-folders in Coralogix matching each selected Grafana folder name, and prints a confirmation:

```
Creating sub-folders under 'Grafana Migration'...
  'General'... OK (id: abc123)
  'Observability'... OK (id: def456)
```

**h) Migration plan review**

```
Migration plan:
  Grafana 'General'        →  CX 'General'
  Grafana 'Observability'  →  CX 'Observability'

Overwrite dashboards that already exist in Coralogix? [y/N]:
Proceed with migration? [Y/n]:
```

Review the mapping, decide whether to overwrite existing dashboards, then confirm.

**i) Checkpoint prompt (on subsequent runs)**

If a previous migration checkpoint exists with completed dashboards, you are asked:

```
Checkpoint 'migration-checkpoint.json' already has 42 completed dashboard(s).
Keeping it means those dashboards will be SKIPPED (not re-migrated).
Reset checkpoint and re-migrate all dashboards? [y/N]:
```

- Answer **N** (default) to resume and skip already-completed dashboards.
- Answer **Y** to wipe the checkpoint and start fresh.

**j) Migration runs**

The orchestrator processes each dashboard with automatic retries and logs progress. When complete, a summary is printed:

```
Migration complete.
  Completed : 47
  Skipped   : 0
  Failed    : 1

See migration-report.txt for details.
```

### Step 7 — Monitor progress and resume

Progress is saved after each dashboard to `migration-checkpoint.json`. If the run is interrupted, re-run the same command — completed dashboards are skipped automatically.

A human-readable summary is written to `migration-report.txt` after every run.

---

## How It Works

```text
Grafana Dashboard JSON
        │
        ▼
┌─────────────────────────────┐
│   GrafanaToCxConverter      │
│                             │
│  • Groups panels into       │
│    sections (row panels)    │
│  • Maps variables           │
│  • Maps time ranges         │
│                             │
│  Panel Converters:          │
│  ┌──────────────────────┐   │
│  │ LineChartConverter   │   │  PromQL / LogQL -> Lucene / Elasticsearch
│  │ GaugeConverter       │   │  Thresholds, stat panels
│  │ LogsConverter        │   │  Log panel with Lucene queries
│  │ MarkdownConverter    │   │  Text / markdown panels
│  └──────────────────────┘   │
└─────────────────────────────┘
        │
        ▼
Coralogix Custom Dashboard JSON
        │
        ├─ save locally (convert)
        ├─ upload via API (push / migrate / import)
        └─ upload + verify round-trip (verify)
```

The core conversion logic is in `src/GrafanaToCx.Core`, while `src/GrafanaToCx.Cli` provides CLI commands, API interaction, interactive prompts, and migration orchestration.

---

## Supported Panel Types

| Grafana Panel | Coralogix Widget |
|---|---|
| Time series / Graph | Line chart |
| Stat / Gauge | Gauge |
| Table | Line chart (aggregated) |
| Logs | Log viewer |
| Text / Markdown | Markdown |

---

## Supported Query Languages

| Source | Conversion |
|---|---|
| PromQL | Passed through as-is |
| LogQL | Converted to Lucene via `LogqlToLuceneConverter` |
| Elasticsearch | Passed through as-is |

---

## Project Structure

```text
grafana_to_cx_custom_converter/
├── GrafanaToCx.sln
├── src/
│   ├── GrafanaToCx.Cli/
│   │   ├── Program.cs
│   │   ├── Cli/
│   │   │   ├── AppRunner.cs
│   │   │   ├── ArgumentParser.cs
│   │   │   ├── CommandHandlers.cs
│   │   │   ├── PromptInput.cs
│   │   │   ├── PromptMenus.cs
│   │   │   └── SessionConfig.cs
│   │   └── migration-settings.json
│   └── GrafanaToCx.Core/
│       ├── ApiClient/
│       ├── Converter/
│       │   └── PanelConverters/
│       └── Migration/
└── test_data/
    └── grafana_test_dashboards/
```

---

## Migration Settings Reference

Full settings file with all available fields:

```json
{
  "grafana": {
    "region": "eu1",
    "folders": ["General"]
  },
  "coralogix": {
    "region": "eu1",
    "folderId": "",
    "isLocked": false,
    "migrateFolderStructure": true,
    "parentFolderId": ""
  },
  "credentials": {
    "grafanaApiKey": "",
    "cxApiKey": ""
  },
  "migration": {
    "checkpointFile": "migration-checkpoint.json",
    "reportFile": "migration-report.txt",
    "maxRetries": 5,
    "initialRetryDelaySeconds": 2
  }
}
```

| Field | Description |
|---|---|
| `grafana.region` | Grafana Cloud region |
| `grafana.folders` | Grafana folders to migrate (empty = all) |
| `coralogix.region` | Coralogix region (used to resolve endpoint) |
| `coralogix.folderId` | Fallback CX folder ID when mapping is missing |
| `coralogix.isLocked` | Lock uploaded dashboards |
| `coralogix.migrateFolderStructure` | Recreate Grafana folder structure in Coralogix |
| `coralogix.parentFolderId` | Parent folder for newly created Coralogix folders |
| `credentials.grafanaApiKey` | Optional fallback when `GRAFANA_API_KEY` is not set |
| `credentials.cxApiKey` | Optional fallback when `CX_API_KEY` is not set |
| `migration.checkpointFile` | Checkpoint file path for resume |
| `migration.reportFile` | Human-readable migration report path |
| `migration.maxRetries` | Max retries per dashboard |
| `migration.initialRetryDelaySeconds` | Initial exponential backoff delay |

`migration.multiLuceneMerge.allowlistedWidgetTypes` optionally allowlists widget types for incremental multi-query Lucene merge rollout. Example widget types: `piechart`, `timeseries`, `barchart`.

---

## Supported Regions

| Region Code | Coverage |
|---|---|
| `eu1`, `eu2` | Europe |
| `us1`, `us2` | United States |
| `ap1`, `ap2`, `ap3` | Asia Pacific |
| `in1` | India |

---

## Environment Variables

| Variable | Used by | Notes |
|---|---|---|
| `GRAFANA_API_KEY` | `migrate` | First priority for Grafana API key (falls back to `credentials.grafanaApiKey`) |
| `CX_API_KEY` | `migrate`, `verify` | First priority for Coralogix API key (falls back to `credentials.cxApiKey`) |

`push` and `import` get the API key from the interactive session.

---

## Other Commands

All commands run from repository root:

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- <command> [options]
```

### `convert`

Convert one Grafana dashboard JSON file locally (no API calls):

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- convert ./dashboard.json -o ./dashboard_cx.json
```

| Argument/Flag | Description |
|---|---|
| `<input>` | Input Grafana dashboard JSON file or directory |
| `-o`, `--output` | Output file or directory (default: `<input>_cx.json`) |

### `migrate` (non-interactive)

Bulk migration driven entirely by the settings file — no prompts:

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- migrate --settings migration-settings.json
```

| Flag | Description |
|---|---|
| `-s`, `--settings` | Path to migration settings JSON (default: `migration-settings.json`) |
| `-I`, `--interactive` | Enable guided prompts for folder mapping and conflict handling |

API key precedence for non-interactive `migrate`:
1. `GRAFANA_API_KEY` / `CX_API_KEY` environment variables
2. `credentials.grafanaApiKey` / `credentials.cxApiKey` in the settings file

### `push`

Available via **interactive mode** (menu option 2). Configure Coralogix region and API key, then choose Push to upload a single dashboard.

### `import`

Available via **interactive mode** (menu option 3). Upload many local Grafana dashboard JSON files from a directory with prompts for folder mapping.

### `verify`

Convert, fetch from Coralogix, and compare conversion output:

```bash
dotnet run --project ./src/GrafanaToCx.Cli/GrafanaToCx.Cli.csproj -- verify ./dashboard.json -e https://api.coralogix.com/mgmt/openapi/latest -d DASHBOARD_ID
```

| Argument/Flag | Description |
|---|---|
| `<input>` | Input Grafana dashboard JSON file |
| `-e`, `--endpoint` | Coralogix API endpoint |
| `-d`, `--dashboard-id` | CX dashboard ID to verify against |

---

## Integration Settings and Live Test

- Commit-safe template: `src/GrafanaToCx.Cli/migration-settings.integration.example.json`
- Local secret-bearing file (git-ignored): `src/GrafanaToCx.Cli/migration-settings.integration.json`

```bash
export GRAFANA_TO_CX_INTEGRATION_SETTINGS=src/GrafanaToCx.Cli/migration-settings.integration.json
dotnet test --filter "FullyQualifiedName~MigrationFlowIntegrationTests"
```

---

## Playwright Migration Validation (Grafana vs Coralogix)

End-to-end migration checks under `tests/e2e` validate zero visible errors on both platforms, data presence, and tolerance-based numeric comparison for matched panel titles.

### 1) Install Playwright tooling

```bash
npm install
npm run e2e:install
```

### 2) Create one-time auth storage state

```bash
npm run e2e:auth
```

Complete login in the headed browser, then press Enter to save `tests/e2e/.auth/storage-state.json`.

### 3) Configure dashboards by name

Edit `tests/e2e/dashboard-selection.json`. Set `dashboards` to Grafana dashboard titles to validate — names are resolved against `migration-checkpoint.json` and must map to unique `Completed` entries.

### 4) Run migration comparison tests

```bash
npm run e2e:test
```

For interactive debugging:

```bash
npm run e2e:headed
```

Failure artifacts are written to `tests/e2e/artifacts/<dashboard-name>/`.

---

## Troubleshooting

- `dotnet: command not found` — install .NET 9 SDK and restart terminal.
- `401/403` API errors — verify token/key, scopes/permissions, and endpoint/region alignment.
- Dashboard skipped during migration — check `migration-report.txt` and re-run; the checkpoint will resume from where it left off.
- Unexpected conversion output — run `verify` on the same input to compare round-trip behaviour.
- Rate limits/timeouts — increase `maxRetries` and `initialRetryDelaySeconds` in `migration-settings.json`.
- Migration stopped mid-run — re-run the same command; completed dashboards are skipped automatically via checkpoint.
