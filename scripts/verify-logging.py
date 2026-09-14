"""Verify actual indexed events, levels, CRUD correlation and Kubernetes metadata."""
import argparse
import json
from pathlib import Path
import time
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://localhost:9200')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
checks = json.loads((root/'docs/results/logging/http-checks.json').read_text(encoding='utf-8-sig'))
required = {'application_started':'INFO','application_stopping':'INFO','application_stopped':'INFO',
    'http_request':'INFO','users_listed':'INFO','user_created':'INFO','user_read':'INFO',
    'user_updated':'INFO','user_deleted':'INFO','validation_failed':'WARN','user_not_found':'WARN',
    'user_conflict':'WARN','http_error':'ERROR','database_error':'ERROR',
    'migration_started':'INFO','migration_completed':'INFO'}
def search(query):
    request = urllib.request.Request(args.url+'/fluent-bit-logs-*/_search',
        data=json.dumps(query).encode(), headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)
# Look only at the current deployment/test hour, never satisfy checks with old homework data.
query = {'size':0,'query':{'bool':{'filter':[{'term':{'service':'user-service'}},
    {'range':{'@timestamp':{'gte':'now-1h'}}}]}},'aggs':{'events':{'terms':{'field':'event','size':50},
    'aggs':{'levels':{'terms':{'field':'level'}},'sample':{'top_hits':{'size':1}}}}}}
for attempt in range(30):
    result = search(query)
    events = {b['key']:b for b in result['aggregations']['events']['buckets']}
    missing = set(required)-set(events)
    if not missing: break
    time.sleep(2)
assert not missing, f'Missing indexed events: {sorted(missing)}'
for event, level in required.items():
    assert level in {b['key'] for b in events[event]['levels']['buckets']}, (event,level)
    sample = events[event]['sample']['hits']['hits'][0]['_source']
    assert sample.get('kubernetes',{}).get('pod_name'), (event,'missing Kubernetes metadata')
request_sample = events['http_request']['sample']['hits']['hits'][0]['_source']
assert all(request_sample.get(k) for k in ['method','path','ip','request_id'])
user_result = search({'size':100,'query':{'bool':{'filter':[
    {'term':{'service':'user-service'}},{'term':{'user_id':checks['user_id']}}]}},
    'sort':[{'@timestamp':'asc'}]})
user_events = {hit['_source']['event'] for hit in user_result['hits']['hits']}
assert {'user_created','user_read','user_updated','user_deleted','user_not_found'} <= user_events
for hit in user_result['hits']['hits']:
    assert hit['_source'].get('request_id'), 'CRUD event lost request scope'
summary = {'checkedAt':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'user_id':checks['user_id'],
    'events':{e:{'count':events[e]['doc_count'],'level':level} for e,level in required.items()},
    'user_events':sorted(user_events),'passed':True}
(root/'docs/results/logging/index-check.json').write_text(json.dumps(summary,indent=2))
(root/'docs/results/logging/event-samples.json').write_text(json.dumps(
    {e:events[e]['sample']['hits']['hits'][0]['_source'] for e in required},indent=2))
print(json.dumps(summary,indent=2))
