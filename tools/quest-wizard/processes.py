"""Owned process trees, cancellation and durable PID creation identities."""
from __future__ import annotations
import ctypes
import hashlib
import json
import importlib.util
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sqlite3
import time

from state import Cancelled, STAGES, WizardError, atomic_json, ordinary, stage_progress, native_memory_progress
from stage_plan import PLANS, MEMORY_OBSERVATIONS

# Pinned Unity2021.3 Bee actions witnessed in actual import/Android build logs.
# A numeric frame plus an arbitrary word is deliberately not accepted as work.
BEE_ACTIONS = (r"Csc|Clang|Compile|Link|CopyFiles|WriteText|WriteResponseFile|IL2CPP\w*|Generate\w*|"
               r"Archive|Lump|Pch|MoveFiles|DeleteFiles|MovedFromExtractor(?:Combine)?|MakeLump|"
               r"C_Android_arm64|Link_Android_arm64|UnityLinker|SplitFile|ActionGenerateProjectFiles|"
               r"ExtractUsedFeatures|GuidGenerator|NdkObjCopy|ICallRegistrationGenerator|"
               r"ClassRegistrationGenerator|Stripping|Adding")

_BEE_NATIVE = re.compile(r"^(?:C_Android_\w+|Link_Android_\w+|Clang|Compile|Link|Archive|"
                         r"MakeLump|Lump|Pch|NdkObjCopy|ICallRegistrationGenerator|"
                         r"ClassRegistrationGenerator|Stripping|Adding)(?:\s|$)")
_BEE_CONVERSION = re.compile(r"^(?:IL2CPP\w*|UnityLinker|ExtractUsedFeatures)(?:\s|$)")
# Registration compilation and linking libunity occur in preliminary graphs
# before generated game CPP exists. Only actual Android compiler/link actions
# qualify the finite game-native plan; the other tasks retain local scope.
_BEE_COMPILER = re.compile(r"^(?:C_Android_\w+|Link_Android_\w+|Clang)(?:\s|$)")
_BEE_DAG_LIMIT = 16 * 1048576
_BEE_NODE_LIMIT = 250000


def _bee_scope(annotation):
    if _BEE_NATIVE.match(annotation): return "native"
    if _BEE_CONVERSION.match(annotation): return "il2cpp"
    return "staging"


class PlayerBeePlan:
    """Observe one existing Player DAG and its real cache/execution counter.

    Unity2021.3 regenerates the graph after split files and IL2CPP codegen.
    Capture231755's9233 copying actions therefore prove no C++ compilation.
    Only a graph containing native tasks supplies the combined native-plan
    denominator. No generated output is opened or rebuilt by this observer.
    """
    def __init__(self, project, relative, graph_id, *, profile=None, started=0):
        project = ordinary(project)
        normalized = relative.replace("\\", "/")
        if not re.fullmatch(r"Library/Bee/Player[A-Za-z0-9_.-]+\.dag\.json", normalized):
            raise ValueError("Not an owned Player graph")
        dag = ordinary(project / normalized)
        before = dag.stat()
        if before.st_size > _BEE_DAG_LIMIT: raise ValueError("Optional graph exceeds observation limit")
        with dag.open("rb") as stream: raw = stream.read(_BEE_DAG_LIMIT + 1)
        after = dag.stat()
        witness = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)
        if len(raw) > _BEE_DAG_LIMIT or witness(before) != witness(after): raise ValueError("Graph changed")
        value = json.loads(raw)
        nodes, named = value.get("Nodes"), value.get("NamedNodes")
        if not isinstance(nodes, list) or not 0 < len(nodes) <= _BEE_NODE_LIMIT or not isinstance(named, dict):
            raise ValueError("Unsupported graph")
        root = named.get("Player")
        if type(root) is not int or not 0 <= root < len(nodes): raise ValueError("Missing Player target")
        pending, reached = [root], set()
        while pending:
            index = pending.pop()
            if type(index) is not int or not 0 <= index < len(nodes): raise ValueError("Invalid dependency")
            if index in reached: continue
            reached.add(index)
            node = nodes[index]
            if not isinstance(node, dict): raise ValueError("Invalid graph node")
            for name in ("ToBuildDependencies", "ToUseDependencies"):
                dependencies = node.get(name, [])
                if not isinstance(dependencies, list): raise ValueError("Invalid dependencies")
                pending.extend(dependencies)
        self.annotations = {index: nodes[index].get("Annotation") for index in reached}
        if any(not isinstance(annotation, str) for annotation in self.annotations.values()): raise ValueError("Invalid annotation")
        self.counts = {scope: sum(_bee_scope(annotation) == scope for annotation in self.annotations.values())
                       for scope in ("staging", "il2cpp", "native")}
        self.total, self.done, self.graph_id = len(reached), 0, graph_id
        # The final finite task is the backend result. Merely evaluating every
        # node (including reused ones) cannot show100 while a graph regenerates.
        # MakeLump and registration/preparation actions can already exist in
        # the copy graph before codegen exposes any actual compiler/link jobs.
        self.qualified = any(_BEE_COMPILER.match(annotation) for annotation in self.annotations.values())
        log = value.get("StructuredLogFileName")
        if not isinstance(log, str) or not re.fullmatch(r"Library/Bee/[A-Za-z0-9_.-]+\.json", log.replace("\\", "/")):
            raise ValueError("Unsupported structured log")
        self.log = ordinary(project / log.replace("\\", "/"))
        self.dag_name, self.dag_mtime = normalized[:-5], after.st_mtime_ns
        self.profile = ordinary(project / profile.replace("\\", "/")) if isinstance(profile, str) and re.fullmatch(
            r"Library[/\\]Bee[/\\]backend_profiler\d+\.traceevents", profile) else None
        self.started_ns, self.epoch_ns = int(started * 1000000000), None
        self.profile_clock = None
        self.initial_log = self._log_witness()
        self.offset, self.pending, self.identity, self.initialized = 0, b"", None, False
        self.failed, self.last_emitted, self.conversion_done = False, None, False

    def _log_witness(self):
        if not self.log.is_file(): return None
        value = self.log.stat()
        return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)

    def fields(self, *, status="progress", detail=None):
        counts = self.counts
        description = (f"[bee-native-plan] [bee-graph:{self.graph_id}] [bee-scope:mixed] "
                       f"Player graph: {self.done} / {self.total} nodes; "
                       f"staging {counts['staging']}, code conversion {counts['il2cpp']}, "
                       f"native compilation/link {counts['native']}; backend result "
                       + ("completed" if status == "complete" else "pending"))
        if detail: description += " · " + detail
        return {"phase": "unity-native-build-plan", "done": self.done + int(status == "complete"),
                "total": self.total + 1, "unit": "tasks", "detail": description[:1024], "status": status}

    def read(self):
        """Bounded live observations; cached nodes count only when Bee counts them."""
        if not self.qualified or not self.log.is_file(): return None
        # A retained DAG's timestamp can be older than yesterday's structured
        # log. Bind that log to this backend's separate profiler epoch instead;
        # profilerN is also distinct for each normal graph-regeneration run.
        if self.profile is None: return None
        header = b""
        if self.profile.is_file() and self.profile.stat().st_mtime_ns >= self.started_ns:
            with self.profile.open("rb") as stream: header = stream.read(65536)
        epoch, clock = None, None
        for line in header.splitlines()[:64]:
            try: entry = json.loads(line.strip().lstrip(b","))
            except (ValueError, UnicodeError): continue
            timestamp = entry.get("ts") if isinstance(entry, dict) else None
            if isinstance(entry, dict) and entry.get("name") == "DriverInitData" and type(timestamp) in (int, float) \
                    and math.isfinite(timestamp) and timestamp >= 0:
                clock = timestamp
                if timestamp > 10 ** 15: epoch = int(timestamp * 1000)
                break
        if epoch is not None and epoch < self.started_ns: return None
        if clock is not None and self.profile_clock is not None and clock != self.profile_clock:
            self.offset, self.pending, self.identity, self.initialized = 0, b"", None, False
            self.done, self.failed, self.last_emitted, self.conversion_done = 0, False, None, False
            self.initial_log = self._log_witness()
        if clock is not None: self.profile_clock = clock
        self.epoch_ns = epoch
        current = self.log.stat()
        if current.st_mtime_ns < max(self.dag_mtime, epoch or self.started_ns): return None
        # Windows clocks may be relative, and profilers can flush only on exit.
        # Require a fresh write/init after this scoped backend observation
        # instead of treating uptime or a retained old log as current progress.
        # A final profiler flush being newer than its log is not a failure.
        if epoch is None and self._log_witness() == self.initial_log: return None
        identity = (current.st_dev, current.st_ino)
        if self.identity != identity or current.st_size < self.offset:
            self.offset, self.pending, self.initialized, self.identity = 0, b"", False, identity
        with self.log.open("rb") as stream:
            stream.seek(self.offset); block = stream.read(1048576); self.offset += len(block)
        lines = (self.pending + block).split(b"\n"); self.pending = lines.pop()[-65536:]
        for line in lines:
            if len(line) > 65536: continue
            try: value = json.loads(line)
            except (ValueError, UnicodeError): continue
            if not isinstance(value, dict): continue
            if value.get("msg") == "init":
                dag_file = value.get("dagFile")
                self.initialized = (isinstance(dag_file, str) and dag_file.replace("\\", "/") == self.dag_name
                                    and value.get("targets") == ["Player"])
            elif self.initialized and value.get("msg") == "noderesult":
                index = value.get("index")
                if type(index) is not int or self.annotations.get(index) != value.get("annotation"): continue
                done, queued = value.get("processed_node_count"), value.get("number_of_nodes_ever_queued")
                if type(done) is not int or type(queued) is not int or not 0 <= done <= queued <= self.total: continue
                if type(value.get("exitcode")) is not int: continue
                if value["exitcode"] != 0: self.failed = True
                if value["exitcode"] == 0 and value.get("annotation", "").startswith("IL2CPP_CodeGen "):
                    self.conversion_done = True
                # Raw stdout may already be ahead of this buffered JSON tail.
                # Preserve its high counter, but still consume authoritative
                # per-node success/failure results from the same graph.
                self.done = max(self.done, done)
        emitted = (self.done, self.failed)
        if emitted == self.last_emitted: return None
        self.last_emitted = emitted
        return self.fields(status="failed" if self.failed else "progress")


