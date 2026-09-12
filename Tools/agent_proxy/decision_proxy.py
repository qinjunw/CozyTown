"""Local adapter for CozyTown decision candidates and DeepSeek chat completions."""

import json
import threading
import time
import copy
import argparse
from pathlib import Path
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def load_cozytown_key(path):
    try:
        records = json.loads(Path(path).read_text(encoding="utf-8-sig"))
        matches = [item for item in records.values() if isinstance(item, dict) and item.get("Todo") == "CozyTown"]
    except (OSError, ValueError, AttributeError):
        raise ProxyError("proxy.credential_file_invalid", 400) from None
    if len(matches) != 1:
        raise ProxyError("proxy.credential_ambiguous", 400)
    key = matches[0].get("testingAPIKey")
    if not isinstance(key, str) or not key.strip() or any(char.isspace() for char in key.strip()):
        raise ProxyError("proxy.credential_invalid", 400)
    return key.strip()


def create_server(service, port=0):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format, *args):
            pass

        def reply(self, status, value):
            body = json.dumps(value, ensure_ascii=False, allow_nan=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self):
            try:
                if self.path != "/decide":
                    raise ProxyError("proxy.route_unknown", 404)
                if self.headers.get_content_type() != "application/json" or self.headers.get("Origin"):
                    raise ProxyError("proxy.request_type_invalid", 415)
                length_text = self.headers.get("Content-Length", "")
                if not length_text.isdigit() or not 0 < int(length_text) <= 32768:
                    raise ProxyError("proxy.request_size_invalid", 413)
                self.connection.settimeout(8)
                body = self.rfile.read(int(length_text))
                if len(body) != int(length_text):
                    raise ProxyError("proxy.request_incomplete", 400)
                try:
                    context = json.loads(body)
                except (ValueError, UnicodeError):
                    raise ProxyError("proxy.request_json_invalid", 400) from None
                self.reply(200, service.decide(context))
            except ProxyError as error:
                self.reply(error.status, {"error": error.code})
            except (BrokenPipeError, ConnectionResetError):
                pass
            except Exception:
                self.reply(500, {"error": "proxy.internal_error"})

        def do_GET(self):
            if self.path == "/status":
                self.reply(200, service.status)
            else:
                self.reply(404, {"error": "proxy.route_unknown"})

    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    server.daemon_threads = True
    return server


class ProxyError(Exception):
    def __init__(self, code, status=502):
        super().__init__(code)
        self.code = code
        self.status = status


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        raise ProxyError("provider.redirect_rejected")


class DeepSeekTransport:
    def __init__(self, api_key, *, opener=None, timeout=7):
        if not isinstance(api_key, str) or not api_key.strip():
            raise ValueError("A nonempty API key is required")
        self._key = api_key.strip()
        self._opener = opener or urllib.request.build_opener(_NoRedirect())
        self._timeout = timeout

    def __call__(self, payload):
        request = urllib.request.Request(
            "https://api.deepseek.com/chat/completions",
            data=json.dumps(payload, ensure_ascii=False, allow_nan=False).encode("utf-8"),
            headers={"Authorization": "Bearer " + self._key, "Content-Type": "application/json"},
            method="POST",
        )
        try:
            with self._opener.open(request, timeout=self._timeout) as response:
                raw = response.read(65537)
            if len(raw) > 65536:
                raise ProxyError("provider.response_too_large")
            return json.loads(raw)
        except urllib.error.HTTPError as error:
            error.close()
            raise ProxyError("provider.http_" + str(error.code)) from None
        except (TimeoutError, urllib.error.URLError):
            raise ProxyError("provider.transport_failure") from None
        except (ValueError, UnicodeError):
            raise ProxyError("provider.invalid_response") from None


SYSTEM_PROMPT = """You are the single CozyTown resident identified by npcId.
Use the supplied persona and world context. Return exactly one JSON candidate using
schemaVersion from the request and an operation from allowedOperations.
The host checks identity, availability, arrival and turns. Text never transfers items
or proves an action happened. Never invent an actor, location, meeting or completed event.
For a meeting opportunity, consider inviting the named partner to discuss the pond
and local cooking. For an invitation, accept or decline the supplied meeting.
During your conversation turn, respond to the supplied transcript in character.
Use concise English dialogue, at most 180 characters. Stop when the topic is finished.
Examples: {"schemaVersion":1,"operation":"wait"};
{"schemaVersion":1,"operation":"inspect_location","locationId":"known-id"};
{"schemaVersion":1,"operation":"visit","locationId":"known-id","activity":"resting","durationGameMinutes":20}.
Social candidate fields, when allowed: invite uses planId; accept_invite,
decline_invite, say and end_conversation use meetingId; say also uses text.
For resource meetings, social.resources contains fixed trade terms, only your own
relevant quantity and balance, and deliveryResultCode from the host. Invite or accept
only if you agree to those terms. When delivery is offered, use deliver or cancel_exchange
with meetingId. Never supply replacement items, actors, quantities or prices.
Only resource.delivered proves transfer. Do not claim fishing, cooking or other production
has happened without a host result. Use gameTotalMinutes modulo 1440 for time of day.
Copy supplied identifiers exactly. Do not include explanation outside the JSON object.
"""


