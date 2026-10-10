# /// script
# requires-python = ">=3.11"
# dependencies = ["websockets>=13"]
# ///
"""Run converter-output bundles against an isolated Resonite headless world.

A bundle is the exact ResoniteLink operation batch produced by the Unity converters plus numeric
runtime cases (inputs to write, outputs to read back). See tests/runtime/README.md.
"""
from __future__ import annotations

import argparse
import asyncio
import copy
import datetime
import json
import math
import os
from pathlib import Path
import socket
import subprocess
import sys
import time
import uuid

SCHEMA_VERSION = 1
DEFINING_OPS = {'addSlot', 'addComponent'}
def find_headless():
    """RESONITE_HEADLESS_DIR, else Resonite/Headless in any Steam library (same lookup as the SDK window)."""
    if os.environ.get('RESONITE_HEADLESS_DIR'):
        return Path(os.environ['RESONITE_HEADLESS_DIR'])
    roots = [Path(os.environ[v]) / 'Steam' for v in ('ProgramFiles(x86)', 'ProgramFiles') if os.environ.get(v)]
    roots += [Path.home() / '.steam' / 'steam', Path.home() / '.local' / 'share' / 'Steam']
    libraries = list(roots)
    for root in roots:
        vdf = root / 'steamapps' / 'libraryfolders.vdf'
        if vdf.is_file():
            for line in vdf.read_text(encoding='utf-8', errors='replace').splitlines():
                parts = line.strip().split('"')
                if len(parts) >= 4 and parts[1] == 'path':
                    libraries.append(Path(parts[3].replace('\\\\', '\\')))
    for library in libraries:
        candidate = library / 'steamapps' / 'common' / 'Resonite' / 'Headless'
        if (candidate / 'FrooxEngine.dll').is_file():
            return candidate
    return Path('Resonite/Headless')


DEFAULT_INSTALL = find_headless()
DEFAULT_HOME = Path(os.environ.get('RESONITE_HARNESS_HOME',
                                   Path(os.environ.get('LOCALAPPDATA', Path.home())) / 'ResoniteConverterHarness'))
# Same bridge plugin the Resonite SDK window builds (Unity ignores folders ending in ~).
PLUGIN_PROJECT = (Path(__file__).resolve().parents[2] / 'Assets/ResoniteSDK/Editor/HeadlessExport/HeadlessPlugin~'
                  / 'ResoniteUnitySDK.HeadlessBridge.csproj')
PLUGIN_TYPE = '[ResoniteUnitySDK.HeadlessBridge]ResoniteUnitySDK.Headless.PackageBridge'


class HarnessError(Exception):
    pass


# ---------------------------------------------------------------- static bundle checks

def _walk(value, path='$'):
    yield path, value
    if isinstance(value, dict):
        for key, child in value.items():
            yield from _walk(child, f'{path}.{key}')
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from _walk(child, f'{path}[{index}]')


def defined_ids(operations):
    """IDs created by add operations (slots, components and all of their members)."""
    ids = {}
    for index, operation in enumerate(operations):
        if operation.get('$type') not in DEFINING_OPS:
            continue
        for path, node in _walk(operation.get('data'), f'$.operations[{index}].data'):
            if isinstance(node, dict) and isinstance(node.get('id'), str):
                if node['id'] in ids:
                    raise ValueError(f'Duplicate ID {node["id"]} at {path} and {ids[node["id"]]}')
                ids[node['id']] = path
    return ids


def _referenced_ids(operations):
    for index, operation in enumerate(operations):
        base = f'$.operations[{index}]'
        kind = operation.get('$type')
        if kind == 'addComponent':
            yield f'{base}.containerSlotId', operation.get('containerSlotId')
        elif kind in ('updateSlot', 'updateComponent'):
            yield f'{base}.data.id', (operation.get('data') or {}).get('id')
        elif kind in ('removeSlot', 'removeComponent'):
            yield base, operation.get('slotId') or operation.get('componentId')
        for path, node in _walk(operation.get('data'), f'{base}.data'):
            if isinstance(node, dict) and node.get('$type') == 'reference' and node.get('targetId'):
                yield path, node['targetId']


