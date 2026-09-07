"""Validate every dashboard expression against actual Prometheus samples."""
import argparse
import json
import math
from pathlib import Path
import time
from urllib.parse import urlencode
from urllib.request import urlopen

parser = argparse.ArgumentParser()
parser.add_argument("--prometheus", default="http://localhost:9095")
parser.add_argument("--start", type=float, default=None, help="Unix seconds")
parser.add_argument("--end", type=float, default=None, help="Unix seconds")
parser.add_argument("--require-faults", action="store_true")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
end = args.end or time.time()
start = args.start or end - 600


def get(path, **params):
    with urlopen(args.prometheus + path + "?" + urlencode(params), timeout=30) as response:
        result = json.load(response)
    if result.get("status") != "success":
        raise RuntimeError(result)
    return result["data"]


targets = get("/api/v1/targets")["activeTargets"]
for job, minimum in {"blt-users-api": 2, "ingress-nginx": 1, "postgres-exporter": 1, "kubernetes-cadvisor": 1}.items():
    selected = [t for t in targets if t["labels"].get("job") == job]
    assert len(selected) >= minimum, (job, "missing scrape targets")
    assert all(t["health"] == "up" for t in selected), [(t["scrapeUrl"], t["lastError"]) for t in selected]

dashboard = json.loads((root / "monitoring/grafana/dashboards/blt-users-overview.json").read_text(encoding="utf-8"))
checks = []
for panel in dashboard["panels"]:
    for target in panel.get("targets", []):
        expr = target["expr"].replace("$__range", f"{int(end-start)}s")
        data = get("/api/v1/query_range", query=expr, start=start, end=end, step=10)
        values = [float(value) for series in data["result"] for _, value in series["values"] if math.isfinite(float(value))]
        assert values, (panel["title"], target["legendFormat"], "no finite data")
        checks.append({"panel": panel["title"], "legend": target["legendFormat"], "expression": expr,
                       "series": len(data["result"]), "samples": len(values), "min": min(values), "max": max(values)})

if args.require_faults:
    for name in ("API · HTTP 500 responses / 1m", "Ingress · HTTP 500 responses / 1m"):
        assert next(c["max"] for c in checks if c["panel"] == name) > 0, (name, "no injected errors recorded")
    assert next(c["max"] for c in checks if c["panel"] == "API latency · p95") > .3

report = {"checkedAt": time.time(), "start": start, "end": end,
          "targets": [{"labels": t["labels"], "health": t["health"], "error": t["lastError"]} for t in targets],
          "queries": checks}
path = root / "docs/results/metrics-check.json"
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"OK: {len(targets)} healthy targets; {len(checks)} dashboard queries have real, finite samples.")
