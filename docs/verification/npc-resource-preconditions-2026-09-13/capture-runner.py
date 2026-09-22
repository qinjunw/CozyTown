import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / 'Tools/agent_proxy'))
from decision_proxy import DeepSeekTransport, ProxyService, SYSTEM_PROMPT, create_server, load_cozytown_key
from grounding_experiment import _record_responses

args = argparse.ArgumentParser()
args.add_argument('--credential-file', required=True)
args.add_argument('--output-dir', required=True, type=Path)
args.add_argument('--port', type=int, default=25710)
options = args.parse_args()
options.output_dir.mkdir(exist_ok=False)
manifest = {
    'mode': 'production_resource_v4', 'startedAtUtc': datetime.now(timezone.utc).isoformat(),
    'codeCommit': subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
    'maxCalls':96, 'plannedTrials':12, 'automaticRetries':0,
    'requestedModel':'deepseek-v4-flash', 'thinking':'disabled','maxOutputTokens':512,'providerTimeoutSeconds':7,
    'systemPromptSha256':hashlib.sha256(SYSTEM_PROMPT.encode()).hexdigest(),
    'sourceSha256':{name:hashlib.sha256((root/'Tools/agent_proxy'/name).read_bytes()).hexdigest()
        for name in ('decision_proxy.py','grounding_experiment.py')},
    'captureRunnerSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    'instrumentation':'Default ProxyService receives the unmodified Unity request. Only the existing response-content recorder is reused; no A/B/C/D projection or filtering adapter is invoked.'
}
(options.output_dir/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
with (options.output_dir/'proxy.jsonl').open('x',encoding='utf-8') as trace, (options.output_dir/'provider-responses.jsonl').open('x',encoding='utf-8') as raw:
    proxy = ProxyService(_record_responses(DeepSeekTransport(load_cozytown_key(options.credential_file)),raw),max_calls=96,trace=trace)
    server = create_server(proxy,options.port)
    print(json.dumps({'endpoint':f'http://127.0.0.1:{server.server_port}/decide',**proxy.status}),flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()
