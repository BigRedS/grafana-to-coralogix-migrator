import fs from "node:fs";
import path from "node:path";
import type { CompletedDashboard, DashboardSelectionContract } from "./types";

const NAME_KEYS = ["GrafanaTitle", "dashboardName", "name", "grafanaDashboardName", "title"];
const GRAFANA_ID_KEYS = ["GrafanaUid", "grafanaDashboardId", "grafanaId", "sourceDashboardId", "dashboardUid", "uid"];
const CX_ID_KEYS = ["CxDashboardId", "cxDashboardId", "coralogixDashboardId", "targetDashboardId", "dashboardId"];

export function loadSelectionContract(rootDir: string): DashboardSelectionContract {
  const selectionPath = path.join(rootDir, "tests/e2e/dashboard-selection.json");
  const raw = fs.readFileSync(selectionPath, "utf8");
  const contract = JSON.parse(raw) as DashboardSelectionContract;

  if (!Array.isArray(contract.dashboards)) {
    throw new Error(`Invalid dashboards list in ${selectionPath}: expected an array of names.`);
  }

  if (!contract.tolerance || typeof contract.tolerance.absolute !== "number" || typeof contract.tolerance.relative !== "number") {
    throw new Error(`Invalid tolerance config in ${selectionPath}: expected numeric absolute and relative values.`);
  }

  return contract;
}

export function resolveCompletedDashboardsByName(
  rootDir: string,
  contract: DashboardSelectionContract
): CompletedDashboard[] {
  const checkpointPath = path.resolve(rootDir, contract.checkpointPath);
  if (!fs.existsSync(checkpointPath)) {
    throw new Error(
      `Checkpoint file not found at ${checkpointPath}. Update tests/e2e/dashboard-selection.json or run migration first.`
    );
  }
  const checkpointRaw = fs.readFileSync(checkpointPath, "utf8");
  const checkpoint = JSON.parse(checkpointRaw) as unknown;
  const selectedNames = new Set(contract.dashboards.map((name) => name.trim()).filter(Boolean));

  if (selectedNames.size === 0) {
    return [];
  }

  const candidates = flattenCheckpoint(checkpoint);
  const byName = new Map<string, CompletedDashboard>();

  for (const candidate of candidates) {
    const dashboardName = readFirstString(candidate, NAME_KEYS);
    const grafanaDashboardId = readFirstString(candidate, GRAFANA_ID_KEYS);
    const cxDashboardId = readFirstString(candidate, CX_ID_KEYS);

    if (!dashboardName || !grafanaDashboardId || !cxDashboardId || !isCompleted(candidate)) {
      continue;
    }

    if (!selectedNames.has(dashboardName)) {
      continue;
    }

    const existing = byName.get(dashboardName);
    if (existing) {
      throw new Error(`Duplicate completed dashboard found for "${dashboardName}" in checkpoint ${checkpointPath}.`);
    }

    byName.set(dashboardName, { name: dashboardName, grafanaDashboardId, cxDashboardId });
  }

  const missing = [...selectedNames].filter((name) => !byName.has(name));
  if (missing.length > 0) {
    throw new Error(
      `Selected dashboards not found as completed in checkpoint ${checkpointPath}: ${missing.join(", ")}.`
    );
  }

  return [...byName.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function flattenCheckpoint(value: unknown): Record<string, unknown>[] {
  if (Array.isArray(value))
    return value.filter(isRecord);
  if (!isRecord(value))
    return [];

  const aggregateKeys = ["dashboards", "items", "results", "completed", "entries"];
  for (const key of aggregateKeys) {
    const raw = value[key];
    if (Array.isArray(raw))
      return raw.filter(isRecord);
  }

  const values = Object.values(value);
  if (values.length > 0 && values.every(isRecord))
    return values as Record<string, unknown>[];

  return [];
}

function isCompleted(candidate: Record<string, unknown>): boolean {
  const completedFlag = candidate.completed ?? candidate.Completed;
  if (typeof completedFlag === "boolean") {
    return completedFlag;
  }

  const status = asLowerString(candidate.status ?? candidate.Status);
  const state = asLowerString(candidate.state ?? candidate.State);
  const result = asLowerString(candidate.result ?? candidate.Result);

  if (["completed", "success", "succeeded", "done"].includes(status)) {
    return true;
  }
  if (["completed", "success", "succeeded", "done"].includes(state)) {
    return true;
  }
  if (["completed", "success", "succeeded", "done"].includes(result)) {
    return true;
  }

  return false;
}

function asLowerString(value: unknown): string {
  return typeof value === "string" ? value.toLowerCase() : "";
}

function readFirstString(obj: Record<string, unknown>, keys: string[]): string | null {
  for (const key of keys) {
    const value = obj[key];
    if (typeof value === "string" && value.trim().length > 0) {
      return value.trim();
    }
  }
  return null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
