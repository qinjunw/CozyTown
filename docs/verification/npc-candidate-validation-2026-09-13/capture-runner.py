import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import threading

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root/'Tools/agent_proxy'))
from decision_proxy import DeepSeekTransport, ProxyService, SYSTEM_PROMPT, create_server, load_cozytown_key
from grounding_experiment import _record_responses

parser = argparse.ArgumentParser()
parser.add_argument('--credential-file', required=True)
parser.add_argument('--output-dir', required=True, type=Path)
parser.add_argument('--port', type=int, default=25712)
args = parser.parse_args()
args.output_dir.mkdir(exist_ok=False)
manifest = {'mode':'candidate_field_fault_injection', 'startedAtUtc':datetime.now(timezone.utc).isoformat(),
    'codeCommit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
    'requestedModel':'deepseek-v4-flash','maxCalls':96,'plannedTrials':12,
    'proxyAutomaticRetries':0,'maxHostCorrectionsPerDecision':1,'thinking':'disabled',
    'maxOutputTokens':512,'providerTimeoutSeconds':7,
    'injection':'After preserving the raw provider response, remove only top-level planId from the first step-1 invite candidate in each eligible world. Later responses are unchanged. An injected omission is not a spontaneous model error.',
    'systemPromptSha256':hashlib.sha256(SYSTEM_PROMPT.encode()).hexdigest(),
    'sourceSha256':{name:hashlib.sha256((root/'Tools/agent_proxy'/name).read_bytes()).hexdigest()
                    for name in ('decision_proxy.py','grounding_experiment.py')},
    'captureRunnerSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
(args.output_dir/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
with (args.output_dir/'proxy.jsonl').open('x',encoding='utf-8') as trace, \
     (args.output_dir/'provider-responses.jsonl').open('x',encoding='utf-8') as raw, \
     (args.output_dir/'injections.jsonl').open('x',encoding='utf-8') as injections:
    transport = _record_responses(DeepSeekTransport(load_cozytown_key(args.credential_file)), raw)
    injected_worlds = set()
    gate = threading.Lock()

    def with_field_fault(payload):
        response = transport(payload)
        context = json.loads(payload['messages'][1]['content'])
        if (context.get('step') != 1 or (context.get('social') or {}).get('kind') != 'opportunity'
                or 'invite' not in context.get('allowedOperations', [])):
            return response
        try:
            original = response['choices'][0]['message']['content']
            candidate = json.loads(original)
        except (KeyError, IndexError, TypeError, ValueError):
            return response
        if not isinstance(candidate, dict) or candidate.get('operation') != 'invite' or 'planId' not in candidate:
            return response
        with gate:
            if context['worldRunId'] in injected_worlds:
                return response
            injected_worlds.add(context['worldRunId'])
            modified = dict(candidate)
            del modified['planId']
            content = json.dumps(modified,ensure_ascii=False,allow_nan=False)
            injections.write(json.dumps({'worldRunId':context['worldRunId'], 'decisionId':context['decisionId'],
                'step':context['step'],'kind':'remove_top_level_planId','originalContentSha256':hashlib.sha256(original.encode()).hexdigest(),
                'effectiveContent':content},ensure_ascii=False)+'\n')
            injections.flush()
        result = copy.deepcopy(response)
        result['choices'][0]['message']['content'] = content
        return result

    proxy = ProxyService(with_field_fault,max_calls=96,trace=trace)
    server = create_server(proxy,args.port)
    print(json.dumps({'endpoint':f'http://127.0.0.1:{server.server_port}/decide',**proxy.status}),flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()