def validate_bundle(bundle):
    """Reject bundles Resonite could not resolve; returns the set of defined IDs."""
    if bundle.get('schemaVersion') != SCHEMA_VERSION:
        raise ValueError(f'Unsupported schemaVersion {bundle.get("schemaVersion")!r}')
    batch = bundle.get('batch') or {}
    if batch.get('$type') != 'dataModelOperationBatch' or not isinstance(batch.get('operations'), list):
        raise ValueError('batch must be a dataModelOperationBatch with operations')
    operations = batch['operations']
    ids = defined_ids(operations)
    known = set(ids) | {'Root'} | set(bundle.get('externalIds', []))
    for path, target in _referenced_ids(operations):
        if target not in known:
            raise ValueError(f'Unresolved reference {target!r} at {path}')
    root = bundle.get('rootId')
    roots = [op for op in operations if op.get('$type') == 'addSlot' and op['data'].get('id') == root]
    if not roots:
        raise ValueError(f'rootId {root!r} is not an added slot')
    for case in bundle.get('cases', []):
        for item in case.get('updates', []) + case.get('checks', []):
            if item.get('id') not in ids:
                raise ValueError(f'Case {case.get("name")!r} targets unknown ID {item.get("id")!r}')
            if item.get('kind') not in ('component', 'slot'):
                raise ValueError(f'Case {case.get("name")!r} has unsupported kind {item.get("kind")!r}')
    return set(ids)


def namespace_bundle(bundle, prefix):
    """Prefix every bundle-defined ID so a bundle can run repeatedly in one world."""
    if prefix.startswith('Reso_'):
        raise ValueError('Client IDs must not use the Resonite-reserved Reso_ prefix')
    mapping = {old: prefix + old for old in defined_ids(bundle['batch']['operations'])}
    result = copy.deepcopy(bundle)
    keys = {'id', 'targetId', 'containerSlotId', 'slotId', 'componentId', 'rootId', 'expectedReference'}

    def rewrite(node):
        if isinstance(node, dict):
            for key, value in node.items():
                if key in keys and isinstance(value, str) and value in mapping:
                    node[key] = mapping[value]
                else:
                    rewrite(value)
        elif isinstance(node, list):
            for child in node:
                rewrite(child)

    rewrite(result)
    for index, operation in enumerate(result['batch']['operations']):
        operation['messageId'] = f'{prefix}op{index}'
    return result, mapping


# ---------------------------------------------------------------- value comparison

def member_value(data, member, kind='component'):
    """Read a dotted member path (e.g. Points.0.Value or position.x) from slot/component data."""
    node = data.get('members', {}) if kind == 'component' else data
    for part in member.split('.'):
        if isinstance(node, dict) and 'elements' in node:
            node = node['elements']
        elif isinstance(node, dict) and isinstance(node.get('members'), dict) and part not in node:
            node = node['members']
        if isinstance(node, list):
            node = node[int(part)]
        elif isinstance(node, dict) and part in node:
            node = node[part]
        elif isinstance(node, dict) and isinstance(node.get('value'), dict) and part in node['value']:
            node = node['value'][part]
        else:
            raise KeyError(f'{member!r}: missing {part!r}')
    return node


def compare(actual, check):
    """Return None when the wire value matches the check, otherwise a mismatch description."""
    if 'expectedReference' in check:
        target = actual.get('targetId') if isinstance(actual, dict) else None
        return None if target == check['expectedReference'] else f'reference {target!r}'
    value = actual.get('value') if isinstance(actual, dict) and 'value' in actual else actual
    expected = check['expected']
    tolerance = check.get('absoluteTolerance', 1e-5)

    def close(a, b, path):
        if isinstance(b, dict):
            if not isinstance(a, dict):
                return f'{path}: {a!r} is not a vector'
            return next((r for k in b if (r := close(a.get(k), b[k], f'{path}.{k}'))), None)
        if isinstance(b, list):
            if not isinstance(a, list) or len(a) != len(b):
                return f'{path}: {a!r} has the wrong length'
            return next((r for i, (x, y) in enumerate(zip(a, b)) if (r := close(x, y, f'{path}[{i}]'))), None)
        if isinstance(b, bool) or isinstance(b, str) or b is None:
            return None if a == b else f'{path}: {a!r}'
        if not isinstance(a, (int, float)) or isinstance(a, bool) or not math.isfinite(a):
            return f'{path}: {a!r} is not a finite number'
        return None if abs(a - b) <= tolerance else f'{path}: {a!r} (|diff| {abs(a - b):.3g} > {tolerance})'

    return close(value, expected, 'value')


