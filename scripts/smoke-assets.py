"""Explicit operator smoke: writes one collection item and one private asset. Never run automatically on AWS."""
import argparse
import base64
import getpass
import hashlib
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('api_url', help='HTTPS API URL behind Nginx')
    parser.add_argument('printing_id', type=uuid.UUID, help='An active Catalog PrintingId')
    args = parser.parse_args()
    base = args.api_url.rstrip('/')
    if urllib.parse.urlsplit(base).scheme != 'https':
        parser.error('Use the HTTPS production endpoint.')
    email = input('Email of a dedicated smoke account: ')
    password = getpass.getpass('Password: ')

    def request(path, payload=None, token=None):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        if path == '/api/v1/me/collection/items':
            headers['Idempotency-Key'] = str(uuid.uuid4())
        req = urllib.request.Request(base + path, headers=headers,
                                     data=None if payload is None else json.dumps(payload).encode())
        with urllib.request.urlopen(req, timeout=60) as response:
            content = response.read()
            return json.loads(content) if content else None

    token = request('/api/v1/auth/login', {'email': email, 'password': password})['accessToken']
    data = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=')
    asset = request('/api/v1/assets/uploads', {'purpose': 'collection-item', 'contentType': 'image/png',
                    'contentLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}, token)
    upload = urllib.request.Request(asset['uploadUrl'], data=data, method='PUT', headers={'Content-Type': 'image/png'})
    with urllib.request.urlopen(upload, timeout=60) as response:
        assert response.status == 200
    assert request('/api/v1/assets/' + asset['assetId'] + '/confirm', {}, token)['status'] == 'ready'
    added = request('/api/v1/me/collection/items', {'printingId': str(args.printing_id), 'quantity': 1, 'condition': 'NEAR_MINT'}, token)
    item_id = added['createdItems'][0]['id']
    request('/api/v1/me/collection/items/' + item_id + '/assets', {'assetId': asset['assetId'], 'type': 'FRONT', 'sortOrder': 0}, token)
    item = request('/api/v1/me/collection/items/' + item_id, token=token)
    url = next(a['url'] for a in item['assets'] if a['assetId'] == asset['assetId'])
    with urllib.request.urlopen(url, timeout=60) as response:
        assert response.read() == data
    parsed = urllib.parse.urlsplit(url)
    unsigned = urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, parsed.path, '', ''))
    try:
        with urllib.request.urlopen(unsigned, timeout=30):
            raise AssertionError('Unsigned read unexpectedly succeeded.')
    except urllib.error.HTTPError as error:
        assert error.code == 403
    print('PASS: presigned PUT, Confirm, Attach, presigned GET and anonymous denial.')
    print('Persisted smoke item: ' + item_id + '; asset: ' + asset['assetId'])


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Exceptions/tracebacks may contain signed URLs. Report only a type and HTTP status.
        print('Smoke failed: ' + type(error).__name__ +
              (' HTTP ' + str(error.code) if isinstance(error, urllib.error.HTTPError) else ''), file=sys.stderr)
        sys.exit(1)
