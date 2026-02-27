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

- [How It Works](#how-it-works)
- [Supported Panel Types](#supported-panel-types)
- [Supported Query Languages](#supported-query-languages)
- [Project Structure](#project-structure)
- [Setup](#setup)
- [Build](#build)
- [How to Run](#how-to-run)
  - [Interactive Mode](#interactive-mode)
  - [`convert`](#convert)
  - [`push`](#push)
  - [`migrate`](#migrate)
  - [`import`](#import)
  - [`verify`](#verify)
- [Migration Settings](#migration-settings)
- [Supported Regions](#supported-regions)
- [Environment Variables](#environment-variables)
- [Troubleshooting](#troubleshooting)
- [Checkpoint and Resume](#checkpoint-and-resume)

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

## Setup

### 1) Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Grafana API token (for `migrate`)
- Coralogix API key (for `push`, `migrate`, `import`, `verify`)

### 2) Clone and enter the project

```bash
git clone <your-repo-url>
cd grafana_to_cx_custom_converter
```

### 3) Restore dependencies

```bash
dotnet restore GrafanaToCx.sln
```

### 4) Configure credentials (recommended for migrate)

```bash
export GRAFANA_API_KEY=glsa_xxxxxxxxxxxx
export CX_API_KEY=cxtp_xxxxxxxxxxxx
```

If these are not exported, the CLI may prompt you in interactive flows.

---

## Build

```bash
dotnet build GrafanaToCx.sln
```

---

## How to Run

All commands run from repository root. The CLI uses Sharprompt for interactive flows when no command is given.

```bash
dotnet run --project src/GrafanaToCx.Cli -- <command> [options]
```

### Interactive Mode (Sharprompt)

Run with no command to enter the guided menu. Uses Sharprompt for prompts (Select, MultiSelect, Confirm, Input, Password):

```bash
dotnet run --project src/GrafanaToCx.Cli
```

From the menu you can: **Convert**, **Push**, **Import**, **Migrate**, **Settings**, **Cleanup**, or **Exit**. Push and Import are available only via interactive mode.

### `convert`

Convert one Grafana dashboard JSON file locally (no API calls):

```bash
dotnet run --project src/GrafanaToCx.Cli -- convert ./dashboard.json -o ./dashboard_cx.json
```

| Argument/Flag | Description |
|---|---|
| `<input>` | Input Grafana dashboard JSON file or directory |
| `-o`, `--output` | Output file or directory (default: `<input>_cx.json`) |

### `push`

Push is available via **interactive mode** (menu option 2). Configure Coralogix region and API key in the session, then choose Push to upload a single dashboard.

### `migrate`

Bulk migration from live Grafana API to Coralogix using a settings file:

```bash
dotnet run --project src/GrafanaToCx.Cli -- migrate --settings migration-settings.json
```

Interactive migration mode (Sharprompt prompts for folder selection and mapping):

```bash
dotnet run --project src/GrafanaToCx.Cli -- migrate -s migration-settings.json -I
```

| Flag | Description |
|---|---|
| `-s`, `--settings` | Path to migration settings JSON (default: `migration-settings.json`) |
| `-I`, `--interactive` | Enable Sharprompt prompts for folder mapping and conflict handling |

### `import`

Import is available via **interactive mode** (menu option 3). Choose Import to upload many local Grafana dashboard JSON files from a directory, with prompts for folder mapping.

### `verify`

Convert, fetch from Coralogix, and compare conversion output. Requires `CX_API_KEY` in the environment.

```bash
dotnet run --project src/GrafanaToCx.Cli -- verify ./dashboard.json -e https://api.coralogix.com/mgmt/openapi/latest -d DASHBOARD_ID
```

| Argument/Flag | Description |
|---|---|
| `<input>` | Input Grafana dashboard JSON file |
| `-e`, `--endpoint` | Coralogix API endpoint (default: `https://api.coralogix.com/mgmt/openapi/latest`) |
| `-d`, `--dashboard-id` | CX dashboard ID to verify against (fetches from API) |

---

## Migration Settings

Use a JSON file (example below) with `migrate`:

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
| `grafana.folders` | Grafana folders to migrate (empty means all) |
| `coralogix.region` | Coralogix region (used to resolve endpoint) |
| `coralogix.folderId` | Fallback CX folder ID when mapping is missing |
| `coralogix.isLocked` | Lock uploaded dashboards |
| `coralogix.migrateFolderStructure` | Recreate Grafana folder structure in Coralogix |
| `coralogix.parentFolderId` | Parent folder for newly created Coralogix folders |
| `migration.checkpointFile` | Checkpoint file path for resume |
| `migration.reportFile` | Human-readable migration report path |
| `migration.maxRetries` | Max retries per dashboard |
| `migration.initialRetryDelaySeconds` | Initial exponential backoff delay |

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
| `GRAFANA_API_KEY` | `migrate` | Grafana token with `Viewer` role or higher |
| `CX_API_KEY` | `migrate` | Coralogix key with dashboard read/write permissions |

`push` and `import` get the API key from the interactive session; `verify` reads `CX_API_KEY` from the environment.

---

## Troubleshooting

- `dotnet: command not found`: install .NET 9 SDK and restart terminal.
- `401/403` API errors: verify token/key, scopes/permissions, and endpoint/region alignment.
- Dashboard skipped during migration: check `migration-report.txt` and re-run with same checkpoint.
- Unexpected conversion output: run `verify` on the same input to compare round-trip behavior.
- Rate limits/timeouts: increase retry settings in `migration-settings.json`.

---

## Checkpoint and Resume

During `migrate`, progress is saved after each dashboard to `migration-checkpoint.json` (or your configured path). If migration stops, rerun the same command to resume from the checkpoint.

A run summary is written to `migration-report.txt`.
