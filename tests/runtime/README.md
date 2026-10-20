# Converter output harness (headless Resonite)

Numeric runtime tests for converter output. Unity converts small fixtures with the real converters into
**bundles**: the exact ResoniteLink operation batch plus cases (inputs to write, values to read back).
`harness.py` replays them in an isolated headless world and compares the engine's values.

## Pieces

| File | Role |
| --- | --- |
| `ConverterOutputBundles.cs` | Fixture registry and `BundleBuilder` (Unity editor). Uses `SceneConverter`'s own conversion and member filtering, without sending. |
| `IsolatedPreviewScene.cs` | Keeps fixtures out of the open scene. |
| `export_bundles_in_editor.csx` | Unity MCP `execute_code` snippet: compiles the two files with Roslyn and writes bundles to `%LOCALAPPDATA%/ResoniteConverterHarness/bundles/latest` (needs the Unity process to have `RESONITE_SDK_REPO` set to this checkout). |
| `harness.py` | Static checks, headless lifecycle, ResoniteLink client, case runner and reports. |
| `test_harness.py` | Offline unit tests for the harness. |

## Run

```sh
python -m unittest tests/runtime/test_harness.py           # offline, no Resonite
uv run tests/runtime/harness.py validate <bundle-or-dir>  # reference closure only
uv run tests/runtime/harness.py run --selftest <bundle-or-dir>
```

`run` starts `Headless/Resonite.exe` with a generated config: private, unlisted, logged out, Grid preset,
ResoniteLink on a free local port, separate data/cache folders under `%LOCALAPPDATA%/ResoniteConverterHarness`
(`RESONITE_HARNESS_HOME`, `RESONITE_HEADLESS_DIR` override). It shuts the headless down afterwards.
`--port N` attaches to an existing ResoniteLink session instead; `serve` keeps a headless running.
Each run writes `HeadlessConfig.json`, `console.log`, engine logs, `transcript.jsonl` and `report.json` to
`runs/<timestamp>-<port>/`. Exit codes: 0 pass, 1 test failure, 2 setup/static error.

Batch mode export (Unity closed): stage both `.cs` files under an `Editor/` folder and run
`-executeMethod ConverterOutputBundles.ExportFromCommandLine -converterBundleDir <dir>` (not yet wrapped).

## What a pass means

- Every reference in the filtered payload resolves (statically, before Resonite starts).
- Resonite applied the batch with no failed sub-operation and no deferred/unsolicited error. Resonite reports
  unresolved references **after** a successful batch response, with the batch's message ID; the harness treats
  any such message as a failure.
- Each case's values match within tolerance; checks are polled until they pass or `timeoutSeconds` (default 5).
  Polling means a check proves a value is reached, not that it never changes.
- Bundle IDs are prefixed per run, so bundles can repeat in one world; test slots are removed afterwards
  unless `--keep`.

It does not test visuals, physical interaction, avatar equipping or assets: fixtures that need asset uploads
or conversion post-processors are rejected until a live-link capture mode exists.

## Bundle format (schemaVersion 1)

```json
{"schemaVersion": 1, "name": "...", "rootId": "<slot id>", "externalIds": [],
 "batch": {"$type": "dataModelOperationBatch", "operations": []},
 "cases": [{"name": "...", "timeoutSeconds": 5,
            "updates": [{"kind": "component", "id": "<component id>", "members": {"Value": {"$type": "float", "value": 0.75}}}],
            "checks":  [{"kind": "component", "id": "<component id>", "member": "Value", "expected": 0.75, "absoluteTolerance": 1e-4}]}]}
```

`kind` is `component` or `slot` (slot members: `position.x`, `scale`, `isActive`...). Member paths are dotted and
index lists (`Points.0.Value`). `expected` may be a number, bool, string, or vector object; use
`expectedReference` to compare a reference target ID.

## Adding a fixture

Add an entry to `ConverterOutputBundles.Fixtures`: build source objects under `bundle.Root`, call
`bundle.Convert()`, then declare cases with `AddCase(...).Set(binding, member, value).Check(binding, member, expected)`.
Derive expected values from source data or decompiled native behavior, and document the formula beside the case.

## Package export

The Resonite SDK window's **Headless Package Export** section (`Assets/ResoniteSDK/Editor/HeadlessExport/`) converts a copy of the chosen object in a temporary scene, sends it to a private headless world and writes a `.resonitepackage` with Resonite's own exporter. It loads the bridge plugin in `HeadlessPlugin/` (sources stored as `.txt` so Unity neither compiles nor drops them; built with `dotnet` against the headless install, cached under `Library/ResoniteHeadless`).

From the command line, `harness.py serve --package-plugin` and `harness.py export-package` use the same plugin.

