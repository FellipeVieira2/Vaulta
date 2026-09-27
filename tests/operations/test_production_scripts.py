"""Exercise destructive boundaries with fake processes; Docker/DB behavior is covered by the smoke job."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ProductionScriptsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        shutil.copytree(ROOT / 'scripts', self.root / 'scripts')
        shutil.copy(ROOT / 'docker-compose.production.yml', self.root)
        env_file = self.root / '.env.production'
        env_file.write_text('')
        env_file.chmod(0o600)
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.calls = self.root / 'calls'
        self.env = {**os.environ, 'PATH': str(self.bin) + ':' + os.environ['PATH'],
                    'CALLS': str(self.calls), 'IMAGE_TAG': 'abc1234'}
        api = {'Jwt__Secret': 'a' * 64, 'Jwt__Issuer': 'vaulta', 'Jwt__Audience': 'vaulta-app',
               'Assets__S3__Bucket': 'test-bucket', 'Assets__S3__Region': 'us-east-1'}
        config = {'services': {'postgres': {'environment': {'POSTGRES_DB': 'vaulta', 'POSTGRES_USER': 'vaulta', 'POSTGRES_PASSWORD': 'b' * 64}},
                  'vaulta-api': {'environment': api, 'ports': [{'host_ip': '127.0.0.1'}]}}}
        self.env['MOCK_CONFIG'] = json.dumps(config)
        self.command('docker', '''
import json, os, sys
args = sys.argv[1:]
with open(os.environ['CALLS'], 'a') as log: log.write('docker ' + ' '.join(args) + '\\n')
if 'config' in args: print(os.environ['MOCK_CONFIG'])
if 'login' in args: sys.stdin.read()
if '--migrate' in args and os.environ.get('FAIL_MIGRATE'): sys.exit(17)
if 'pull' in args and os.environ.get('FAIL_PULL'): sys.exit(18)
if 'exec' in args:
    if os.environ.get('FAIL_DUMP'): sys.exit(19)
    print('-- disposable dump')
''')
        self.command('aws', '''
import os, sys
with open(os.environ['CALLS'], 'a') as log: log.write('aws ' + ' '.join(sys.argv[1:]) + '\\n')
assert 'AWS_ACCESS_KEY_ID' not in os.environ
assert os.environ['AWS_SHARED_CREDENTIALS_FILE'] == '/dev/null'
if 'get-login-password' in sys.argv: print('disposable-token')
''')
        self.command('curl', "import os, sys; sys.exit(22 if os.environ.get('FAIL_HEALTH') else 0)")
        self.command('sleep', 'pass')

    def command(self, name, body):
        file = self.bin / name
        file.write_text('#!/usr/bin/env python3\n' + body)
        file.chmod(0o755)

    def run_script(self, name, *args, **env):
        return subprocess.run(['bash', str(self.root / 'scripts' / name), *args],
                              env={**self.env, **env}, text=True, capture_output=True)

    def log(self):
        return self.calls.read_text() if self.calls.exists() else ''

    def test_migration_failure_never_replaces_api_or_records_tag(self):
        result = self.run_script('deploy-production.sh', 'abc1234', FAIL_MIGRATE='1')
        self.assertEqual(17, result.returncode, result.stderr)
        self.assertNotIn('up -d --no-deps vaulta-api', self.log())
        self.assertFalse((self.root / '.deploy/current-tag').exists())

    def test_pull_failure_never_starts_postgres(self):
        result = self.run_script('deploy-production.sh', 'abc1234', FAIL_PULL='1')
        self.assertEqual(18, result.returncode, result.stderr)
        self.assertNotIn('up -d', self.log())

    def test_success_records_tag_only_after_migration_and_restart(self):
        result = self.run_script('deploy-production.sh', 'abc1234', AWS_ACCESS_KEY_ID='must-be-removed')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('abc1234', (self.root / '.deploy/current-tag').read_text().strip())
        self.assertLess(self.log().index('--migrate'), self.log().index('up -d --no-deps vaulta-api'))
        again = self.run_script('deploy-production.sh', 'def5678')
        self.assertEqual(0, again.returncode, again.stderr)
        self.assertEqual('abc1234', (self.root / '.deploy/previous-tag').read_text().strip())

    def test_readiness_failure_preserves_last_successful_tag(self):
        (self.root / '.deploy').mkdir()
        (self.root / '.deploy/current-tag').write_text('previous')
        result = self.run_script('deploy-production.sh', 'abc1234', FAIL_HEALTH='1')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual('previous', (self.root / '.deploy/current-tag').read_text())

    def test_invalid_tags_never_call_docker(self):
        for tag in ('latest', '../invalid', 'x;echo unsafe', ''):
            self.assertNotEqual(0, self.run_script('deploy-production.sh', tag).returncode)
        self.assertEqual('', self.log())

    def test_failed_dump_never_uploads_partial_backup(self):
        result = self.run_script('backup-postgres.sh', FAIL_DUMP='1')
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn('aws ', self.log())
        self.assertEqual([], list((self.root / '.deploy').glob('backup.*')))

    def test_backup_upload_is_private_and_only_after_successful_dump(self):
        result = self.run_script('backup-postgres.sh')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('--sse AES256 --only-show-errors', self.log())
        self.assertIn('/backups/postgres/', self.log())
        self.assertNotIn('--acl', self.log())


if __name__ == '__main__':
    unittest.main()