# ---------------------------------------------------------------- ResoniteLink client

class LinkClient:
    """Minimal ResoniteLink client that records the full transcript."""

    def __init__(self, port, host='localhost', transcript=None):
        self.uri = f'ws://{host}:{port}'
        self.transcript = transcript
        self.unsolicited = []
        self._pending = {}
        self._socket = None
        self._reader = None

    async def __aenter__(self):
        import websockets
        self._socket = await websockets.connect(self.uri, max_size=None, ping_interval=None)
        self._reader = asyncio.create_task(self._read())
        return self

    async def __aexit__(self, *_):
        self._reader.cancel()
        await self._socket.close()

    def _record(self, direction, message):
        if self.transcript:
            self.transcript.write(json.dumps({'t': time.time(), 'dir': direction, 'msg': message}) + '\n')

    async def _read(self):
        async for raw in self._socket:
            message = json.loads(raw)
            self._record('in', message)
            future = self._pending.pop(message.get('sourceMessageId'), None)
            if future and not future.done():
                future.set_result(message)
            else:
                self.unsolicited.append(message)
        for future in self._pending.values():
            if not future.done():
                future.set_exception(HarnessError('ResoniteLink connection closed'))

    async def request(self, payload, timeout=30, check=True):
        payload.setdefault('messageId', 'harness_' + uuid.uuid4().hex)
        future = asyncio.get_running_loop().create_future()
        self._pending[payload['messageId']] = future
        self._record('out', payload)
        await self._socket.send(json.dumps(payload))
        try:
            response = await asyncio.wait_for(future, timeout)
        finally:
            self._pending.pop(payload['messageId'], None)
        if check and response.get('success') is False:
            raise HarnessError(f'{payload["$type"]} failed: {response.get("errorInfo")}')
        return response


async def wait_for_link(port, timeout, process=None):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        if process and process.poll() is not None:
            raise HarnessError(f'Headless exited with code {process.returncode} before ResoniteLink was ready')
        try:
            async with LinkClient(port) as client:
                return await client.request({'$type': 'requestSessionData'}, timeout=10)
        except Exception as error:  # noqa: BLE001 - keep polling until the deadline
            last = error
            await asyncio.sleep(2)
    raise HarnessError(f'ResoniteLink on port {port} not ready after {timeout}s: {last}')


# ---------------------------------------------------------------- headless lifecycle

def free_port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


