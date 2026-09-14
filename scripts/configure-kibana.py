"""Import the homework data view, saved Discover searches and histogram dashboard."""
import argparse
import json
from pathlib import Path
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://localhost:5601')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
boundary = 'homework6-saved-objects'
body = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="saved-objects.ndjson"\r\n'
        'Content-Type: application/ndjson\r\n\r\n').encode()
body += (root/'logging/kibana/saved-objects.ndjson').read_bytes()
body += f'\r\n--{boundary}--\r\n'.encode()
request = urllib.request.Request(args.url+'/api/saved_objects/_import?overwrite=true', data=body,
    headers={'kbn-xsrf':'homework6','Content-Type':f'multipart/form-data; boundary={boundary}'})
with urllib.request.urlopen(request, timeout=120) as response:
    result = json.load(response)
if not result.get('success'):
    raise RuntimeError(json.dumps(result))
print(json.dumps(result, indent=2))
# Make the imported pattern the default in Discover.
request = urllib.request.Request(args.url+'/api/data_views/default',
    data=json.dumps({'data_view_id':'homework-logs','force':True}).encode(),
    headers={'kbn-xsrf':'homework6','Content-Type':'application/json'})
with urllib.request.urlopen(request, timeout=30) as response:
    print(response.read().decode())
