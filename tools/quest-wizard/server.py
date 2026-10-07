"""Token-protected loopback-only HTTP adapter for the local wizard engine."""
from __future__ import annotations
import ctypes
from datetime import datetime, timezone
import hmac
import importlib.util
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import mimetypes
import os
from pathlib import Path, PurePosixPath
import secrets
import re
import threading
import traceback
from urllib.parse import parse_qs, unquote, urlsplit
import webbrowser

import discovery
from state import STAGES, WizardError, ordinary
from wizard import Engine, REPO, choices


def browse(kind):
    if os.name != "nt" or kind not in ("game", "unity"):
        raise WizardError("browse_unavailable", "Native path selection is unavailable on this host.", "Die native Ordnerauswahl ist hier nicht verfügbar.")
    from ctypes import wintypes as w
    ole = ctypes.WinDLL("ole32")
    shell = ctypes.WinDLL("shell32")
    class BrowseInfo(ctypes.Structure):
        _fields_ = [("owner", w.HWND), ("root", ctypes.c_void_p), ("display", w.LPWSTR),
                    ("title", w.LPCWSTR), ("flags", w.UINT), ("callback", ctypes.c_void_p),
                    ("parameter", ctypes.c_ssize_t), ("image", ctypes.c_int)]
    shell.SHBrowseForFolderW.argtypes = [ctypes.POINTER(BrowseInfo)]
    shell.SHBrowseForFolderW.restype = ctypes.c_void_p
    shell.SHGetPathFromIDListW.argtypes = [ctypes.c_void_p, w.LPWSTR]
    shell.SHGetPathFromIDListW.restype = w.BOOL
    ole.CoInitializeEx.argtypes = [ctypes.c_void_p, w.DWORD]
    ole.CoInitializeEx.restype = ctypes.c_int32
    ole.CoTaskMemFree.argtypes = [ctypes.c_void_p]
    ole.CoTaskMemFree.restype = None
    ole.CoUninitialize.argtypes = []
    ole.CoUninitialize.restype = None
    initialized = ole.CoInitializeEx(None, 2)
    if initialized not in (0, 1): raise WizardError("browse_failed", "Windows could not open the folder picker.", "Windows konnte die Ordnerauswahl nicht öffnen.")
    title = "Select your Gloomhaven installation" if kind == "game" else "Select the Unity 2021.3.5f1 Editor folder"
    label = ctypes.create_unicode_buffer(32768)
    try:
        # Structure fields require a pointer, unlike native function arguments,
        # where ctypes converts an array automatically. Keep label alive here.
        info = BrowseInfo(None, None, ctypes.cast(label, w.LPWSTR), title, 0x41, None, 0, 0)
        item = shell.SHBrowseForFolderW(ctypes.byref(info))
        if not item: return None
        try:
            output = ctypes.create_unicode_buffer(32768)
            if not shell.SHGetPathFromIDListW(item, output): raise WizardError("browse_failed", "Selected folder is not a filesystem path.", "Der ausgewählte Ordner ist kein Dateisystempfad.")
            path = Path(output.value)
            if kind == "unity":
                path = next((candidate for candidate in (path / "Unity.exe", path / "Editor/Unity.exe") if candidate.is_file()), path / "Unity.exe")
            return str(path)
        finally: ole.CoTaskMemFree(item)
    finally: ole.CoUninitialize()


class LocalServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, store, ui_root, *, port=0, engine_factory=Engine, discover=discovery.discover, promotional=False):
        self.store, self.ui_root = store, ordinary(ui_root)
        if not self.ui_root.is_dir() or not (self.ui_root / "index.html").is_file():
            raise WizardError("ui_missing", "Wizard UI files are missing.", "Die Wizard-Oberfläche fehlt.")
        if type(port) is not int or not 0 <= port <= 65535: raise WizardError("server_port", "Invalid loopback server port.")
        self.token = secrets.token_urlsafe(32)
        self.engine_factory, self.discover = engine_factory, discover
        self.jobs, self.jobs_lock, self.browse_lock = {}, threading.Lock(), threading.Lock()
        self.request_log_lock = threading.Lock()
        self.job_errors = {}
        self.artwork_cache, self.artwork_lock = {}, threading.Lock()
        self.mod_source_cache = {}
        self.action_lock = threading.Lock()
        self.promo = None
        if promotional:
            from promotional import Gallery
            self.promo = Gallery(self.ui_root, self.store.root / 'artwork/publisher')
            self.promo.start()
        super().__init__(("127.0.0.1", port), Handler)
        self.origin = "http://127.0.0.1:" + str(self.server_address[1])
        self.url = self.origin + "/#" + self.token

    def request_failure(self, error):
        """Retain startup failures even before a build session exists."""
        helper = discovery.local_support_module(REPO, "support")
        detail, _ = helper.redact("".join(traceback.format_exception(error)),
                                 ((self.token, "[local token]"), (str(REPO), "[builder]"),
                                  (str(self.store.root), "[workspace]")))
        with self.request_log_lock:
            path = ordinary(self.store.root / "logs/wizard-requests.log")
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.exists() and path.stat().st_size >= 262144:
                os.replace(path, path.with_name("wizard-requests.previous.log"))
            with path.open("a", encoding="utf-8") as stream:
                stream.write(datetime.now(timezone.utc).isoformat() + "\n" + detail[-32768:] + "\n")

    def artwork(self, session, state):
        """Resolve only witnessed recovery caches, never a browser filesystem path."""
        adapter_path = ordinary(self.ui_root / "artwork.py")
        if not adapter_path.is_file(): return []
        engine = self.engine_factory(self.store)
        proof = self.store.valid(session, "inspect", engine.key(state, "inspect"))
        if not proof: return []
        game_key = proof.get("details", {}).get("gameKey")
        if not isinstance(game_key, str) or not re.fullmatch(r"[0-9a-f]{64}", game_key): return []
        recovery = ordinary(self.store.root / "build/cache/recovery")
        if not recovery.is_dir(): return []
        candidates = []
        for path in recovery.iterdir():
            if not re.fullmatch(r"[0-9a-f]{64}", path.name): continue
            project = ordinary(path / "project")
            report = ordinary(project / "quest-campaign-report.json")
            if report.is_file(): candidates.append((report.stat().st_mtime_ns, project, report.stat().st_size))
        candidates.sort(key=lambda row: row[0], reverse=True); candidates = candidates[:8]
        signature = (game_key, tuple((time, str(project), size) for time, project, size in candidates), adapter_path.stat().st_mtime_ns)
        with self.artwork_lock:
            cached = self.artwork_cache.get(session)
            if cached and cached[0] == signature: return cached[1]
            spec = importlib.util.spec_from_file_location("_quest_wizard_owned_artwork", adapter_path)
            adapter = importlib.util.module_from_spec(spec); spec.loader.exec_module(adapter)
            rows = []
            for _, project, _ in candidates:
                rows = adapter.verified_project_artwork(project, game_key, limit=12)
                if rows: break
            result = [(row, adapter.read_artwork) for row in rows]
            self.artwork_cache[session] = (signature, result)
            return result

    def visible_state(self, session, state):
        try: rows = self.artwork(session, state)
        except (OSError, ValueError, WizardError, ImportError, AttributeError): rows = []
        value = dict(state)
        source = state.get("completed", {}).get("source", {}).get("details", {})
        source_root = source.get("sourceRoot") or state.get("choices", {}).get("sourceRoot") or str(REPO)
        signature = (source_root, source.get("commit"))
        if signature not in self.mod_source_cache:
            # A session pins its resolved checkout. Keep status polling cheap;
            # discovery independently describes a newly unpacked launch release.
            if len(self.mod_source_cache) >= 16: self.mod_source_cache.clear()
            self.mod_source_cache[signature] = discovery.mod_source(source_root, source.get("commit"))
        value["modSource"] = self.mod_source_cache[signature]
        value["artwork"] = [{"id": row["id"], "url": "/api/artwork?session=" + session + "&id=" + row["id"], "altCode": row["altCode"]} for row, _ in rows]
        if not rows and self.promo: value['artwork'] = self.promo.visible()
        value["capabilities"] = {"artwork": bool(rows)}
        if session in self.job_errors: value["requestError"] = self.job_errors[session]
        return value

    def run_session(self, session):
        self.store.load(session)
        with self.jobs_lock:
            if any(job.is_alive() for job in self.jobs.values()):
                raise WizardError("already_running", "Another wizard run owns this workspace.", "Ein anderer Wizard-Lauf verwendet diesen Arbeitsordner.")
            def work():
                try: self.engine_factory(self.store).run(session)
                except (WizardError, OSError, ValueError) as error:
                    code = error.code if isinstance(error, WizardError) else "stage_failed"
                    message = error.message if isinstance(error, WizardError) else {"en": str(error), "de": str(error)}
                    if code == "already_running":
                        # Another server owns the durable session. A rejected
                        # request must never overwrite that runner's live state.
                        self.job_errors[session] = {"code": code, "message": message}
                        return
                    state = self.store.load(session)
                    state.update(status="blocked", needsActions=[{"code": code, "message": message}])
                    self.store.event(state, code)
            job = threading.Thread(target=work, name="quest-wizard-" + session, daemon=False)
            self.job_errors.pop(session, None)
            self.jobs[session] = job; job.start()

    def close_owned(self):
        # Closing the console/server cancels owned children before allowing the
        # interpreter to exit. Receipts survive; incomplete work stays uncommitted.
        with self.jobs_lock: running = [(session, job) for session, job in self.jobs.items() if job.is_alive()]
        for session, _ in running: self.store.cancel(session)
        for _, job in running: job.join()
        if self.promo: self.promo.stop.set()
        self.server_close()