class HeadlessServer:
    """Starts an isolated, private, logged-out headless world with ResoniteLink enabled."""

    def __init__(self, install=DEFAULT_INSTALL, home=DEFAULT_HOME, port=None, tick_rate=60.0, startup_timeout=600,
                 assemblies=()):
        self.install, self.home = Path(install), Path(home)
        self.port = port or free_port()
        self.tick_rate, self.startup_timeout = tick_rate, startup_timeout
        self.assemblies = [Path(a).resolve() for a in assemblies]
        stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        self.run_dir = self.home / 'runs' / f'{stamp}-{self.port}'
        self.process = None
        self.console = None
        self.session = None

    def config(self):
        return {
            'comment': 'Generated by Resonite.UnitySDK tests/runtime/harness.py; isolated converter tests',
            'tickRate': self.tick_rate,
            'dataFolder': str(self.home / 'data'),
            'cacheFolder': str(self.home / 'cache'),
            'logsFolder': str(self.run_dir / 'logs'),
            'startWorlds': [{
                'isEnabled': True, 'sessionName': 'Converter Harness', 'accessLevel': 'Private',
                'hideFromPublicListing': True, 'maxUsers': 1, 'loadWorldPresetName': 'Grid',
                'autoSleep': False, 'saveOnExit': False, 'awayKickMinutes': -1,
                'enableResoniteLink': True, 'forceResoniteLinkPort': self.port}]}

    async def start(self):
        executable = self.install / 'Resonite.exe'
        if not executable.exists():
            raise HarnessError(f'Headless executable not found: {executable}')
        self.run_dir.mkdir(parents=True)
        config = self.run_dir / 'HeadlessConfig.json'
        config.write_text(json.dumps(self.config(), indent=2), encoding='utf-8')
        self.console = open(self.run_dir / 'console.log', 'w', encoding='utf-8', errors='replace')
        flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0
        command = [str(executable), '-HeadlessConfig', str(config)]
        for assembly in self.assemblies:
            command += ['-LoadAssembly', str(assembly)]
        self.process = subprocess.Popen(command, cwd=self.install,
                                        stdin=subprocess.PIPE, stdout=self.console, stderr=subprocess.STDOUT,
                                        text=True, creationflags=flags)
        self.session = await wait_for_link(self.port, self.startup_timeout, self.process)
        return self

    def stop(self, timeout=60):
        if not self.process or self.process.poll() is not None:
            return self.process.returncode if self.process else None
        try:
            self.process.stdin.write('shutdown\n')
            self.process.stdin.flush()
            return self.process.wait(timeout)
        except (OSError, subprocess.TimeoutExpired):
            self.process.kill()
            return self.process.wait(30)
        finally:
            if self.console:
                self.console.close()

    async def __aenter__(self):
        try:
            return await self.start()
        except BaseException:
            self.stop()
            raise

    async def __aexit__(self, *_):
        self.stop()


# ---------------------------------------------------------------- bundle execution

async def read_member(client, check):
    if check['kind'] == 'component':
        data = (await client.request({'$type': 'getComponent', 'componentId': check['id']}))['data']
    else:
        data = (await client.request({'$type': 'getSlot', 'slotId': check['id'], 'depth': 0,
                                      'includeComponentData': False}))['data']
    return member_value(data, check['member'], check['kind'])


async def apply_update(client, update):
    if update['kind'] == 'component':
        payload = {'$type': 'updateComponent', 'data': {'id': update['id'], 'members': update['members']}}
    else:
        payload = {'$type': 'updateSlot', 'data': {'id': update['id'], **update['members']}}
    await client.request(payload)


async def run_case(client, case):
    for update in case.get('updates', []):
        await apply_update(client, update)
    await asyncio.sleep(case.get('minSettleSeconds', 0.1))
    deadline = time.monotonic() + case.get('timeoutSeconds', 5)
    while True:
        results = []
        for check in case.get('checks', []):
            try:
                actual = await read_member(client, check)
                mismatch = compare(actual, check)
            except (HarnessError, KeyError, IndexError, ValueError) as error:
                actual, mismatch = None, f'{type(error).__name__}: {error}'
            results.append({'check': check, 'actual': actual, 'error': mismatch})
        if all(r['error'] is None for r in results) or time.monotonic() >= deadline:
            return {'name': case.get('name'), 'passed': all(r['error'] is None for r in results), 'checks': results}
        await asyncio.sleep(0.1)


async def run_bundle(client, bundle, keep=False):
    validate_bundle(bundle)
    prefix = f'Harness_{uuid.uuid4().hex[:8]}_'
    bundle, _ = namespace_bundle(bundle, prefix)
    unsolicited_before = len(client.unsolicited)
    report = {'name': bundle.get('name'), 'idPrefix': prefix, 'cases': []}
    batch = dict(bundle['batch'], messageId=prefix + 'batch')
    try:
        response = await client.request(batch, timeout=120, check=False)
        failures = [r for r in response.get('responses', []) if not r.get('success')]
        report['batch'] = {'success': bool(response.get('success')) and not failures,
                           'errorInfo': response.get('errorInfo'), 'failures': failures,
                           'operations': len(batch['operations'])}
        if report['batch']['success']:
            for case in bundle.get('cases', []):
                report['cases'].append(await run_case(client, case))
    finally:
        if not keep:
            await client.request({'$type': 'removeSlot', 'slotId': bundle['rootId']}, check=False)
    report['unsolicited'] = client.unsolicited[unsolicited_before:]
    report['passed'] = (report['batch']['success'] and not report['unsolicited']
                        and all(c['passed'] for c in report['cases']))
    return report