class ProxyService:
    def __init__(self, transport, *, model="deepseek-v4-flash", max_calls=12, max_parallel=2, trace=None):
        self.transport = transport
        self.model = model
        if not isinstance(max_calls, int) or max_calls < 1:
            raise ValueError("max_calls must be a positive integer")
        self._max_calls = max_calls
        if not isinstance(max_parallel, int) or max_parallel < 1:
            raise ValueError("max_parallel must be a positive integer")
        self._max_parallel = max_parallel
        self._inflight = 0
        self._calls = 0
        self._lock = threading.Lock()
        self._measurements = []
        self._trace = trace

    @property
    def status(self):
        with self._lock:
            return {"requestedModel": self.model, "attemptedProviderCalls": self._calls,
                    "remainingCalls": self._max_calls - self._calls, "inflight": self._inflight}

    @property
    def measurements(self):
        with self._lock:
            return copy.deepcopy(self._measurements)

    def decide(self, context):
        try:
            operations = {"wait", "inspect_location", "visit", "invite", "accept_invite", "decline_invite", "say", "end_conversation", "deliver", "cancel_exchange"}
            if not isinstance(context, dict) or type(context.get("schemaVersion")) is not int or context["schemaVersion"] not in (1, 2, 3):
                raise ValueError
            if not isinstance(context.get("npcId"), str) or not context["npcId"].strip():
                raise ValueError
            allowed = context.get("allowedOperations")
            if not isinstance(allowed, list) or not allowed or any(not isinstance(op, str) or op not in operations for op in allowed):
                raise ValueError
            context_json = json.dumps(context, ensure_ascii=False, allow_nan=False)
            if len(context_json.encode("utf-8")) > 32768:
                raise ValueError
        except (TypeError, ValueError, UnicodeError):
            raise ProxyError("proxy.context_invalid", 400) from None
        with self._lock:
            if self._calls >= self._max_calls:
                raise ProxyError("proxy.call_limit", 429)
            if self._inflight >= self._max_parallel:
                raise ProxyError("proxy.busy", 503)
            self._calls += 1
            self._inflight += 1
            call_number = self._calls
        started = time.monotonic()
        record = {"call": call_number, "npcId": context.get("npcId"), "decisionId": context.get("decisionId"),
                  "step": context.get("step"), "requestedModel": self.model, "status": "provider.failure"}
        try:
            response = self.transport({
                "model": self.model,
                "messages": [{"role": "system", "content": SYSTEM_PROMPT},
                             {"role": "user", "content": context_json}],
                "thinking": {"type": "disabled"},
                "max_tokens": 512,
                "response_format": {"type": "json_object"},
                "stream": False,
            })
            record["returnedModel"] = response.get("model")
            usage = response.get("usage") or {}
            for source, target in (("prompt_tokens", "promptTokens"), ("completion_tokens", "completionTokens"),
                                   ("total_tokens", "totalTokens")):
                value = usage.get(source)
                record[target] = value if type(value) is int and value >= 0 else None
            try:
                choice = response["choices"][0]
                content = choice["message"]["content"]
                if not isinstance(content, str) or len(content.encode("utf-8")) > 16384 or choice.get("finish_reason") == "length":
                    raise ValueError
                candidate = json.loads(content)
                if not isinstance(candidate, dict) or candidate.get("schemaVersion") != context.get("schemaVersion"):
                    raise ValueError
                if candidate.get("operation") not in context.get("allowedOperations", []):
                    raise ValueError
                json.dumps(candidate, allow_nan=False)
                fields = {"schemaVersion", "operation"} | {
                    "wait": set(), "inspect_location": {"locationId"},
                    "visit": {"locationId", "activity", "durationGameMinutes"}, "invite": {"planId"},
                    "accept_invite": {"meetingId"}, "decline_invite": {"meetingId"},
                    "say": {"meetingId", "text"}, "end_conversation": {"meetingId"},
                    "deliver": {"meetingId"}, "cancel_exchange": {"meetingId"},
                }[candidate["operation"]]
                candidate = {key: value for key, value in candidate.items() if key in fields}
                record["candidate"] = candidate
                record["status"] = "passed"
                return candidate
            except (KeyError, IndexError, TypeError, ValueError, UnicodeError):
                raise ProxyError("provider.candidate_invalid") from None
        except ProxyError as error:
            record["status"] = error.code
            raise
        except Exception:
            raise ProxyError("provider.failure") from None
        finally:
            record["elapsedMilliseconds"] = round((time.monotonic() - started) * 1000)
            with self._lock:
                self._inflight -= 1
                self._measurements.append(record)
                if self._trace is not None:
                    self._trace.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
                    self._trace.flush()


def main():
    parser = argparse.ArgumentParser(description="Run the loopback CozyTown decision proxy with a fixed provider-call cap.")
    parser.add_argument("--credential-file", required=True, type=Path)
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--max-calls", type=int, default=12)
    parser.add_argument("--trace", required=True, type=Path, help="New local JSONL file for sanitized decision measurements.")
    args = parser.parse_args()
    try:
        key = load_cozytown_key(args.credential_file)
        with args.trace.open("x", encoding="utf-8") as trace:
            service = ProxyService(DeepSeekTransport(key), max_calls=args.max_calls, trace=trace)
            server = create_server(service, args.port)
            print(json.dumps({"endpoint": "http://127.0.0.1:" + str(server.server_port) + "/decide", **service.status}), flush=True)
            try:
                server.serve_forever()
            finally:
                server.server_close()
    except ProxyError as error:
        parser.exit(1, error.code + "\n")
    except (OSError, ValueError):
        parser.exit(1, "proxy.startup_failed\n")
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
