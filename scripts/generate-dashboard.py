"""Generate reviewable Grafana JSON; run with Python 3, no dependencies."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
DS = {"type": "prometheus", "uid": "prometheus"}
API = 'job="blt-users-api",namespace="blt"'
ING = 'job="ingress-nginx",namespace="blt",service="blt-users-api"'
DB = 'job="postgres-exporter",namespace="blt",datname="users"'
GROUP = "method, route"
LEGEND = "{{method}} {{route}}"
api_total = f'sum by ({GROUP}) (rate(blt_http_requests_total{{{API}}}[1m]))'
api_errors = f'sum by ({GROUP}) (rate(blt_http_requests_total{{{API},status_code=~"5.."}}[1m]))'
api_ratio = f'100 * ({api_errors}) / clamp_min(({api_total}), 1e-9)'
ing_total = f'sum(rate(nginx_ingress_controller_requests{{{ING}}}[1m]))'
ing_errors = f'(sum(rate(nginx_ingress_controller_requests{{{ING},status=~"5.."}}[1m])) or ({ing_total}) * 0)'
ing_ratio = f'100 * {ing_errors} / clamp_min(({ing_total}), 1e-9)'


def latency(q, ingress=False):
    if ingress:
        return f'histogram_quantile({q}, sum by (le) (rate(nginx_ingress_controller_request_duration_seconds_bucket{{{ING}}}[1m])))'
    return f'histogram_quantile({q}, sum by (le, {GROUP}) (rate(blt_http_request_duration_seconds_bucket{{{API}}}[1m])))'


panels = []
next_id = 1


def row(title, y):
    global next_id
    panels.append({"id": next_id, "type": "row", "title": title, "collapsed": False,
                   "panels": [], "gridPos": {"x": 0, "y": y, "w": 24, "h": 1}})
    next_id += 1


def panel(title, queries, unit, x, y, w=6, h=8, description="", stat=False):
    global next_id
    identity = next_id
    targets = [{"refId": chr(65+i), "datasource": DS, "expr": query,
                "legendFormat": label, "range": True, "instant": False, "editorMode": "code"}
               for i, (label, query) in enumerate(queries)]
    options = {"legend": {"displayMode": "table", "placement": "bottom", "calcs": ["lastNotNull"]},
               "tooltip": {"mode": "multi", "sort": "desc"}}
    if any(label == LEGEND for label, _ in queries):
        options["legend"] = {"displayMode": "list", "placement": "bottom", "calcs": []}
    defaults = {"unit": unit, "min": 0, "color": {"mode": "palette-classic"},
                "custom": {"drawStyle": "line", "lineInterpolation": "linear", "lineWidth": 2,
                           "fillOpacity": 9, "showPoints": "never", "spanNulls": False,
                           "axisLabel": "", "axisPlacement": "auto", "axisBorderShow": False,
                           "stacking": {"mode": "none", "group": "A"}}}
    if stat:
        options = {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                   "colorMode": "value", "graphMode": "area", "textMode": "auto", "justifyMode": "auto"}
        if unit == "percent":
            defaults["color"] = {"mode": "thresholds"}
            defaults["thresholds"] = {"mode": "absolute", "steps": [{"color": "green", "value": None}, {"color": "red", "value": 1}]}
    panels.append({"id": identity, "type": "stat" if stat else "timeseries", "title": title,
                   "description": description, "datasource": DS,
                   "gridPos": {"x": x, "y": y, "w": w, "h": h}, "targets": targets,
                   "fieldConfig": {"defaults": defaults, "overrides": []}, "options": options})
    next_id += 1
    return identity


panel("Ingress · RPS", [("RPS", ing_total)], "reqps", 0, 0, w=6, h=4, stat=True)
panel("Ingress · HTTP 5xx", [("5xx", ing_ratio)], "percent", 6, 0, w=6, h=4, stat=True)
panel("API · requests in selected range", [("Requests", f'sum(increase(blt_http_requests_total{{{API}}}[$__range]))')], "short", 12, 0, w=6, h=4, stat=True)
panel("PostgreSQL · available", [("pg_up", 'pg_up{job="postgres-exporter",namespace="blt"}')], "bool", 18, 0, w=6, h=4, stat=True)

row("01 / API METHODS · canonical routes · 1 minute window", 4)
api_latency_id = None
for i, q in enumerate((0.5, 0.95, 0.99)):
    identity = panel(f"API latency · p{int(q*100)}", [(LEGEND, latency(q))], "s", i*6, 5,
                     description="Histogram quantile over 1 minute, merging buckets across both API pods before calculating the quantile.")
    if q == 0.95:
        api_latency_id = identity
panel("API latency · max (exact, 1m)", [(LEGEND, f'max by ({GROUP}) (blt_http_request_duration_max_seconds{{{API}}})')], "s", 18, 5,
      description="Exact per-pod sliding 60-second maximum, then max across pods; expires to zero when idle. Health checks and scrapes are excluded.")
panel("API · RPS by method", [(LEGEND, api_total)], "reqps", 0, 13, w=8)
panel("API · HTTP 500 responses / 1m", [(LEGEND, f'sum by ({GROUP}) (increase(blt_http_requests_total{{{API},status_code="500"}}[1m]))')], "short", 8, 13, w=8,
      description="Only HTTP 500; increase() estimates the count in a rolling minute and may be fractional because of scrape extrapolation.")
api_error_id = panel("API · Error Rate (all HTTP 5xx)", [(LEGEND, api_ratio)], "percent", 16, 13, w=8,
                     description="100 * rate(5xx) / rate(all requests), separately for each method and route. The alert threshold is 1% for 1 minute.")

row("02 / NGINX INGRESS · entire BLT service · 1 minute window", 21)
ing_latency_id = panel("Ingress latency · p50 / p95 / p99 / max", [(f"p{int(q*100)}" if q < 1 else "max (histogram estimate)", latency(q, True)) for q in (.5, .95, .99, 1)], "s", 0, 22,
      description="Controller request_duration_seconds includes proxy/client overhead. max = histogram_quantile(1): upper bucket estimate, NOT an exact observed maximum. Values above 30s are clipped to the highest finite bucket (30s).")
panel("Ingress · RPS", [("RPS", ing_total)], "reqps", 6, 22)
panel("Ingress · HTTP 500 responses / 1m", [("HTTP 500", f'(sum(increase(nginx_ingress_controller_requests{{{ING},status="500"}}[1m])) or ({ing_total}) * 0)')], "short", 12, 22,
      description="Only status=500. A zero appears while traffic exists without a 500 series; missing scrapes are not filled with a constant zero.")
ing_error_id = panel("Ingress · Error Rate (all HTTP 5xx)", [("5xx %", ing_ratio)], "percent", 18, 22)

row("03 / KUBERNETES · application pods · kubelet / cAdvisor", 30)
pod_filter = 'job="kubernetes-cadvisor",namespace="blt",pod=~"blt-users-api-.*",container="api",image!=""'
panel("Memory · working set per pod", [("{{pod}}", f'sum by (pod) (container_memory_working_set_bytes{{{pod_filter}}})')], "bytes", 0, 31, w=12,
      description="Existing Kubernetes cAdvisor metric; container=api excludes pause containers and migration jobs.")
panel("CPU · cores used per pod", [("{{pod}}", f'sum by (pod) (rate(container_cpu_usage_seconds_total{{{pod_filter}}}[1m]))')], "cores", 12, 31, w=12,
      description="CPU seconds / second = CPU cores; 0.1 core is 100 millicores. Existing kubelet/cAdvisor metric, not process instrumentation.")

row("04 / POSTGRESQL · postgres_exporter · database users", 39)
panel("DB · connections", [("connections", f'pg_stat_database_numbackends{{{DB}}}')], "short", 0, 40, w=8)
panel("DB · transactions / second", [("commit", f'rate(pg_stat_database_xact_commit{{{DB}}}[1m])'), ("rollback", f'rate(pg_stat_database_xact_rollback{{{DB}}}[1m])')], "ops", 8, 40, w=8)
panel("DB · rows changed / second", [(op, f'rate(pg_stat_database_tup_{metric}{{{DB}}}[1m])') for op, metric in (("insert", "inserted"), ("update", "updated"), ("delete", "deleted"))], "ops", 16, 40, w=8)
hit = f'rate(pg_stat_database_blks_hit{{{DB}}}[1m])'
read = f'rate(pg_stat_database_blks_read{{{DB}}}[1m])'
panel("DB · buffer cache hit ratio", [("cache hit", f'100 * {hit} / clamp_min(({hit} + {read}), 1e-9)')], "percent", 0, 48, w=8)
panel("DB · rows returned / fetched", [(metric, f'rate(pg_stat_database_tup_{metric}{{{DB}}}[1m])') for metric in ("returned", "fetched")], "ops", 8, 48, w=8)
panel("DB · deadlocks / 1m", [("deadlocks", f'increase(pg_stat_database_deadlocks{{{DB}}}[1m])')], "short", 16, 48, w=8)

dashboard = {"uid": "blt-users-overview", "title": "BLT Users API · OTUS Homework 5", "id": None,
             "tags": ["otus", "homework-5", "prometheus", "postgresql"], "timezone": "browser",
             "schemaVersion": 41, "version": 1, "editable": True, "graphTooltip": 1,
             "refresh": "10s", "time": {"from": "now-15m", "to": "now"}, "timepicker": {},
             "templating": {"list": []}, "annotations": {"list": [{"builtIn": 1,
               "datasource": {"type": "grafana", "uid": "-- Grafana --"}, "enable": True,
               "hide": True, "iconColor": "rgba(255, 166, 0, 1)", "name": "Annotations & Alerts", "type": "dashboard"}]},
             "panels": panels}


def alert(uid, title, expr, limit, panel_id, summary):
    return {"uid": uid, "title": title, "condition": "C", "for": "1m", "noDataState": "OK",
            "execErrState": "Error", "isPaused": False,
            "annotations": {"summary": summary, "description": "Sustained for 1 minute. See docs/monitoring.md for diagnosis and the controlled fault scenario.",
                            "__dashboardUid__": dashboard["uid"], "__panelId__": str(panel_id)},
            "labels": {"service": "blt-users-api", "severity": "warning", "homework": "5"},
            "data": [
                {"refId": "A", "datasourceUid": "prometheus", "relativeTimeRange": {"from": 120, "to": 0},
                 "model": {"datasource": DS, "expr": expr, "refId": "A", "instant": True, "range": False,
                           "intervalMs": 1000, "maxDataPoints": 43200, "editorMode": "code"}},
                {"refId": "C", "datasourceUid": "__expr__", "relativeTimeRange": {"from": 0, "to": 0},
                 "model": {"datasource": {"type": "__expr__", "uid": "__expr__"}, "refId": "C",
                           "type": "math", "expression": f"$A > {limit}", "intervalMs": 1000, "maxDataPoints": 43200}}]}


alerts = {"apiVersion": 1, "groups": [{"orgId": 1, "name": "BLT service alerts", "folder": "OTUS",
    "interval": "20s", "rules": [
        alert("blt-api-errors", "API · Error Rate > 1%", api_ratio, 1, api_error_id, "API HTTP 5xx exceeds 1% for {{ $labels.method }} {{ $labels.route }}"),
        alert("blt-api-latency", "API · p95 latency > 300ms", f'({latency(.95)}) and on ({GROUP}) (({api_total}) > 0)', .3, api_latency_id, "API p95 exceeds 300ms for {{ $labels.method }} {{ $labels.route }}"),
        alert("blt-ingress-errors", "Ingress · Error Rate > 1%", ing_ratio, 1, ing_error_id, "Ingress HTTP 5xx exceeds 1%"),
        alert("blt-ingress-latency", "Ingress · p95 latency > 300ms", f'({latency(.95, True)}) and on () (({ing_total}) > 0)', .3, ing_latency_id, "Ingress p95 exceeds 300ms")]}],
    "contactPoints": [{"orgId": 1, "name": "local-webhook", "receivers": [{"uid": "blt-local-webhook", "type": "webhook",
        "disableResolveMessage": False, "settings": {"url": "http://alert-receiver:8080/alerts", "httpMethod": "POST"}}]}],
    "policies": [{"orgId": 1, "receiver": "local-webhook", "group_by": ["grafana_folder", "alertname"],
                  "group_wait": "10s", "group_interval": "30s", "repeat_interval": "1h"}]}

for relative, content in (("monitoring/grafana/dashboards/blt-users-overview.json", dashboard),
                          ("monitoring/grafana/alerting/alerts.json", alerts)):
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(content, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(relative)