def diagnostic_command(argv):
    """Record tool invocation without credential flags or environment values."""
    result = []; hide_next = False
    for raw in argv:
        value = str(raw)
        if hide_next: result.append('[redacted]'); hide_next = False; continue
        if value.startswith('-') and re.search(r'(?i)(password|token|secret|serial|credential)', value):
            result.append(value.split('=')[0] + ('=[redacted]' if '=' in value else ''))
            hide_next = '=' not in value
        else: result.append(value[:4096])
    return result[:128]


_UNITY_WORK = None


def _unity_work_module():
    global _UNITY_WORK
    if _UNITY_WORK is None:
        path = Path(__file__).resolve().parents[1] / "quest-builder/unity_work.py"
        spec = importlib.util.spec_from_file_location("_wizard_unity_work", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        _UNITY_WORK = module
    return _UNITY_WORK


class ProgressParser:
    """Recognize measured counters, never infer a percentage from elapsed time."""
    def __init__(self, *, started=0):
        self.started = started
        self.bee_runs = {}
        self.bee_current = {}
        self.player_graphs = {}
        self.bee_epochs = {}
        self.unity_operations = {}
        self.active_operation = None
        self.closed_operations = set()
        self.import_runs = {}
        self.import_pending = {}
        self.import_coverage = {}
        self.coverage_invocations = {}
        self.plan_signatures = {}
        self.native_import_counts = {}
        self.shader_runs = {}
        self.gradle_runs = {}
        self.gradle_counters = {}
        self.gradle_epochs = {}
        self.gradle_retired_epochs = {}
        self.memory_rejections = set()
        self.shader_coverage = {}
        self.additional_progress = []

    def take_observations(self):
        result, self.additional_progress = self.additional_progress, []
        return result

    def _queue_observation(self, fields):
        if fields is not None:
            if len(self.additional_progress) >= 64: self.additional_progress.pop(0)
            self.additional_progress.append(fields)

    def _shader_coverage(self, source, *, finished=None):
        """Reuse the existing finite asset census; read only Shader declarations.

        Duplicate declared names identify the compiler's actual log namespace,
        not GUIDs or a promised variant queue. This explicitly named coverage
        can reach its final task only at the Android platform handoff.
        """
        owner = self._unity_operation(source)
        if owner not in ("player", "update-code") or owner in self.closed_operations: return None
        key = (source, owner)
        fresh = key not in self.shader_coverage
        if key not in self.shader_coverage:
            if len(self.shader_coverage) >= 32: self.shader_coverage.clear()
            self.shader_coverage[key] = None
            coverage = self.import_coverage.get(source)
            if coverage is None: return None
            try:
                paths = sorted(path for path in coverage.known if path.endswith(".shader"))
                if not paths or len(paths) > 10000: return None
                project = ordinary(coverage.plan["project"])
                roots = [("Assets", project / "Assets"), *_unity_work_module()._package_roots(project)]
                names = set()
                for logical in paths:
                    if any(part in ("", ".", "..") for part in logical.split("/")): return None
                    prefix, root = next(((prefix, root) for prefix, root in roots if logical.startswith(prefix + "/")), (None, None))
                    if root is None: continue
                    path = ordinary(root / logical[len(prefix) + 1:])
                    with path.open("rb") as stream: header = stream.read(2048)
                    if header.startswith(b"\xef\xbb\xbf"): header = header[3:]
                    # Consume comments/other strings before matching a real
                    # line-leading declaration, including commented examples.
                    tokens = re.finditer(rb'//[^\r\n]*|/\*[\s\S]*?(?:\*/|\Z)|"(?:\\.|[^"\\])*"|'
                                         rb'^[\t ]*Shader\s+"([^"\r\n]{1,512})"', header, re.M)
                    declaration = next((token[1] for token in tokens if token[1] is not None), None)
                    if declaration: names.add(declaration.decode("utf-8"))
                if names: self.shader_coverage[key] = {"names": names, "observed": set()}
            except (OSError, ValueError, TypeError, UnicodeError, WizardError, AttributeError, MemoryError, RecursionError): return None
        census = self.shader_coverage.get(key)
        if not census: return None
        if finished is None and not fresh: return None
        if finished is not None:
            if finished not in census["names"] or finished in census["observed"]: return None
            census["observed"].add(finished)
        done, known = len(census["observed"]), len(census["names"])
        return self._unity_fields({"phase": "unity-player-shaders", "done": done, "total": known + 1,
            "unit": "tasks", "status": "progress", "detail": f"[shader-coverage] {done} / {known} known declared Shader names observed; +1 Android platform handoff pending"}, source)

    def _conversion_complete(self, source, graph):
        if graph.get("conversionClosed"): return None
        graph["conversionClosed"] = True
        return self._unity_fields({"phase": "unity-il2cpp", "done": 2, "total": 2, "unit": "tasks",
            "status": "complete", "detail": f"[bee-graph:{graph['id']}] [bee-scope:il2cpp] Managed code conversion returned successfully"}, source)

    @staticmethod
    def _fallback_native_fields(graph, fields, *, status="progress", detail=None):
        """The actual printed CPP-containing queue remains finite without a DAG."""
        done, total = fields.get("done"), fields.get("total")
        if type(done) is not int or type(total) is not int or not 0 <= done <= total: return None
        terminal = status == "complete"
        return {"phase": "unity-native-build-plan", "done": done + int(terminal), "total": total + 1,
            "unit": "tasks", "status": status, "detail": (f"[bee-native-plan] [bee-graph:{graph['id']}] [bee-scope:mixed] "
                f"Player graph: {done} / {total} evaluated nodes; +1 backend result "
                + ("completed" if terminal else "pending") + " · " + (detail or fields.get("detail") or ""))[:1024]}

    def _player_bee_boundary(self, line, source):
        owner = self._unity_operation(source)
        if owner not in ("player", "update-code") or owner in self.closed_operations: return None
        if re.search(r"^Starting: .*(?:[/\\]|\")AndroidPlayerBuildProgram\.exe(?:\"|\s|$)", line):
            self.player_graphs.pop(source, None)
            return self._unity_fields({"phase": "unity-player-platform-handoff", "done": 1, "total": 1,
                "unit": "tasks", "status": "complete", "detail": "Android Player build program started; scene and Shader preparation returned"}, source)
        if line.startswith("Starting:") and "--dagfile=" in line:
            match = re.search(r'--dagfilejson=(?:"([^"]+)"|(\S+))', line)
            if match: relative = match[1] or match[2]
            else:
                match = re.search(r'--dagfile=(?:"([^"]+)"|(\S+))', line)
                relative = ((match[1] or match[2]) + ".json") if match else ""
            if not re.search(r"\sPlayer$", line) or not re.fullmatch(r"Library[/\\]Bee[/\\]Player[A-Za-z0-9_.-]+\.dag\.json", relative): return None
            if len(self.player_graphs) >= 32 and source not in self.player_graphs: self.player_graphs.clear()
            epoch = self.bee_epochs.get(source, 0) + 1; self.bee_epochs[source] = epoch
            graph_id = hashlib.sha256((source + ":" + relative + ":" + str(epoch)).encode()).hexdigest()[:20]
            coverage = self.import_coverage.get(source)
            profile_match = re.search(r'--profile=(?:"([^"]+)"|(\S+))', line)
            profile = (profile_match[1] or profile_match[2]) if profile_match else None
            self.player_graphs[source] = {"relative": relative, "id": graph_id, "owner": owner,
                "project": coverage.plan["project"] if coverage is not None else None, "snapshot": None,
                "attempted": 0, "profile": profile, "started": max(self.started, coverage.plan["createdAt"] if coverage is not None else 0),
                "exit": None, "ended": False}
            self.bee_current.pop(source, None)
            return self._unity_fields({"phase": "unity-bee-activity", "done": None, "total": None,
                "unit": "tasks", "status": "start", "detail": f"[bee-graph:{graph_id}] Player dependency graph started"}, source)
        graph = self.player_graphs.get(source)
        if not graph: return None
        if line.startswith("WorkingDir: "):
            graph["project"] = line[len("WorkingDir: "):]
            self._open_player_graph(graph)
        match = re.fullmatch(r"ExitCode: (\d+) Duration: .+", line)
        if match: graph["exit"] = int(match[1])
        match = re.fullmatch(r"\*\*\* Tundra (build success|build failed|requires additional run) \([\d.]+ seconds\), (\d+) items updated, (\d+) evaluated", line)
        if match:
            graph["ended"] = True
            snapshot = graph.get("snapshot")
            if graph.get("codegen") and graph["exit"] in (0, 4) and match[1] in ("build success", "requires additional run") \
                    and not (snapshot and snapshot.failed):
                self._queue_observation(self._conversion_complete(source, graph))
            if snapshot and snapshot.qualified:
                evaluated = int(match[3])
                if evaluated <= snapshot.total: snapshot.done = max(snapshot.done, evaluated)
                if match[1] == "build success" and graph["exit"] == 0 and not snapshot.failed and evaluated == snapshot.total:
                    return self._unity_fields(snapshot.fields(status="complete", detail=line), source)
                return self._unity_fields(snapshot.fields(status="failed" if match[1] == "build failed" or snapshot.failed else "progress", detail=line), source)
            current = self.bee_current.get(source)
            if current:
                if graph.get("nativeSeen"):
                    actual = dict(current)
                    if int(match[3]) <= actual["total"]: actual["done"] = max(actual["done"], int(match[3]))
                    terminal = match[1] == "build success" and graph["exit"] == 0 and actual["done"] == actual["total"]
                    return self._unity_fields(self._fallback_native_fields(graph, actual,
                        status="complete" if terminal else "failed" if match[1] == "build failed" else "progress", detail=line), source)
                fields = dict(current)
                fields.update(detail=current["detail"] + " · " + line,
                    status="complete" if match[1] == "build success" and graph["exit"] == 0 else "failed" if match[1] == "build failed" else "progress")
                return self._unity_fields(fields, source)
        return None

    @staticmethod
    def _open_player_graph(graph):
        if graph["snapshot"] is not None or graph["attempted"] >= 8 or not graph["project"]: return
        graph["attempted"] += 1
        try:
            graph["snapshot"] = PlayerBeePlan(graph["project"], graph["relative"], graph["id"],
                                              profile=graph["profile"], started=graph["started"])
        except FileNotFoundError: pass  # Publication can follow the first observed WorkingDir.
        except (OSError, ValueError, TypeError, UnicodeError, WizardError, AttributeError, MemoryError, RecursionError): graph["attempted"] = 8

    def poll_player_bee(self):
        """Current owned graph only; optional observation never stops a build."""
        values = []
        for source, graph in list(self.player_graphs.items()):
            if graph["ended"] or graph["owner"] in self.closed_operations or not graph["project"]: continue
            try:
                self._open_player_graph(graph)
                snapshot = graph["snapshot"]
                if snapshot:
                    fields = snapshot.read()
                    if snapshot.conversion_done:
                        complete = self._conversion_complete(source, graph)
                        if complete: values.append(complete)
                    if fields:
                        fields["operation"] = graph["owner"]
                        values.append(fields)
            except (OSError, ValueError, TypeError, UnicodeError, WizardError, AttributeError, MemoryError, RecursionError):
                # A missing/changing/oversized telemetry file does not justify
                # resetting Library, delaying Unity or rejecting an APK.
                graph["snapshot"] = None
        return values

    def _unity_operation(self, source):
        """Compiler logs belong to their actual child build, not Player import."""
        if re.fullmatch(r"mod-bundle(?:-launch)?-[A-Za-z0-9_.-]+\.log", source): return "mod-banks"
        if source.startswith(("update-code-unity", "update-sdk-unity")): return "update-code"
        if re.fullmatch(r"package-import(?:-launch)?-[A-Za-z0-9_.-]+\.log", source): return "unity-import"
        if re.fullmatch(r"package-api(?:-launch)?-[A-Za-z0-9_.-]+\.log", source): return "package-api"
        if re.fullmatch(r"content-pack-[A-Za-z0-9_.-]+\.log", source): return "content-bank"
        if source in self.unity_operations: return self.unity_operations[source]
        if re.fullmatch(r"unity-(?:build|launch)[A-Za-z0-9_.-]*\.log", source):
            if self.active_operation in ("unity-validation", "content-bank", "player"): return self.active_operation
            return "unity-import"
        return None

    def _unity_fields(self, fields, source):
        """Late child tails cannot reopen their successfully closed owner."""
        operation = fields.get("operation") or self._unity_operation(source)
        if operation in self.closed_operations: return None
        if operation: fields["operation"] = operation
        return fields

    @staticmethod
    def _unity_child(source):
        return re.fullmatch(r"(?:package-(?:import|api)|content-pack|unity-(?:build|launch)|mod-bundle|update-(?:code|sdk)-unity)"
                            r"[A-Za-z0-9_.-]*\.log", source) is not None

    def observe_plan(self, path, *, started=0):
        """Bind a fresh log to its source-backed inventory before parsing it."""
        try:
            work = _unity_work_module()
            control = work.sidecar(path)
            if not control.is_file() or control.is_symlink(): return None
            stat = control.stat()
            signature = (stat.st_dev, stat.st_ino, stat.st_mtime_ns, stat.st_size)
            source = Path(path).name
            if self.plan_signatures.get(source) == signature and source in self.import_coverage: return None
            plan = work.load_plan(path, started=started)
            if plan is None: return None
            coverage = self.coverage_invocations.get(plan["invocationId"])
            if coverage is None:
                if len(self.coverage_invocations) >= 32:
                    self.close_coverage()
                    self.import_coverage.clear(); self.coverage_invocations.clear(); self.plan_signatures.clear()
                coverage = work.Coverage(plan)
                self.coverage_invocations[plan["invocationId"]] = coverage
            previous = self.import_coverage.get(source)
            if previous is not coverage:
                owner = self._unity_operation(source)
                self.gradle_counters.pop(owner, None)
                self.gradle_epochs.pop(owner, None)
                self.gradle_retired_epochs.pop(owner, None)
                self.import_runs.pop(source, None); self.import_pending.pop(source, None)
                self.native_import_counts.pop(source, None)
                for key in list(self.shader_coverage):
                    if key[0] == source: self.shader_coverage.pop(key, None)
            self.import_coverage[source] = coverage
            self.plan_signatures[source] = signature
            return self._unity_fields({"phase": "unity-work-plan", "done": coverage.done, "total": plan["total"],
                    "unit": "assets", "detail": "[asset-coverage] Known project/package assets: " +
                    str(coverage.done) + " / " + str(plan["total"]), "status": "start"}, source)
        except (OSError, ValueError, TypeError, sqlite3.Error, ImportError, AttributeError):
            return None  # Optional observation cannot fail an owned build.

    def flush_coverage(self):
        for coverage in self.coverage_invocations.values():
            try: coverage.flush()
            except (OSError, ValueError, sqlite3.Error): pass

    def close_coverage(self):
        for coverage in self.coverage_invocations.values():
            try: coverage.close()
            except (OSError, ValueError, sqlite3.Error): pass

    def _import_fields(self, source, detail, *, phase="unity-import-activity"):
        native = self.native_import_counts.get(source)
        coverage = self.import_coverage.get(source)
        if native is not None:
            done, total = native
            detail = "[unity-native-total] " + detail
        elif coverage is not None:
            done, total = coverage.done, coverage.plan["total"]
            detail = "[asset-coverage] Known project/package assets: " + str(done) + " / " + str(total) + " · " + detail
        else:
            done, total = self.import_runs.get(source, 0), None
        return self._unity_fields({"phase": phase, "done": done, "total": total,
                                  "unit": "assets", "detail": detail[:1024], "status": "progress"}, source)

    def _import_progress(self, line, source):
        if self._unity_operation(source) in self.closed_operations: return None
        # Artifact + duration proves one call returned. Keep the full logical
        # path so reimports, duplicate basenames and interleaved suffixes cannot
        # inflate the fixed inventory's completed coverage.
        start = re.match(r"Start importing (.+?) using Guid\([0-9a-f]{32}\) Importer\([^)]*\)", line)
        if len(self.import_runs) >= 64 and source not in self.import_runs:
            self.import_runs.clear(); self.import_pending.clear()
        if start: self.import_pending[source] = start[1].replace("\\", "/")[:4096]
        match = re.search(r"-> \(artifact id: '[0-9a-f]{32}'\) in (\d+(?:\.\d+)?) seconds$", line)
        if match and source in self.import_pending:
            duration = float(match[1])
            if not math.isfinite(duration): return None
            self.import_runs[source] = self.import_runs.get(source, 0) + 1
            asset = self.import_pending.pop(source)
            coverage = self.import_coverage.get(source)
            if coverage is not None:
                try: coverage.observe(asset)
                except (OSError, ValueError, sqlite3.Error): pass
            detail = "last: " + asset + " · last import: " + str(duration) + " s"
            if coverage is None and source not in self.native_import_counts:
                detail = "Completed asset imports: " + str(self.import_runs[source]) + " · " + detail
            return self._import_fields(source, detail, phase="unity-asset-import")
        if line.startswith("Start importing "):
            return self._import_fields(source, line)
        return None

    def _shader_fields(self, source, run, *, failed=False):
        current = run["current"]
        census = self.shader_coverage.get((source, self._unity_operation(source)))
        source_scope = ("outside" if not census or current["name"] not in census["names"] else
                        "observed" if current["name"] in census["observed"] else "unseen")
        detail = ("[shader-pass:" + str(run["started"]) + "] [shader-source:" + source_scope + "] Unity shader compilation: " + current["name"] + " / " + (current["pass"] or "unnamed pass") +
                  " · pass #" + str(run["started"]) + " · completed passes " + str(run["passes"]) +
                  " · completed variants " + str(run["variants"]))
        if failed: detail += " · compiler reported an error; this pass is incomplete"
        fields = {"phase": "unity-shader-compile", "done": current["done"], "total": current["total"],
                  "unit": "variants", "detail": detail[:1024], "status": "failed" if failed else "progress"}
        operation = self._unity_operation(source)
        if operation: fields["operation"] = operation
        return fields

    def _shader_progress(self, line, source):
        # Unity 2021.3 writes the retained variant total before scheduling a
        # pass, and a real ready counter each minute for a long compilation.
        # The 123047 capture contains 4903/12288 and 9467/12288, followed by
        # 12288 actual outputs. Stripping is work scheduled, not work completed.
        # One pass reaching its total never closes its owning bank/Player build.
        match = re.fullmatch(r'Compiling shader "([^\"]+)" pass "([^\"]*)" \(([^)]+)\)', line)
        if match:
            if len(self.shader_runs) >= 64 and source not in self.shader_runs: self.shader_runs.clear()
            owner = self._unity_operation(source)
            run = self.shader_runs.setdefault(source, {"owner": owner, "started": 0, "passes": 0, "variants": 0, "current": None})
            if run["owner"] != owner:
                run.update(owner=owner, started=0, passes=0, variants=0)
            run["started"] += 1
            run["current"] = {"name": match[1], "pass": match[2], "done": None, "total": None, "closed": False}
            self._queue_observation(self._shader_coverage(source))
            return self._shader_fields(source, run)
        run = self.shader_runs.get(source)
        if not run or not run["current"] or run["current"]["closed"]: return None
        current = run["current"]
        match = re.fullmatch(r"(\d+)\s*/\s*(\d+) variants left after stripping, processed in [\d.]+ seconds", line)
        if match:
            retained, original = int(match[1]), int(match[2])
            if retained > original or current["total"] is not None: return None
            current.update(done=0, total=retained)
            return self._shader_fields(source, run)
        match = re.fullmatch(r"\[\s*\d+(?:\.\d+)?s\]\s*(\d+)\s*/\s*(\d+) variants ready", line)
        if match:
            done, total = int(match[1]), int(match[2])
            if (not total or done > total or current["total"] != total or
                    current["done"] is None or done < current["done"]): return None
            current["done"] = done
            return self._shader_fields(source, run)
        match = re.fullmatch(r"finished in [\d.]+ seconds\. Local cache hits (\d+) \([^)]*\), "
                             r"remote cache hits (\d+) \([^)]*\), compiled (\d+) variants \([^)]*\), skipped (\d+) variants", line)
        if match:
            done = sum(map(int, match.groups()))
            if current["total"] is None or done != current["total"]: return None
            current.update(done=done, closed=True)
            run["passes"] += 1; run["variants"] += done
            fields = self._shader_fields(source, run)
            fields["detail"] += " · pass finished"
            # Only this explicitly scoped pass closes. Unity may still compile
            # other passes, serialize a bank or fail in a later native action.
            fields["status"] = "complete"
            self._queue_observation(self._shader_coverage(source, finished=current["name"]))
            return fields
        if re.search(r"^(?:Shader error in |Error compiling shader|Shader compiler (?:process )?(?:crashed|failed))", line, re.I):
            fields = self._shader_fields(source, run, failed=True)
            current["closed"] = True
            return fields
        return None

    def parse(self, line, source="tool"):
        line = line.strip()
        if line.startswith("resources: "):
            try:
                value = json.loads(line[len("resources: "):])
                if not isinstance(value, dict) or value.get("phase") != "il2cpp": return None
                from failures import memory_resources
                capacity = memory_resources(value)
                if value.get("nativeLaunchAllowed") is not False:
                    # A successful scheduling observation may occur inside an
                    # update-code job. It cannot detach that running owner.
                    self.memory_rejections.discard(source)
                    return None
                if len(self.memory_rejections) >= 64: self.memory_rejections.clear()
                self.memory_rejections.add(source)
                detail = "Native compiler memory check: " + "; ".join(
                    name + "=" + str(number) for name, number in capacity.items())
                # This capacity observation has no known work denominator and
                # cannot reopen the completed weave/preparation operation.
                return {"phase": "native-memory-check", "done": None, "total": None,
                        "unit": None, "detail": detail[:1024], "status": "failed"}
            except (ValueError, TypeError): return None
        if line.startswith("GHVRQ_PROGRESS "):
            try:
                value = json.loads(line[15:])
                if not isinstance(value, dict) or value.get("schema") != 1: return None
                if value.get("phase") in ("stage:build", "builder-python-handoff") and value.get("status") == "failed" and source in self.memory_rejections:
                    # Generic stage/launcher exit summaries must not conceal
                    # the specific pre-launch memory rejection just emitted.
                    return None
                if value.get("phase") == "native-memory-check" and value.get("status") == "failed":
                    if len(self.memory_rejections) >= 64: self.memory_rejections.clear()
                    self.memory_rejections.add(source)
                fields = {name: value.get(name) for name in ("phase", "done", "total", "unit", "detail")}
                stage_progress(**fields)  # The same strict bounds apply at ingestion.
                if value.get("operation") is not None:
                    if not isinstance(value["operation"], str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", value["operation"]): return None
                    fields["operation"] = value["operation"]
                if value.get("status") is not None:
                    if value["status"] not in ("start", "progress", "complete", "reuse", "failed"): return None
                    fields["status"] = value["status"]
                if fields["phase"] == "unity-gradle-tasks" and type(fields.get("total")) is int and fields["total"] > 0:
                    owner = fields.get("operation") or self._unity_operation(source)
                    if owner in self.closed_operations: return None
                    counter = self.gradle_counters.get(owner)
                    marker = re.search(r"\[gradle-graph:([A-Za-z0-9-]{1,64})\]", fields.get("detail") or "")
                    epoch = marker[1] if marker else None
                    previous_epoch = self.gradle_epochs.get(owner)
                    retired = self.gradle_retired_epochs.setdefault(owner, set())
                    if epoch and epoch in retired: return None
                    changed = epoch and previous_epoch and epoch != previous_epoch
                    if changed:
                        if fields.get("status") != "start": return None
                        retired.add(previous_epoch)
                        if len(retired) > 32: return None
                    # Buffered output may echo 0/N after the live sidecar reached
                    # 5/N. Only an actual new graph identity can reset that child.
                    if counter and not changed and fields.get("done", 0) < counter["done"]: return None
                    if epoch: self.gradle_epochs[owner] = epoch
                    self.gradle_counters[owner] = dict(fields)
                if fields["phase"] in MEMORY_OBSERVATIONS:
                    # Capacity, retries and resource waits describe a strategy,
                    # not work completed. Never let a reported 1/1 close Player.
                    fields.update(done=None, total=None, unit=None)
                    fields.pop("operation", None)
                    memory = native_memory_progress(value.get("nativeMemory"))
                    if memory: fields["nativeMemory"] = memory
                    self.memory_rejections.discard(source)
                # Public Editor task counters can be observed before/after the
                # native compiler log. Preserve their supplied fraction and
                # distinguish them from one native shader pass's variants.
                if fields["phase"].startswith(("unity-progress", "unity-shader-task")):
                    detail = fields.get("detail") or ""
                    task_name = detail.split(" — ", 1)[0]
                    shader_task = re.search(r"shader.*compil|compil.*shader", task_name, re.I)
                    if fields["phase"].startswith("unity-shader-task") or shader_task:
                        fields["phase"] = fields["phase"].replace("unity-progress", "unity-shader-task", 1)
                        if "operation" not in fields:
                            operation = self._unity_operation(source)
                            if operation: fields["operation"] = operation
                if fields.get("operation") and fields["phase"] == "operation:" + fields["operation"]:
                    # Only the runner can reopen a finished owner. Old Editor
                    # logs may still contain an unread boundary from that job.
                    owner = fields["operation"]
                    if self._unity_child(source) and owner in self.closed_operations: return None
                    if fields.get("status") in ("complete", "reuse"):
                        self.closed_operations.add(owner)
                    elif fields.get("status") == "start":
                        self.closed_operations.discard(owner)
                        self.active_operation = owner
                    self.unity_operations[source] = fields["operation"]
                elif fields["phase"].startswith("unity-") or self._unity_child(source):
                    return self._unity_fields(fields, source)
                return fields
            except (ValueError, TypeError, WizardError): return None
        shader = self._shader_progress(line, source)
        if shader is not None: return self._unity_fields(shader, source)
        boundary = self._player_bee_boundary(line, source)
        if boundary is not None: return boundary
        match = re.fullmatch(r"\[\s*(\d+)/(\d+)\s+\d+(?:\.\d+)?s\]\s+(" + BEE_ACTIONS + r")\b(.*)", line)
        if match:
            done, total = int(match[1]), int(match[2])
            if not total or done > total: return None
            previous, generation = self.bee_runs.get((source, total), (-1, 0))
            if done < previous: generation += 1
            self.bee_runs[(source, total)] = done, generation
            annotation = match[3] + match[4]
            graph = self.player_graphs.get(source)
            scope = _bee_scope(annotation)
            metadata = f"[bee-scope:{scope}] "
            if graph: metadata += f"[bee-graph:{graph['id']}] "
            fields = {"phase": f"bee-actions:{source}:{total}:{generation}"[:160], "done": done, "total": total,
                      "unit": "actions", "detail": (metadata + annotation)[:1024]}
            self.bee_current[source] = fields
            if match[3] == "IL2CPP_CodeGen" and self._unity_operation(source) in ("player", "update-code"):
                if graph: graph["codegen"] = True
                return self._unity_fields({"phase": "unity-il2cpp", "done": 1, "total": 2,
                    "unit": "tasks", "status": "progress", "detail": (metadata + "Managed code conversion action returned; result pending · " + annotation)[:1024]}, source)
            snapshot = graph.get("snapshot") if graph else None
            if snapshot and snapshot.qualified and total == snapshot.total and done >= snapshot.done:
                snapshot.done = done
                return self._unity_fields(snapshot.fields(detail=annotation), source)
            if graph:
                if _BEE_COMPILER.match(annotation): graph["nativeSeen"] = True
                if graph.get("nativeSeen"):
                    return self._unity_fields(self._fallback_native_fields(graph, fields, detail=annotation), source)
            return self._unity_fields(dict(fields), source)
        match = re.fullmatch(r"\[BUSY\s+(\d+(?:\.\d+)?)s\]\s+(" + BEE_ACTIONS + r")\b(.*)", line)
        if match:
            fields = dict(self.bee_current.get(source, {"phase": "unity-bee-activity", "done": None,
                                                       "total": None, "unit": "actions"}))
            # BUSY is a real activity heartbeat, not a newly completed action.
            # Preserve the last measured DAG counter and its generation.
            graph = self.player_graphs.get(source)
            metadata = f"[bee-scope:{_bee_scope(match[2] + match[3])}] "
            if graph: metadata += f"[bee-graph:{graph['id']}] "
            fields.update(detail=(metadata + "Active compiler action: " + match[2] + match[3] + " · elapsed: " + match[1] + " s")[:1024],
                          status="progress")
            snapshot = graph.get("snapshot") if graph else None
            if snapshot and snapshot.qualified:
                fields = snapshot.fields(detail=fields["detail"])
            elif graph and graph.get("nativeSeen"):
                fields = self._fallback_native_fields(graph, fields, detail=fields["detail"])
                if fields is None: return None
            return self._unity_fields(fields, source)
        if self._unity_child(source):
            match = re.fullmatch(r"> Task (:[A-Za-z0-9_:.\-]+)(?: (UP-TO-DATE|FROM-CACHE|NO-SOURCE|SKIPPED|FAILED))?", line)
            if match:
                if len(self.gradle_runs) >= 64 and source not in self.gradle_runs: self.gradle_runs.clear()
                tasks = self.gradle_runs.setdefault(source, set())
                tasks.add(match[1])
                owner = self._unity_operation(source)
                measured = self.gradle_counters.get(owner)
                if measured:
                    fields = dict(measured)
                    fields.update(detail="Gradle completed tasks: " + str(fields["done"]) + " / " +
                        str(fields["total"]) + " · " + line, status="progress")
                    return self._unity_fields(fields, source)
                # Gradle's plain task messages have no full graph denominator
                # and do not prove execution versus cache reuse. Count reported
                # task identities, keep the actual status and never guess ETA.
                return self._unity_fields({"phase": "unity-gradle-tasks", "done": len(tasks), "total": None,
                    "unit": "tasks", "detail": "Gradle tasks reported: " + str(len(tasks)) + " · " + line,
                    "status": "progress"}, source)
            match = re.fullmatch(r"(\d+) actionable tasks?: ((?:\d+ (?:executed|up-to-date|from cache)(?:, )?)+)", line)
            if match:
                total = int(match[1]); observed = sum(int(number) for number in re.findall(r"\d+", match[2]))
                if total and total == observed:
                    measured = self.gradle_counters.get(self._unity_operation(source))
                    if measured:
                        fields = dict(measured); fields.update(detail=line, status="progress")
                        return self._unity_fields(fields, source)
                    fields = {"phase": "unity-gradle-tasks", "done": total, "total": total,
                              "unit": "tasks", "detail": line, "status": "progress"}
                    self.gradle_counters[self._unity_operation(source)] = dict(fields)
                    return self._unity_fields(fields, source)
        match = re.fullmatch(r"Installed content files: (\d+)/(\d+)\.", line)
        if match and 0 < int(match[2]) >= int(match[1]):
            return {"phase": "install-content", "done": int(match[1]), "total": int(match[2]), "unit": "files", "detail": line}
        if re.fullmatch(r"Confirmed installed build: B\d+ \(input [0-9a-f]{12}\)", line):
            return {"phase": "operation:apk", "done": 1, "total": 1, "unit": "operations", "detail": line, "operation": "apk", "status": "complete"}
        preparing_content = re.fullmatch(r"Preparing \d+ changed files on the PC; \d+ files remain installed\.", line)
        if line.startswith("Installing complete Campaign content:") or preparing_content or line == "Reusing the verified Campaign content already on this Quest.":
            return {"phase": "operation:content", "done": None, "total": None, "unit": None, "detail": line[:1024], "operation": "content", "status": "start"}
        # The Editor writes real import counters and lifecycle milestones even
        # before our source has compiled. No total is invented for a single asset.
        match = re.search(r"(?:Importing|Imported)\s+(\d+)\s+(?:assets|files)\s+(?:of|/)\s*(\d+)", line, re.I)
        if match and 0 < int(match[2]) >= int(match[1]):
            self.native_import_counts[source] = (int(match[1]), int(match[2]))
            return self._unity_fields({"phase": "unity-asset-import", "done": int(match[1]), "total": int(match[2]), "unit": "assets", "detail": "[unity-native-total] " + line[:1000]}, source)
        importing = self._import_progress(line, source)
        if importing is not None: return importing
        milestones = ((r"^\[Package Manager\].*(?:Resolving|Registering|Installing)", "unity-packages"),
                      (r"^Begin MonoManager ReloadAssembly|^Reloading assemblies", "unity-domain"),
                      (r"^Compiling (?:shader|compute)|^Shader compiler", "unity-shaders"),
                      (r"^Invoking il2cpp|^Converting managed assemblies|^IL2CPP", "unity-il2cpp"),
                      (r"^Building Gradle project|^Starting a Gradle Daemon|^> Task :", "unity-gradle"),
                      (r"^Asset Pipeline Refresh", "unity-refresh"))
        for pattern, phase in milestones:
            if re.search(pattern, line):
                if re.fullmatch(r"package-import(?:-launch)?-[A-Za-z0-9_.-]+\.log", source):
                    return self._import_fields(source, line)
                return self._unity_fields({"phase": phase, "done": None, "total": None, "unit": None, "detail": line[:1024]}, source)
        match = re.search(r"(?:Receiving objects|Resolving deltas|Counting objects):\s*\d+%\s*\((\d+)/(\d+)\)", line)
        if match and 0 < int(match[2]) >= int(match[1]):
            return {"phase": "git-transfer:" + source[:100], "done": int(match[1]), "total": int(match[2]), "unit": "objects", "detail": line[:1024]}
        match = re.match(r"^(inspect|snapshot|prepare|graphics|compute|build|package|install):\s+(.+)", line)
        if match:
            return {"phase": match[1], "done": None, "total": None, "unit": None, "detail": match[2][:1024]}
        return None


class LogTail:
    """Incremental bounded reads preserve whole child logs without pipe backpressure."""
    def __init__(self, path):
        self.path, self.offset, self.pending, self.identity = ordinary(path), 0, b"", None

    def read(self, final=False):
        if not self.path.is_file(): return []
        stat = self.path.stat(); identity = (stat.st_dev, stat.st_ino)
        if self.identity != identity or stat.st_size < self.offset:
            self.offset, self.pending, self.identity = 0, b"", identity
        with self.path.open("rb") as stream:
            stream.seek(self.offset); block = stream.read(128 * 1024); self.offset += len(block)
        data = self.pending + block
        lines = re.split(b"[\r\n]", data)
        self.pending = lines.pop()[-65536:]
        if final and self.offset == stat.st_size and self.pending:
            lines.append(self.pending); self.pending = b""
        return [row[:65536].decode("utf-8", errors="replace") for row in lines]


class WindowsJob:
    """Attach suspended children before they can spawn outside their Job Object."""
    CREATE_SUSPENDED = 4
    CREATE_NEW_PROCESS_GROUP = 0x200

    def __init__(self):
        from ctypes import wintypes as w
        self.k = ctypes.WinDLL("kernel32", use_last_error=True)
        self.k.CreateJobObjectW.argtypes = [ctypes.c_void_p, w.LPCWSTR]
        self.k.CreateJobObjectW.restype = w.HANDLE
        self.k.CloseHandle.argtypes = [w.HANDLE]
        self.k.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        self.k.SetInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]
        self.k.TerminateJobObject.argtypes = [w.HANDLE, w.UINT]
        class Basic(ctypes.Structure):
            _fields_ = [("user", ctypes.c_int64), ("job", ctypes.c_int64), ("flags", w.DWORD),
                        ("minimum", ctypes.c_size_t), ("maximum", ctypes.c_size_t), ("active", w.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
        class IO(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in ("readOps", "writeOps", "otherOps", "readBytes", "writeBytes", "otherBytes")]
        class Limits(ctypes.Structure):
            _fields_ = [("basic", Basic), ("io", IO), ("processMemory", ctypes.c_size_t),
                        ("jobMemory", ctypes.c_size_t), ("peakProcess", ctypes.c_size_t), ("peakJob", ctypes.c_size_t)]
        self.handle = self.k.CreateJobObjectW(None, None)
        if not self.handle: self.fail("create_job")
        limits = Limits(); limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE
        if not self.k.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            self.close(); self.fail("configure_job")

    def fail(self, code):
        raise WizardError(code, "Windows could not supervise the owned build process.", "Windows konnte den Build-Prozess nicht kontrollieren.", winError=ctypes.get_last_error())

    def attach_resume(self, process):
        from ctypes import wintypes as w
        if not self.k.AssignProcessToJobObject(self.handle, int(process._handle)):
            process.kill(); process.wait(); self.fail("assign_job")
        # Popen closes the primary thread HANDLE. Resume its exact suspended
        # thread through the documented Toolhelp/OpenThread APIs, never a PID-only kill.
        class Thread(ctypes.Structure):
            _fields_ = [("size", w.DWORD), ("usage", w.DWORD), ("id", w.DWORD), ("owner", w.DWORD),
                        ("basePriority", w.LONG), ("deltaPriority", w.LONG), ("flags", w.DWORD)]
        self.k.CreateToolhelp32Snapshot.argtypes = [w.DWORD, w.DWORD]
        self.k.CreateToolhelp32Snapshot.restype = w.HANDLE
        self.k.Thread32First.argtypes = [w.HANDLE, ctypes.POINTER(Thread)]
        self.k.Thread32Next.argtypes = [w.HANDLE, ctypes.POINTER(Thread)]
        self.k.OpenThread.argtypes = [w.DWORD, w.BOOL, w.DWORD]; self.k.OpenThread.restype = w.HANDLE
        self.k.ResumeThread.argtypes = [w.HANDLE]; self.k.ResumeThread.restype = w.DWORD
        snapshot = self.k.CreateToolhelp32Snapshot(4, 0)
        if snapshot == ctypes.c_void_p(-1).value: self.fail("thread_snapshot")
        item = Thread(); item.size = ctypes.sizeof(item)
        resumed = False
        try:
            present = self.k.Thread32First(snapshot, ctypes.byref(item))
            while present:
                if item.owner == process.pid:
                    thread = self.k.OpenThread(2, False, item.id)
                    if thread:
                        try: resumed = self.k.ResumeThread(thread) != 0xFFFFFFFF
                        finally: self.k.CloseHandle(thread)
                    break
                present = self.k.Thread32Next(snapshot, ctypes.byref(item))
        finally: self.k.CloseHandle(snapshot)
        if not resumed: self.kill(); process.wait(); self.fail("resume_child")

    def kill(self):
        if self.handle and not self.k.TerminateJobObject(self.handle, 130): self.fail("terminate_job")
        if self.handle:
            from ctypes import wintypes as w
            class Accounting(ctypes.Structure):
                _fields_ = [(name, ctypes.c_int64) for name in ("user", "kernel", "periodUser", "periodKernel")] + [
                    (name, w.DWORD) for name in ("faults", "total", "active", "terminated")]
            self.k.QueryInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD, ctypes.c_void_p]
            deadline = time.monotonic() + 10
            while True:
                data = Accounting()
                if not self.k.QueryInformationJobObject(self.handle, 1, ctypes.byref(data), ctypes.sizeof(data), None): self.fail("query_job")
                if data.active == 0: break
                if time.monotonic() >= deadline: self.fail("job_shutdown_timeout")
                time.sleep(0.05)

    def close(self):
        if self.handle: self.k.CloseHandle(self.handle); self.handle = None


def process_identity(process):
    if os.name == "nt":
        from ctypes import wintypes as w
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.GetProcessTimes.argtypes = [w.HANDLE, *[ctypes.POINTER(w.FILETIME)] * 4]
        times = [w.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(int(process._handle), *[ctypes.byref(x) for x in times]):
            raise WizardError("process_identity", "Could not record the owned process creation time.")
        start = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
    else:
        # Linux's stable start ticks are evidence only, never authorization to
        # kill a later process. Cancellation uses the owned Popen/group HANDLE.
        stat = Path("/proc") / str(process.pid) / "stat"
        start = stat.read_text().rsplit(")", 1)[1].split()[19] if stat.is_file() else time.time_ns()
    return {"pid": process.pid, "creation": str(start)}


class Supervisor:
    def __init__(self, store, session, *, grace=5, poll=0.1, job_factory=WindowsJob):
        self.store, self.session, self.grace, self.poll = store, session, grace, poll
        self.job_factory = job_factory
        self.stage = None

    def set_stage(self, stage):
        if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
        self.stage = stage

    def _tail_progress(self, tails, parser, started, *, final=False):
        if self.stage in ("inspect", "build"):
            folder = ordinary(self.store.root / "build/logs")
            if folder.is_dir():
                # Only known logs generated by this attempt are eligible. Old
                # completed logs must not overwrite live stage counters.
                candidates = sorted((entry for entry in folder.iterdir()
                                     if re.fullmatch(r"(?:package-(?:import|api)|content-pack|unity-(?:build|launch)|update-(?:code|sdk)-unity|recovery|recover|dotnet|mod|weave)[A-Za-z0-9_.-]*\.log", entry.name)
                                     # A memory retry renames this run's old
                                     # log without changing its mtime. It stays
                                     # available for support, but reopening it
                                     # here would replay an earlier compiler.
                                     and not re.search(r"\.memory-attempt-[1-9][0-9]*\.log$", entry.name)
                                     and entry.is_file() and not entry.is_symlink() and entry.stat().st_mtime >= started),
                                    key=lambda entry: entry.stat().st_mtime, reverse=True)[:15]
                # Gradle's normal Unity stdout can be buffered until process exit.
                # The pinned Editor callback writes an unbuffered owned sidecar.
                # A retained sidecar is eligible only under its live native-log
                # invocation plan, never because an old file was merely touched.
                additions = []
                for native in candidates:
                    if not parser._unity_child(native.name): continue
                    side = Path(str(native) + ".gradle-progress.jsonl")
                    if side.is_file() and not side.is_symlink() and side.stat().st_mtime >= started:
                        try:
                            work = _unity_work_module(); plan = work.load_plan(native, started=started)
                            if plan and side.stat().st_mtime >= plan["createdAt"]:
                                additions.append(side)
                        except (OSError, ValueError, TypeError, ImportError): pass
                candidates += additions
                for path in list(tails):
                    if path.parent == folder and path not in candidates: del tails[path]
                for entry in candidates: tails.setdefault(entry, LogTail(entry))
        for tail in list(tails.values())[:32]:
            planned = parser.observe_plan(tail.path, started=started)
            if planned and self.stage:
                if planned.get("operation") not in PLANS[self.stage]: planned.pop("operation", None)
                self.store.progress(self.session, self.stage, **planned)
            pending_import = None
            for line in tail.read(final=final):
                fields = parser.parse(line, tail.path.name)
                observations = parser.take_observations()
                # Child counters and coverage describe separate measured
                # scopes. Deliver both without hiding the native log event.
                for observation in observations:
                    if self.stage:
                        if observation.get("operation") not in PLANS[self.stage]: observation.pop("operation", None)
                        self.store.progress(self.session, self.stage, **observation)
                if fields and self.stage:
                    # Observation is additive. A newer selected source or a
                    # nested tool can emit another workflow's operation code;
                    # retain its raw task detail without letting it skip this
                    # stage's plan or turn logging into a build failure.
                    if fields.get("operation") not in PLANS[self.stage]: fields.pop("operation", None)
                    if fields["phase"] in ("unity-asset-import", "unity-import-activity"):
                        # A cold Editor imports 146903 assets in the measured
                        # witness. Parse every completion, but publish the latest
                        # count in each bounded tail read instead of rebuilding
                        # the entire UI state for every asset in that read.
                        pending_import = fields
                        continue
                    if pending_import:
                        self.store.progress(self.session, self.stage, **pending_import)
                        pending_import = None
                    self.store.progress(self.session, self.stage, **fields)
            if pending_import: self.store.progress(self.session, self.stage, **pending_import)
        if self.stage:
            for fields in parser.poll_player_bee():
                if fields.get("operation") not in PLANS[self.stage]: fields.pop("operation", None)
                self.store.progress(self.session, self.stage, **fields)
        parser.flush_coverage()

    def run(self, argv, log, *, cwd=None, env=None, timeout=None, on_started=None, on_poll=None, acceptable_codes=(0,)):
        self.store.check_cancel(self.session)
        if self.stage in ("inspect", "build"):
            from qualification import check_runtime_space, SPACE_CHECK_SECONDS
            check_runtime_space(self.store.root)
        if timeout is not None and (type(timeout) not in (int, float) or not math.isfinite(timeout) or timeout <= 0):
            raise WizardError("invalid_timeout", "Process timeout must be positive.")
        log = Path(log); log.parent.mkdir(parents=True, exist_ok=True)
        job = self.job_factory() if os.name == "nt" else None
        options = {"creationflags": WindowsJob.CREATE_SUSPENDED | WindowsJob.CREATE_NEW_PROCESS_GROUP} if job else {"start_new_session": True}
        process = None; started = time.monotonic(); started_wall = time.time()
        next_space_check = started + SPACE_CHECK_SECONDS if self.stage in ("inspect", "build") else None
        tails = {log: LogTail(log)}; parser = ProgressParser(started=started_wall); controlled_stop = False
        try:
            with log.open("wb") as stream:
                process = subprocess.Popen(list(map(str, argv)), cwd=cwd, env=env,
                                           stdout=stream, stderr=subprocess.STDOUT, **options)
                identity = process_identity(process)
                atomic_json(self.store.session_dir(self.session) / "child.json", {"schema": 1, **identity, "session": self.session})
                if job: job.attach_resume(process)
                self.store.record(self.session, "process_started", self.stage, executable=Path(argv[0]).name, command=diagnostic_command(argv), log=log.name)
                if on_started: on_started(process)
                while process.poll() is None:
                    try: self.store.check_cancel(self.session)
                    except Cancelled:
                        self.stop(process, job)
                        raise
                    self._tail_progress(tails, parser, started_wall)
                    if self.stage in ("inspect", "build") and time.monotonic() >= next_space_check:
                        from qualification import check_runtime_space, SPACE_CHECK_SECONDS
                        check_runtime_space(self.store.root)
                        next_space_check = time.monotonic() + SPACE_CHECK_SECONDS
                    if timeout is not None and time.monotonic() - started >= timeout:
                        raise WizardError("child_timeout", "A required tool exceeded its bounded wait; retry the displayed action.",
                                          "Ein benötigtes Werkzeug hat die Wartezeit überschritten; die angezeigte Aktion erneut ausführen.",
                                          executable=Path(argv[0]).name, durationSeconds=round(time.monotonic() - started, 3), log=str(log))
                    if on_poll and on_poll(process) is False:
                        self.stop(process, job); controlled_stop = True; break
                    time.sleep(self.poll)
                code = process.wait()
                self._tail_progress(tails, parser, started_wall, final=True)
                if code not in acceptable_codes and not controlled_stop:
                    from failures import tool_failure
                    raise tool_failure(self.store.root, self.stage, log, started_wall, Path(argv[0]).name, code)
                return 0 if controlled_stop else code
        finally:
            parser.close_coverage()
            if process is not None:
                if process.poll() is None: self.stop(process, job)
                elif job: job.kill()
            if job: job.close()
            (self.store.session_dir(self.session) / "child.json").unlink(missing_ok=True)
            if process is not None:
                self.store.record(self.session, "process_finished", self.stage, executable=Path(argv[0]).name,
                                  exitCode=process.returncode, durationSeconds=round(time.monotonic() - started, 3), controlledStop=controlled_stop, log=log.name)

    def stop(self, process, job):
        if process.poll() is None:
            try:
                if job: process.send_signal(signal.CTRL_BREAK_EVENT)
                else: os.killpg(process.pid, signal.SIGTERM)
            except (OSError, ProcessLookupError): pass
        deadline = time.monotonic() + self.grace
        while process.poll() is None and time.monotonic() < deadline: time.sleep(self.poll)
        # Kill the entire owned group even if its parent exited gracefully:
        # the parent's compiler descendants may still have inherited open files.
        if job:
            job.kill()
            # Covers a failure before AssignProcessToJobObject: only this exact
            # Popen HANDLE is terminated, never a PID loaded from persisted JSON.
            if process.poll() is None: process.kill()
        else:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        process.wait()
