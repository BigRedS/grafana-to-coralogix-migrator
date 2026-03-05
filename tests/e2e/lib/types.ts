export interface ToleranceConfig {
  absolute: number;
  relative: number;
}

export interface DashboardSelectionContract {
  checkpointPath: string;
  artifactsDir: string;
  tolerance: ToleranceConfig;
  dashboards: string[];
}

export interface CompletedDashboard {
  name: string;
  grafanaDashboardId: string;
  cxDashboardId: string;
}

export interface PanelValueSnapshot {
  panelId: string;
  panelName: string;
  rawText: string;
  numericValues: number[];
  hasNoData: boolean;
  hasVisibleError: boolean;
}

export interface DashboardSnapshot {
  pageKind: "grafana" | "coralogix";
  dashboardName: string;
  url: string;
  panels: PanelValueSnapshot[];
}

export interface ComparisonResult {
  panelName: string;
  grafanaValue: number;
  coralogixValue: number;
  withinTolerance: boolean;
  absoluteDelta: number;
  relativeDelta: number;
}