def selftest_bundle():
    """Native-only bundle: proves the world ticks and drives settle, independent of Unity."""
    def field(kind, value):
        return {'$type': kind, 'value': value, 'id': None}

    def component(cid, ctype, members):
        for name, member in members.items():
            member['id'] = f'{cid}_{name}'
        return {'$type': 'addComponent', 'containerSlotId': 'self_root',
                'data': {'id': cid, 'componentType': ctype, 'members': members}}

    value = '[FrooxEngine]FrooxEngine.ValueField<float>'
    return {
        'schemaVersion': SCHEMA_VERSION, 'name': 'harness-selftest', 'rootId': 'self_root',
        'batch': {'$type': 'dataModelOperationBatch', 'operations': [
            {'$type': 'addSlot', 'data': {'id': 'self_root', 'name': field('string', 'ConverterHarness_selftest'),
                                          'parent': {'$type': 'reference', 'targetId': 'Root', 'id': 'self_parent'}}},
            component('self_source', value, {'Value': field('float', 0.25)}),
            component('self_sink', value, {'Value': field('float', 0.0)}),
            component('self_copy', '[FrooxEngine]FrooxEngine.ValueCopy<float>', {
                'Source': {'$type': 'reference', 'targetId': 'self_source_Value'},
                'Target': {'$type': 'reference', 'targetId': 'self_sink_Value'},
                'WriteBack': field('bool', False)})]},
        'cases': [
            {'name': 'copy settles initial value', 'checks': [
                {'kind': 'component', 'id': 'self_sink', 'member': 'Value', 'expected': 0.25}]},
            {'name': 'copy follows source update',
             'updates': [{'kind': 'component', 'id': 'self_source',
                          'members': {'Value': {'$type': 'float', 'value': 0.75}}}],
             'checks': [{'kind': 'component', 'id': 'self_sink', 'member': 'Value', 'expected': 0.75}]}]}


def load_bundles(paths):
    bundles = []
    for path in paths:
        path = Path(path)
        for file in sorted(path.glob('*.json')) if path.is_dir() else [path]:
            bundles.append((file, json.loads(file.read_text(encoding='utf-8'))))
    return bundles


# ---------------------------------------------------------------- package export (needs the headless plugin)

def build_plugin(home=DEFAULT_HOME):
    """Build the test-only headless plugin outside the repo and return its DLL path."""
    output = Path(home) / 'plugin'
    result = subprocess.run(['dotnet', 'build', str(PLUGIN_PROJECT), '-c', 'Release', '-nologo', '-v:q',
                             '-o', str(output), f'-p:BaseIntermediateOutputPath={Path(home) / "plugin-obj"}/',
                             f'-p:ResoniteHeadless={DEFAULT_INSTALL.as_posix()}'],
                            capture_output=True, text=True)
    if result.returncode:
        raise HarnessError(f'Plugin build failed:\n{result.stdout[-3000:]}{result.stderr[-2000:]}')
    return output / 'ResoniteUnitySDK.HeadlessBridge.dll'


def method_reference(target):
    # Sync-method arguments use Data_Reference, whose JSON key is targetID (members use targetId).
    return {'$type': 'reference', 'targetID': target}


async def count_hierarchy(client, slot_id):
    data = (await client.request({'$type': 'getSlot', 'slotId': slot_id, 'depth': -1,
                                  'includeComponentData': False}, timeout=120))['data']
    slots = components = 0
    types = {}
    stack = [data]
    while stack:
        slot = stack.pop()
        slots += 1
        for component in slot.get('components') or []:
            components += 1
            name = component.get('componentType') or '?'
            types[name] = types.get(name, 0) + 1
        stack.extend(slot.get('children') or [])
    return {'name': (data.get('name') or {}).get('value'), 'slots': slots, 'components': components, 'types': types}


