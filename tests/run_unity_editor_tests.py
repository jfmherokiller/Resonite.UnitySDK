"""Stage the Unity-only converter tests, run them headless, and remove the staged asset."""
import argparse
import os
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--unity', required=True, help='Unity editor executable')
parser.add_argument('--project', required=True, help='Unity project with ResoniteSDK linked in Assets')
args = parser.parse_args()
project = Path(args.project).resolve()
source = Path(__file__).with_name('ExpressionPuppetAnalyzerTests.cs')
staged = project / 'Assets' / 'ResoniteSDK' / 'Editor' / '__ExpressionPuppetAnalyzerTests.cs'
meta = staged.with_suffix('.cs.meta')
log = project / 'Library' / 'expression-puppet-tests.log'
if staged.exists() or meta.exists():
    parser.error(f'Test staging path is occupied: {staged} (or its .meta)')

try:
    staged.write_bytes(source.read_bytes())
    result = subprocess.run([args.unity, '-batchmode', '-nographics', '-quit',
        '-projectPath', str(project), '-executeMethod', 'ExpressionPuppetAnalyzerTests.RunFromCommandLine',
        '-logFile', str(log)], timeout=900)
    text = log.read_text(errors='replace') if log.exists() else ''
    markers = [line for line in text.splitlines() if 'EXPRESSION_PUPPET_TESTS_' in line or 'error CS' in line]
    print('\n'.join(markers[-20:]) or f'No test result marker; inspect {log}')
    sys.exit(result.returncode if result.returncode else (0 if 'EXPRESSION_PUPPET_TESTS_PASS' in text else 1))
finally:
    staged.unlink(missing_ok=True)
    meta.unlink(missing_ok=True)
