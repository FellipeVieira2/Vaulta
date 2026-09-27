"""Read resolved Compose JSON from stdin; only configuration key names may be reported."""
import json
import re
import sys


def validate(config):
    services = config['services']
    pg = services['postgres']['environment']
    api = services['vaulta-api']['environment']
    # Safe interpolation into Npgsql; use generated hex/base64 passwords, not connection-string syntax.
    if not re.fullmatch(r'[A-Za-z0-9_]{1,63}', pg['POSTGRES_DB']):
        raise ValueError('POSTGRES_DB must be a simple identifier.')
    if not re.fullmatch(r'[A-Za-z0-9_]{1,63}', pg['POSTGRES_USER']):
        raise ValueError('POSTGRES_USER must be a simple identifier.')
    if not re.fullmatch(r'[A-Za-z0-9+/=_-]{32,}', pg['POSTGRES_PASSWORD']):
        raise ValueError('POSTGRES_PASSWORD requires at least 32 generated hex/base64 characters.')
    jwt = api.get('Jwt__Secret', '')
    if len(jwt.encode()) < 32 or jwt.lower().startswith(('change-me', 'development-secret')):
        raise ValueError('JWT_SECRET requires at least 32 random bytes.')
    for key in ('Jwt__Issuer', 'Jwt__Audience', 'Assets__S3__Bucket', 'Assets__S3__Region'):
        if not api.get(key, '').strip():
            raise ValueError('Required API setting: ' + key)
    if services['postgres'].get('ports'):
        raise ValueError('PostgreSQL must not publish ports.')
    for port in services['vaulta-api'].get('ports', []):
        if port.get('host_ip') != '127.0.0.1':
            raise ValueError('API port must bind only to loopback.')
    for key in ('AWS_ACCESS_KEY_ID', 'AWS_SECRET_ACCESS_KEY', 'AWS_SESSION_TOKEN',
                'Assets__S3__AccessKey', 'Assets__S3__SecretKey', 'Assets__S3__ServiceUrl'):
        if key in api:
            raise ValueError('Forbidden production API setting: ' + key)


if __name__ == '__main__':
    try:
        validate(json.load(sys.stdin))
    except ValueError as error:
        # JSON parser errors can contain input; only our validation messages are safe.
        sys.exit('Invalid Compose JSON.' if isinstance(error, json.JSONDecodeError) else str(error))
    except (KeyError, TypeError):
        sys.exit('Missing or invalid production Compose configuration.')