def inspect_package(path):
    import zipfile
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        names = archive.namelist()
    kinds = {}
    for name in names:
        top = name.split('/')[0] if '/' in name else '(root)'
        kinds[top] = kinds.get(top, 0) + 1
    return {'bytes': Path(path).stat().st_size, 'entries': len(names), 'corruptEntry': bad, 'byFolder': kinds}


async def export_package(args):
    folder = Path(args.out).resolve()
    target = folder / (Path(args.name).stem + '.resonitepackage')
    if target.exists() and not args.overwrite:
        raise HarnessError(f'{target} already exists (pass --overwrite)')
    report = {'slot': args.slot, 'package': str(target)}
    async with LinkClient(args.port) as client:
        report['source'] = await count_hierarchy(client, args.slot)
        started = time.monotonic()
        await client.request({'$type': 'callStaticSyncMethod', 'targetType': PLUGIN_TYPE, 'methodName': 'ExportPackage',
                              'arguments': {'root': method_reference(args.slot),
                                            'folder': {'$type': 'string', 'value': str(folder)},
                                            'name': {'$type': 'string', 'value': target.name},
                                            'includeVariants': {'$type': 'bool', 'value': args.variants}}},
                             timeout=args.timeout)
        report['exportSeconds'] = round(time.monotonic() - started, 1)
        report['archive'] = inspect_package(target)
        if args.roundtrip:
            holder = 'Harness_import_' + uuid.uuid4().hex[:8]
            await client.request({'$type': 'addSlot', 'data': {'id': holder, 'name': {'$type': 'string', 'value': holder},
                                                              'parent': {'$type': 'reference', 'targetId': 'Root'}}})
            try:
                await client.request({'$type': 'callStaticSyncMethod', 'targetType': PLUGIN_TYPE,
                                      'methodName': 'ImportPackage',
                                      'arguments': {'file': {'$type': 'string', 'value': str(target)},
                                                    'parent': method_reference(holder)}},
                                     timeout=args.timeout)
                imported = await client.request({'$type': 'getSlot', 'slotId': holder, 'depth': 1,
                                                 'includeComponentData': False})
                children = imported['data'].get('children') or []
                report['reimported'] = [await count_hierarchy(client, child['id']) for child in children]
            finally:
                await client.request({'$type': 'removeSlot', 'slotId': holder}, check=False)
        report['unsolicited'] = client.unsolicited
    print(json.dumps(report, indent=1), flush=True)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=2), encoding='utf-8')
    return 0 if not report['unsolicited'] and report['archive']['corruptEntry'] is None else 1


