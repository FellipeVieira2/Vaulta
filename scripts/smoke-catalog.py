"""Run against a disposable development API after syncing tcgdex/base1. No artwork is downloaded."""
import json
import secrets
import sys
import uuid
import urllib.request
from pathlib import Path

base = sys.argv[1].rstrip('/') if len(sys.argv) > 1 else 'http://127.0.0.1:8080'

def request(path, body=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if path == '/api/v1/me/collection/items' and body is not None:
        headers['Idempotency-Key'] = str(uuid.uuid4())
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers)
    with urllib.request.urlopen(req, timeout=30) as response:
        return response.status, json.load(response)

_, page = request('/api/v1/catalog/search?q=pikachu&game=pokemon')
assert page['totalCount'] > 0
selected = next(item for item in page['items'] if item['artworkUrl'])
_, printing = request('/api/v1/catalog/printings/' + selected['printingId'])
assert printing['printingId'] == selected['printingId']
assert printing['artworkUrl'] == selected['artworkUrl']
for key in ('cardId', 'setId', 'setName', 'collectorNumber', 'language', 'rarity'):
    assert printing[key], key
assert printing['variants']
for variant in printing['variants']:
    uuid.UUID(variant['id'])
    assert variant['code'] and variant['name']
# Fresh disposable user; credentials and access token are never printed or written to the report.
registration = {'email': secrets.token_hex(12) + '@example.com', 'username': 'smoke' + secrets.token_hex(8),
                'password': secrets.token_hex(20) + 'Aa1!', 'displayName': 'Catalog smoke'}
assert request('/api/v1/auth/register', registration)[0] == 201
_, auth = request('/api/v1/auth/login', {'email': registration['email'], 'password': registration['password']})
status, added = request('/api/v1/me/collection/items', {
    'printingId': printing['printingId'], 'variantId': printing['variants'][0]['id'],
    'quantity': 2, 'condition': 'NEAR_MINT'
}, auth['accessToken'])
assert status == 201 and added['quantity'] == 2 and len(added['createdItems']) == 2
_, collection = request('/api/v1/me/collection', token=auth['accessToken'])
assert collection['totalCount'] == 1 and collection['items'][0]['quantity'] == 2
_, entry = request('/api/v1/me/collection/entries/' + added['collectionEntryId'], token=auth['accessToken'])
assert entry['quantity'] == 2 and len(entry['items']) == 2
result = {'provider': 'tcgdex', 'scope': 'base1', 'printing': printing,
          'collectionStatus': status, 'collectionEntries': collection['totalCount'], 'collectibleItems': len(entry['items'])}
Path('catalog-smoke-result.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
