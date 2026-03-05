# Query Shape Contract

Last updated: 2026-03-01  
Scope: widget query objects emitted by converter and planner paths.

## Top Architectural Characteristics

| Characteristic | Priority | Why |
|---|---:|---|
| Correctness | 1 | one wrong branch key can break widget execution |
| Testability | 2 | query shape must be assertable with pure JSON tests |
| Maintainability | 3 | shared shape rules reduce per-converter divergence |
| Observability | 4 | violations must fail loudly in CI and migration reports |

## Oneof Branch Contract

Exactly one query branch is allowed per query object.

| Query path | Allowed branches |
|---|---|
| `definition.pieChart.query` | `logs` or `metrics` or `dataprime` |
| `definition.barChart.query` | `logs` or `metrics` |
| `definition.gauge.query` | `logs` or `metrics` |
| `definition.dataTable.query` | `logs` or `metrics` |
| `definition.lineChart.queryDefinitions[*].query` | `logs` or `metrics` |

Violation examples:
- `logs` + `metrics` present together.
- legacy `dataPrime` present in newly produced converter output.

## Branch Shape Requirements

| Branch | Required fields | Optional fields | Forbidden siblings |
|---|---|---|---|
| `logs` | `filters` array | `luceneQuery.value`, `aggregation`, `aggregations`, `groupBy*`, `groupNamesFields` | `metrics`, `dataprime`, `dataPrime` |
| `metrics` | `promqlQuery.value`, `filters` array | `aggregation`, `editorMode`, `groupNames`, `promqlQueryType` | `logs`, `dataprime`, `dataPrime` |
| `dataprime` | `dataprimeQuery.text`, `filters` array, `groupNames` array | none | `logs`, `metrics`, `dataPrime` |

## Normalization Rules

| Rule ID | Input | Required output |
|---|---|---|
| `NORM-VAR-001` | `$name` placeholder | `${name}` (except Grafana built-ins) |
| `NORM-LUC-001` | empty or `*` Lucene | omit `luceneQuery` key |
| `NORM-MET-001` | empty PromQL expr | `promqlQuery.value = "up"` |
| `NORM-DP-001` | legacy `dataPrime.value` payload in pie path | canonical `dataprime.dataprimeQuery.text` |
| `NORM-DP-002` | legacy payload contains `logs.groupNamesFields[*].keypath` | map to `dataprime.groupNames` |
| `NORM-DP-003` | legacy `dataPrime` + other branch | emit only `dataprime` branch |

## Upload Boundary Contract

| Boundary | Rule |
|---|---|
| Converter output | emit canonical `dataprime`; do not emit new `dataPrime` |
| Sanitizer input | may receive legacy `dataPrime` and preserve via adjacent markdown widget |
| Sanitizer output | must not contain `pieChart.query.dataPrime` |

## CI Validation Rules

| Gate | Enforcement rule |
|---|---|
| `QSHAPE-001` | fail if any query object has `!= 1` active branch |
| `QSHAPE-002` | fail if converter output contains `dataPrime` key |
| `QSHAPE-003` | fail if `dataprime` lacks `dataprimeQuery.text`, `filters`, or `groupNames` |
| `QSHAPE-004` | fail if `promqlQuery.value` missing where `metrics` branch exists |