async def run(args):
    bundles = [(Path('<selftest>'), selftest_bundle())] if args.selftest else []
    bundles += load_bundles(args.bundles)
    if not bundles:
        raise HarnessError('No bundles given (pass paths or --selftest)')
    for file, bundle in bundles:  # static gate before paying for a headless start
        try:
            validate_bundle(bundle)
        except ValueError as error:
            raise HarnessError(f'{file}: {error}') from None
    server = None if args.port else HeadlessServer(port=args.listen_port, tick_rate=args.tick_rate)
    try:
        if server:
            print(f'Starting headless (ResoniteLink port {server.port}); run dir {server.run_dir}', flush=True)
            await server.start()
            print(f'Ready: {server.session.get("resoniteVersion")}', flush=True)
        port = args.port or server.port
        run_dir = server.run_dir if server else DEFAULT_HOME / 'attached' / datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        run_dir.mkdir(parents=True, exist_ok=True)
        with open(run_dir / 'transcript.jsonl', 'w', encoding='utf-8') as transcript:
            async with LinkClient(port, transcript=transcript) as client:
                session = await client.request({'$type': 'requestSessionData'})
                reports = []
                for file, bundle in bundles:
                    report = await run_bundle(client, bundle, keep=args.keep)
                    report['file'] = str(file)
                    reports.append(report)
                    print(f'{"PASS" if report["passed"] else "FAIL"} {report["name"]} ({file})', flush=True)
                    for case in report['cases']:
                        for result in case['checks']:
                            if result['error']:
                                c = result['check']
                                print(f'    {case["name"]}: {c["id"]}.{c["member"]} -> {result["error"]}', flush=True)
                    if not report['batch']['success']:
                        print(f'    batch: {report["batch"]["errorInfo"]} {report["batch"]["failures"][:3]}', flush=True)
                    for message in report['unsolicited'][:3]:
                        print(f'    unsolicited: {json.dumps(message)[:300]}', flush=True)
        summary = {'passed': all(r['passed'] for r in reports), 'port': port, 'runDir': str(run_dir),
                   'resoniteVersion': session.get('resoniteVersion'),
                   'resoniteLinkVersion': session.get('resoniteLinkVersion'), 'reports': reports}
        (run_dir / 'report.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
        if args.report:
            Path(args.report).write_text(json.dumps(summary, indent=2), encoding='utf-8')
        print(f'{"PASSED" if summary["passed"] else "FAILED"}: {sum(r["passed"] for r in reports)}/{len(reports)} '
              f'bundles; report {run_dir / "report.json"}', flush=True)
        return 0 if summary['passed'] else 1
    finally:
        if server:
            print(f'Headless exit code: {server.stop()}', flush=True)


async def serve(args):
    assemblies = [build_plugin()] if args.package_plugin else []
    server = HeadlessServer(port=args.listen_port, tick_rate=args.tick_rate, assemblies=assemblies)
    print(f'Starting headless (ResoniteLink port {server.port}); run dir {server.run_dir}', flush=True)
    async with server:
        state = {'port': server.port, 'runDir': str(server.run_dir), 'pid': server.process.pid,
                 'plugins': [str(a) for a in assemblies], 'resoniteVersion': server.session.get('resoniteVersion')}
        (DEFAULT_HOME / 'serve.json').write_text(json.dumps(state), encoding='utf-8')
        print(json.dumps(state), flush=True)
        print('Headless ready; press Enter (or close stdin) to shut it down.', flush=True)
        try:
            await asyncio.get_running_loop().run_in_executor(None, sys.stdin.readline)
        finally:
            (DEFAULT_HOME / 'serve.json').unlink(missing_ok=True)
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest='command', required=True)
    check = commands.add_parser('validate', help='static reference-closure check only (no Resonite)')
    check.add_argument('bundles', nargs='+')
    runner = commands.add_parser('run', help='run bundles against a fresh (or --port attached) headless world')
    runner.add_argument('bundles', nargs='*')
    runner.add_argument('--selftest', action='store_true', help='also run the native-only harness self test')
    runner.add_argument('--port', type=int, help='attach to an already running ResoniteLink port')
    runner.add_argument('--keep', action='store_true', help='leave test slots in the world for inspection')
    runner.add_argument('--report', help='also write the JSON report here')
    server = commands.add_parser('serve', help='start a headless world for interactive/attached use')
    server.add_argument('--package-plugin', action='store_true',
                        help='build and load the test plugin that exposes package export/import')
    package = commands.add_parser('export-package', help='export a slot as .resonitepackage (serve --package-plugin)')
    package.add_argument('--port', type=int, required=True)
    package.add_argument('--slot', required=True, help='slot ID to export (e.g. the converted avatar root)')
    package.add_argument('--out', required=True, help='destination folder')
    package.add_argument('--name', required=True)
    package.add_argument('--variants', action='store_true', help='include asset variants')
    package.add_argument('--roundtrip', action='store_true', help='re-import the package and compare hierarchy counts')
    package.add_argument('--overwrite', action='store_true')
    package.add_argument('--timeout', type=float, default=1800)
    package.add_argument('--report')
    for command in (runner, server):
        command.add_argument('--listen-port', type=int, help='ResoniteLink port for a started headless')
        command.add_argument('--tick-rate', type=float, default=60.0)
    args = parser.parse_args(argv)
    try:
        if args.command == 'validate':
            for file, bundle in load_bundles(args.bundles):
                ids = validate_bundle(bundle)
                print(f'OK {file}: {len(bundle["batch"]["operations"])} operations, {len(ids)} IDs')
            return 0
        command = {'run': run, 'serve': serve, 'export-package': export_package}[args.command]
        return asyncio.run(command(args))
    except (HarnessError, ValueError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
