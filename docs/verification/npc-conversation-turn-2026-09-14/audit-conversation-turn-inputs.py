"""Audit an isolated Unity input tree against a Git baseline and three overlays.

This script reads source trees and existing Bee response files. It never copies
project inputs, starts Unity, invokes a model, or claims that tests passed.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import difflib
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
BASELINE = '68832af00675ecf3388085bee379d46b3fa5947d'
SCOPES = ('Assets', 'Packages', 'ProjectSettings', 'ArtSource')
OVERLAYS = (
    'Assets/CozyTown/Runtime/NpcAgents/NpcDecisionScheduler.cs',
    'Assets/CozyTown/Tests/EditMode/NpcAgents/NpcConversationBudgetTests.cs',
    'Assets/CozyTown/Tests/PlayMode/NpcConversationBudgetPlayModeTests.cs',
)
ASSEMBLIES = (
    'CozyTown.Runtime', 'CozyTown.Unity', 'CozyTown.Unity.Editor',
    'CozyTown.Tests.EditMode', 'CozyTown.Tests.UnityEditMode',
    'CozyTown.Tests.PlayMode',
)
SETTINGS = 'ProjectSettings/ProjectSettings.asset'
# Exact fixture bytes already recorded by conversation-budget-input-audit.json.
# No other PlayerSettings change receives an automatic exemption.
SETTINGS_BASE_SHA = '5fa52c56948ae751f378f826390e91bc94fa36c574731d1fe8df3b6f877a1cd9'
SETTINGS_ISOLATED_SHA = 'a0cc453d18756d18cce9dcab3677f18702669d5ed974bee7f0fea5e5c890db02'
SCENE_TEMPLATE_SETTINGS = 'ProjectSettings/SceneTemplateSettings.json'
SCENE_TEMPLATE_SHA = 'bb9098b3bfcde78d93e264b96f7b77b5430e64e8c57a7aad7f5a5d9c3945e16a'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def read_optional(path):
    return path.read_bytes() if path.is_file() else None


def classify(actual, expected):
    if actual is None:
        return 'missing'
    if expected.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
        fields = dict(line.split(' ', 1) for line in expected.decode().strip().splitlines())
        if sha(actual) == fields['oid'].split(':')[1] and len(actual) == int(fields['size']):
            return 'lfs-payload-verified'
        return 'different'
    if actual == expected:
        return 'byte-identical'
    try:
        if actual.decode('utf-8').replace('\r\n', '\n') == expected.decode('utf-8').replace('\r\n', '\n'):
            return 'line-endings-only'
    except UnicodeDecodeError:
        pass
    return 'different'


def baseline_files(commit):
    entries = []
    for entry in git('ls-tree', '-rz', commit, '--', *SCOPES).split(b'\0'):
        if entry:
            metadata, name = entry.split(b'\t', 1)
            mode, kind, oid = metadata.split()
            if kind != b'blob' or mode not in (b'100644', b'100755'):
                raise ValueError('Unsupported baseline input: ' + name.decode())
            entries.append((oid.decode(), name.decode()))
    process = subprocess.Popen(['git', 'cat-file', '--batch'], cwd=ROOT,
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    packed, _ = process.communicate(('\n'.join(oid for oid, _ in entries) + '\n').encode())
    if process.returncode:
        raise RuntimeError('git cat-file failed')
    offset, files = 0, {}
    for oid, name in entries:
        end = packed.index(b'\n', offset)
        returned_oid, kind, length = packed[offset:end].decode().split()
        if returned_oid != oid or kind != 'blob':
            raise ValueError('Unexpected git cat-file response')
        size = int(length)
        files[name] = (oid, packed[end + 1:end + 1 + size])
        offset = end + 1 + size + 1
    return files


def settings_change_keys(before, after):
    """Describe serialization changes without printing potentially secret values."""
    keys = set()
    for line in difflib.unified_diff(before.decode().splitlines(), after.decode().splitlines()):
        if line[:1] in ('+', '-') and not line.startswith(('+++', '---')):
            match = re.match(r'[+-]\s*([A-Za-z_][A-Za-z_0-9 ]*):', line)
            if match:
                keys.add(match.group(1))
    return sorted(keys)


def redact_settings(text):
    return re.sub(
        r'^([ \t]*[^:\r\n]*(?:passcode|password|secret|token|keystore|keyalias)[^:\r\n]*:[ \t]*)(\S[^\r\n]*)$',
        r'\1<REDACTED>', text, flags=re.IGNORECASE | re.MULTILINE)


def write_frozen_settings_evidence(project, baseline, report):
    before = git('show', baseline + ':' + SETTINGS)
    actual = (project / SETTINGS).read_bytes()
    template = (project / SCENE_TEMPLATE_SETTINGS).read_bytes()
    if sha(actual) != report['settingsException']['isolatedSha256'] or sha(template) != report['sceneTemplateSettingsException']['isolatedSha256']:
        raise RuntimeError('Isolated settings changed before evidence could be captured.')
    settings_diff = ''.join(difflib.unified_diff(
        redact_settings(before.decode().replace('\r\n', '\n')).splitlines(keepends=True),
        redact_settings(actual.decode().replace('\r\n', '\n')).splitlines(keepends=True),
        fromfile=baseline + '/' + SETTINGS, tofile='isolated/' + SETTINGS))
    artifacts = (
        ('conversation-turn-isolated-settings.diff', settings_diff.encode('utf-8'), True),
        ('conversation-turn-SceneTemplateSettings.json', template, False),
    )
    report['settingsEvidenceArtifacts'] = []
    for name, contents, redacted in artifacts:
        path = ROOT / 'Logs' / name
        path.write_bytes(contents)
        report['settingsEvidenceArtifacts'].append({
            'path': path.relative_to(ROOT).as_posix(), 'sha256': sha(contents),
            'bytes': len(contents), 'sensitiveSettingValuesRedacted': redacted,
        })


def category(name):
    if name.endswith('.cs'):
        return 'csharp'
    if name.endswith(('.asmdef', '.asmref')):
        return 'assembly-definition'
    if name.endswith('.unity'):
        return 'scene'
    return name.split('/', 1)[0]


def audit(project, baseline, phase, source_commit, allow_scene_template):
    issues, rows, manifests = [], [], []
    files = baseline_files(baseline)
    initial_overlay = {name: read_optional(ROOT / name) for name in OVERLAYS}
    if source_commit:
        changed = {name.decode() for name in git('diff', '--name-only', '-z', baseline,
                   source_commit, '--', *SCOPES).split(b'\0') if name}
        if changed != set(OVERLAYS):
            issues.append('source-commit-input-diff-is-not-exactly-three-overlays: ' + ', '.join(sorted(changed)))
    for name in OVERLAYS:
        if name not in files:
            issues.append('overlay-not-in-baseline: ' + name)
        if initial_overlay[name] is None:
            issues.append('overlay-missing-from-worktree: ' + name)
    for name, (oid, before) in sorted(files.items()):
        actual = read_optional(project / name)
        workspace = read_optional(ROOT / name)
        expected = initial_overlay[name] if name in OVERLAYS else before
        baseline_match = classify(actual, before)
        match = classify(actual, expected) if expected is not None else 'missing-expected-overlay'
        if name == SETTINGS and match == 'different' and actual is not None:
            if sha(before) == SETTINGS_BASE_SHA and sha(actual) == SETTINGS_ISOLATED_SHA:
                match = 'pinned-unity-settings-serialization'
        if match in ('different', 'missing', 'missing-expected-overlay'):
            issues.append('isolated-input-mismatch: ' + name)
        row = {
            'path': name, 'category': category(name), 'gitBlob': oid,
            'expectedSource': 'target-worktree-overlay' if name in OVERLAYS else 'baseline',
            'baselineBlobSha256': sha(before),
            'isolatedSha256': sha(actual) if actual is not None else None,
            'worktreeSha256': sha(workspace) if workspace is not None else None,
            'isolatedBytes': len(actual) if actual is not None else None,
            'matchExpected': match, 'matchBaseline': baseline_match,
            'worktreeVsBaseline': classify(workspace, before),
        }
        if name == SETTINGS and actual is not None and baseline_match == 'different':
            row['changedSettingKeys'] = settings_change_keys(before, actual)
        if name in OVERLAYS and source_commit:
            committed = git('show', source_commit + ':' + name)
            row['sourceCommitBlobSha256'] = sha(committed)
            row['sourceCommitMatch'] = classify(actual, committed)
            if row['sourceCommitMatch'] in ('different', 'missing'):
                issues.append('overlay-does-not-match-source-commit: ' + name)
        rows.append(row)

    isolated_names = {
        path.relative_to(project).as_posix()
        for scope in SCOPES for path in (project / scope).rglob('*') if path.is_file()
    }
    extras = sorted(isolated_names - set(files))
    template_bytes = read_optional(project / SCENE_TEMPLATE_SETTINGS)
    template_references = []
    for name in sorted(isolated_names):
        if name.startswith('Assets/') and name.endswith('.cs'):
            for number, line in enumerate((project / name).read_text(encoding='utf-8-sig').splitlines(), 1):
                if re.search(r'\b(?:SceneTemplateSettings|SceneTemplateService)\b', line):
                    template_references.append({'path': name, 'line': number})
    template_allowed = (allow_scene_template and template_bytes is not None
                        and sha(template_bytes) == SCENE_TEMPLATE_SHA and not template_references)
    approved_extras = [SCENE_TEMPLATE_SETTINGS] if template_allowed and SCENE_TEMPLATE_SETTINGS in extras else []
    issues.extend('extra-isolated-input: ' + name for name in extras if name not in approved_extras)
    worktree_untracked = [name.decode() for name in
                         git('ls-files', '--others', '--exclude-standard', '-z', '--', *SCOPES).split(b'\0') if name]
    untracked_rows = [{'path': name, 'presentInIsolated': name in isolated_names}
                      for name in sorted(worktree_untracked)]

    compiled_sources = set()
    for assembly in ASSEMBLIES:
        candidates = list((project / 'Library/Bee/artifacts').glob('*.dag/' + assembly + '.rsp'))
        if len(candidates) != 1:
            issues.append(f'expected-one-bee-manifest: {assembly}: {len(candidates)}')
            continue
        rsp = candidates[0]
        contents = rsp.read_bytes()
        sources = []
        for line in contents.decode('utf-8-sig').splitlines():
            value = line.strip().strip('"').replace('\\', '/')
            if value.endswith('.cs') and not value.startswith(('-', '/')):
                source = (project / value).resolve()
                if not source.is_relative_to(project):
                    issues.append('source-outside-isolated-project: ' + value)
                    continue
                name = source.relative_to(project).as_posix()
                sources.append(name)
                compiled_sources.add(name)
                if name not in files:
                    issues.append('compiled-source-not-in-baseline: ' + name)
            elif value.endswith('.cs') and not value.startswith('-'):
                issues.append('absolute-compiler-source-needs-review: ' + value)
        if not sources:
            issues.append('empty-bee-source-manifest: ' + assembly)
        manifests.append({'assembly': assembly, 'path': rsp.relative_to(project).as_posix(),
                          'sha256': sha(contents), 'sourceCount': len(sources), 'sources': sorted(sources)})

    expected_sources = {name for name in files if name.startswith('Assets/') and name.endswith('.cs')}
    absent_sources = sorted(expected_sources - compiled_sources)
    issues.extend('source-absent-from-bee-manifests: ' + name for name in absent_sources)
    for name, initial in initial_overlay.items():
        if read_optional(ROOT / name) != initial:
            issues.append('worktree-overlay-changed-during-audit: ' + name)
        if name not in compiled_sources:
            issues.append('overlay-absent-from-bee-manifests: ' + name)

    selected = [row for row in rows if row['path'] in OVERLAYS]
    excluded = [row['path'] for row in rows if row['path'] not in OVERLAYS and row['path'] != SETTINGS
                and row['worktreeVsBaseline'] in ('different', 'missing')
                and row['matchExpected'] not in ('different', 'missing')]
    settings = next(row for row in rows if row['path'] == SETTINGS)
    return {
        'schemaVersion': 1, 'phase': phase,
        'auditedAtUtc': datetime.now(timezone.utc).isoformat(),
        'baselineCommit': baseline, 'worktreeHead': git('rev-parse', 'HEAD').decode().strip(),
        'sourceCommit': source_commit,
        'isolatedProject': str(project), 'scopes': list(SCOPES),
        'status': 'passed' if not issues else 'failed',
        'evidenceLimit': 'Audits current input bytes and existing Bee source manifests; does not run Unity, verify DLL/PDB source checksums, or assert test results.',
        'trackedFiles': len(rows), 'compiledSourceCount': len(compiled_sources),
        'matches': dict(sorted(Counter(row['matchExpected'] for row in rows).items())),
        'categories': dict(sorted(Counter(row['category'] for row in rows).items())),
        'targetOverlays': selected,
        'settingsException': {
            'path': SETTINGS, 'match': settings['matchExpected'],
            'baselineSha256': settings['baselineBlobSha256'], 'isolatedSha256': settings['isolatedSha256'],
            'expectedIsolatedSha256': SETTINGS_ISOLATED_SHA,
            'reference': 'Logs/conversation-budget-input-audit.json: exact pre-existing isolated settings bytes',
            'reason': 'Previously audited Unity 6000.5.5f1 serialization from version 28 to 29; only this exact tracked settings payload is exempted.',
            'changedSettingKeys': settings.get('changedSettingKeys', []),
        },
        'sceneTemplateSettingsException': {
            'path': SCENE_TEMPLATE_SETTINGS, 'approvedByExplicitFlag': template_allowed,
            'isolatedSha256': sha(template_bytes) if template_bytes is not None else None,
            'expectedSha256': SCENE_TEMPLATE_SHA,
            'projectSourceReferencesToTemplateSettingsOrService': template_references,
            'contentSummary': '22 built-in dependency types, no pinned templates, all userAdded=false, newSceneOverride=0; no scene or asset paths.',
            'observedOrigin': 'Absent from the baseline; file creation/modification timestamp observed as 2026-09-14T06:40:22Z, before this turn-source freeze. Its writer was not captured, so Unity generation is inferred rather than proven.',
            'impactAssessment': 'The installed Unity manual limits these settings to the New Scene menu and dependency clone/reference defaults when creating a scene from a template. This project has no direct settings/service references; the conversation scene fixture loads an existing .unity file. No exercised template-instantiation path was found. This is a scoped assessment, not proof of global irrelevance.',
            'evidence': [
                'Unity 6000.5 local Manual/scene-templates-settings.html: New Scene Menu settings and Default Types settings',
                'https://docs.unity3d.com/6000.5/Documentation/Manual/scene-templates-settings.html',
                'Assets/CozyTown/Tests/PlayMode/NpcResourceScenarioPlayModeTests.cs: LoadFreshTown uses EditorSceneManager.LoadSceneAsyncInPlayMode',
                'Assets/CozyTown/Unity/Editor/CozyTownDevSceneMenu.cs: explicit EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, ...) when creating the development scene',
            ],
        },
        'excludedTrackedWorktreeChanges': excluded,
        'untrackedWorktreeInputs': untracked_rows,
        'extraIsolatedInputs': extras, 'approvedExtraInputs': approved_extras,
        'sourcesAbsentFromManifests': absent_sources,
        'issues': sorted(set(issues)), 'compilerManifests': manifests, 'files': rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=ROOT / 'Logs/expression-publish-check')
    parser.add_argument('--baseline', default=BASELINE)
    parser.add_argument('--output', type=Path, default=ROOT / 'Logs/conversation-turn-compiled-source-audit.json')
    parser.add_argument('--phase', choices=('preliminary', 'frozen'), default='preliminary')
    parser.add_argument('--source-commit', help='Commit containing the three frozen overlay sources.')
    parser.add_argument('--allow-pinned-scene-template-settings', action='store_true',
                        help='Explicitly accept only the inspected SceneTemplateSettings hash; the strict default rejects it.')
    args = parser.parse_args()
    output = args.output.resolve()
    if not output.is_relative_to((ROOT / 'Logs').resolve()):
        parser.error('Audit outputs must stay inside the repository Logs directory.')
    commit = git('rev-parse', '--verify', args.baseline + '^{commit}').decode().strip()
    source_commit = (git('rev-parse', '--verify', args.source_commit + '^{commit}').decode().strip()
                     if args.source_commit else None)
    if args.phase == 'frozen' and not source_commit:
        parser.error('A frozen audit requires --source-commit.')
    report = audit(args.project.resolve(), commit, args.phase, source_commit,
                   args.allow_pinned_scene_template_settings)
    if args.phase == 'frozen':
        write_frozen_settings_evidence(args.project.resolve(), commit, report)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: report[key] for key in
                      ('phase', 'status', 'baselineCommit', 'trackedFiles', 'compiledSourceCount',
                       'matches', 'categories', 'targetOverlays', 'extraIsolatedInputs', 'issues')}, indent=2))
    return 0 if report['status'] == 'passed' else 1


if __name__ == '__main__':
    sys.exit(main())