class Download:
    def __init__(self, path): self.path = ordinary(path)


class Raster:
    def __init__(self, raw, content_type): self.raw, self.content_type = raw, content_type


class Handler(BaseHTTPRequestHandler):
    server_version = "QuestWizard/1"
    def log_message(self, *_): pass  # Request URLs and private selections never enter console logs.

    def send_json(self, value, status=200):
        raw = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status); self.common_headers(); self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw))); self.end_headers(); self.wfile.write(raw)

    def send_raster(self, raw, content_type='image/png'):
        self.send_response(200); self.common_headers(); self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(raw))); self.end_headers(); self.wfile.write(raw)

    def send_download(self, download):
        path = download.path
        self.send_response(200); self.common_headers(); self.send_header("Content-Type", "application/zip")
        self.send_header("Content-Disposition", 'attachment; filename="' + path.name + '"')
        self.send_header("Content-Length", str(path.stat().st_size)); self.end_headers()
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(65536), b""): self.wfile.write(chunk)

    def common_headers(self):
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; img-src 'self' data: blob:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'")

    def authorize(self):
        if self.headers.get("Host") != urlsplit(self.server.origin).netloc: raise WizardError("request_origin", "Untrusted local request host.")
        token = self.headers.get("X-Quest-Token", "")
        if not hmac.compare_digest(token, self.server.token): raise WizardError("request_token", "Invalid local request token.")
        origin = self.headers.get("Origin")
        if origin != self.server.origin:
            # Browsers omit Origin for same-origin GETs. Their immutable fetch
            # metadata plus exact Host/token preserves the same local boundary.
            if not (self.command == "GET" and origin is None and self.headers.get("Sec-Fetch-Site") == "same-origin"):
                raise WizardError("request_origin", "Untrusted local request origin.")

    def body(self):
        if self.headers.get("Content-Type", "").split(";", 1)[0] != "application/json": raise WizardError("request_body", "Expected a JSON request.")
        if self.headers.get("Transfer-Encoding"): raise WizardError("request_body", "Chunked request bodies are unsupported.")
        try: size = int(self.headers.get("Content-Length", "0"))
        except ValueError: raise WizardError("request_body", "Invalid request size.")
        if not 0 < size <= 65536: raise WizardError("request_body", "Request exceeds supported bounds.")
        self.connection.settimeout(5)
        value = json.loads(self.rfile.read(size))
        if not isinstance(value, dict): raise WizardError("request_body", "Expected one JSON object.")
        return value

    def handle_request(self):
        try:
            parsed = urlsplit(self.path)
            if parsed.path.startswith("/api/"):
                self.authorize(); value = self.api(parsed)
                if isinstance(value, Download): self.send_download(value)
                elif isinstance(value, Raster): self.send_raster(value.raw, value.content_type)
                elif isinstance(value, bytes): self.send_raster(value)
                else: self.send_json(value)
                return
            if self.command != "GET": raise WizardError("route", "Unsupported local route.")
            self.static(parsed.path)
        except (WizardError, OSError, ValueError, KeyError, RuntimeError, ImportError, TypeError) as error:
            if not isinstance(error, WizardError):
                try: self.server.request_failure(error)
                except (OSError, ImportError, RuntimeError, ValueError): pass
                error = WizardError("request_failed", "The local request failed. Details are in logs/wizard-requests.log in the wizard workspace.",
                                    "Die lokale Anfrage ist fehlgeschlagen. Details stehen in logs/wizard-requests.log im Wizard-Arbeitsordner.")
            self.send_json({"schema": 1, "event": "error", "code": error.code, "message": error.message, "parameters": error.parameters},
                           403 if error.code in ("request_token", "request_origin") else 400)

    def do_GET(self): self.handle_request()
    def do_POST(self): self.handle_request()

    def api(self, parsed):
        query = parse_qs(parsed.query, strict_parsing=True) if parsed.query else {}
        def selected(name):
            values = query.get(name, [])
            if len(values) != 1: raise WizardError("request_query", "Expected one " + name + " parameter.")
            return values[0]
        if self.command == "GET":
            if parsed.path == "/api/discover": return self.server.discover(REPO, self.server.store)
            if parsed.path == '/api/gallery':
                return {'schema': 1, 'event': 'gallery', 'artwork': self.server.promo.visible() if self.server.promo else []}
            if parsed.path == '/api/promo-artwork':
                identity = selected('id')
                if self.server.promo:
                    from promotional import image_type
                    with self.server.promo.lock: rows = list(self.server.promo.rows)
                    for row in rows:
                        if row['id'] == identity:
                            raw = self.server.promo.read(row)
                            if raw: return Raster(raw, image_type(raw))
                raise WizardError('artwork_unavailable', 'Promotional artwork is unavailable.')
            session = selected("session"); state = self.server.store.load(session)
            if parsed.path == "/api/status": return {"schema": 1, "event": "status", "session": session, "state": self.server.visible_state(session, state)}
            if parsed.path == "/api/artwork":
                identity = selected("id")
                for row, read in self.server.artwork(session, state):
                    if identity == row["id"]:
                        raw = read(row)
                        if raw is not None: return raw
                raise WizardError("artwork_unavailable", "Verified owned artwork is unavailable.")
            if parsed.path == "/api/events":
                after = int(selected("after"))
                if after < 0: raise WizardError("request_query", "Invalid event cursor.")
                return {"schema": 1, "event": "events", "session": session,
                        "events": [row for row in state["events"] if row["sequence"] > after], "lastEvent": state["lastEvent"]}
            if parsed.path == "/api/log":
                stage = selected("stage")
                if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
                from failures import tail
                folder = ordinary(self.server.store.session_dir(session) / "logs")
                paths = [(stage + ".log", ordinary(folder / (stage + ".log")))]
                if stage == "unity":
                    for name in ("unity-install.log", "unity-license-probe.log", "unity-license-process.log", "unity-version.log"):
                        paths.append((name, ordinary(folder / name)))
                elif stage == "build":
                    module = discovery.local_support_module(REPO, "support")
                    failure = module.read_object(self.server.store.root / "build/last-failure.json")
                    paths.extend(module.recovery_logs(self.server.store.root / "build", failure)[:3])
                    if isinstance(failure, dict) and failure.get("stage") == "recovery" and re.fullmatch(r"[0-9a-f]{64}", str(failure.get("key", ""))):
                        name = "recovery-" + failure["key"][:12] + ".log"
                        paths.append((name, ordinary(self.server.store.root / "build/logs" / name)))
                existing = [(name, path) for name, path in paths if path.is_file()]
                limit = 65536 if len(existing) <= 1 else max(1024, (65536 - sum(len(name) + 16 for name, _ in existing)) // len(existing))
                chunks = [tail(path, limit) for _, path in existing]
                text = "".join(("\n--- " + name + " ---\n" if len(existing) > 1 else "") + chunk for (name, _), chunk in zip(existing, chunks))[:65536]
                return {"schema": 1, "event": "log", "session": session, "stage": stage, "text": text,
                        "truncated": any(path.stat().st_size > limit for _, path in existing)}
        else:
            value = self.body()
            if parsed.path == "/api/plan":
                if set(value) - {"choices", "session"}: raise WizardError("request_body", "Unsupported plan fields.")
                selected_choices = choices(value["choices"])
                state = self.server.store.amend(value["session"], selected_choices) if value.get("session") else self.server.store.create(selected_choices)
                return {"schema": 1, "event": "planned", "session": state["session"], "state": state}
            if parsed.path == "/api/qualify":
                if set(value) != {"gameRoot"}: raise WizardError("request_body", "Expected only the owned game path.")
                selected_choices = choices({"gameRoot": value["gameRoot"]})
                from qualification import qualify
                return qualify(self.server.store.root, game_root=selected_choices["gameRoot"], repo=REPO)
            if parsed.path == "/api/browse":
                if set(value) != {"kind"}: raise WizardError("request_body", "Unsupported browse fields.")
                with self.server.browse_lock: path = browse(value["kind"])
                return {"schema": 1, "event": "browse", "path": path}
            if parsed.path == '/api/action':
                if set(value) != {'session', 'action', 'nonce'}: raise WizardError('request_body', 'Expected a current declared action.')
                from unity_setup import request_action
                with self.server.action_lock:
                    return request_action(self.server.store, value['session'], value['action'], value['nonce'])
            if set(value) != {"session"}: raise WizardError("request_body", "Expected only the session ID.")
            session = value["session"]
            if parsed.path == "/api/support":
                support = discovery.local_support_module(REPO, "support")
                result = support.export_support(self.server.store.root, session)
                return Download(result["path"])
            if parsed.path == "/api/run":
                self.server.run_session(session); return {"schema": 1, "event": "started", "session": session}
            if parsed.path == "/api/cancel":
                self.server.store.cancel(session); return {"schema": 1, "event": "cancel_requested", "session": session}
        raise WizardError("route", "Unsupported local route.")

    def static(self, raw):
        if self.headers.get("Host") != urlsplit(self.server.origin).netloc: raise WizardError("request_origin", "Untrusted local request host.")
        name = unquote(raw).lstrip("/") or "index.html"
        relative = PurePosixPath(name)
        if relative.is_absolute() or ".." in relative.parts or "\\" in name or ":" in name:
            raise WizardError("static_path", "Invalid UI asset path.")
        path = ordinary(self.server.ui_root.joinpath(*relative.parts))
        if self.server.ui_root not in path.parents or not path.is_file() or path.suffix not in (".html", ".css", ".js", ".mjs", ".png", ".jpg", ".svg", ".woff2", ".ico"):
            raise WizardError("static_path", "Unknown UI asset.")
        if path.stat().st_size > 16 * 1048576: raise WizardError("static_size", "UI asset exceeds supported bounds.")
        raw = path.read_bytes()
        self.send_response(200); self.common_headers(); self.send_header("Content-Type", "text/javascript; charset=utf-8" if path.suffix in (".js", ".mjs") else mimetypes.guess_type(path.name)[0] or "application/octet-stream")
        self.send_header("Content-Length", str(len(raw))); self.end_headers(); self.wfile.write(raw)


def serve(store, ui_root, *, port=0, open_browser=False):
    from qualification import qualify
    qualify(store.root)
    if (REPO / "quest-builder-release.json").is_file() and not (REPO / ".git").exists():
        discovery.local_support_module(REPO, "release").verified_source_inventory(REPO)
    server = LocalServer(store, ui_root, port=port, promotional=True)
    print(json.dumps({"schema": 1, "event": "server", "url": server.url}), flush=True)
    if open_browser: webbrowser.open(server.url, new=2)
    try: server.serve_forever(poll_interval=0.2)
    except KeyboardInterrupt: pass
    finally: server.close_owned()
