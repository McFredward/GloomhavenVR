"""Finite per-run ownership graph used by the single read-only size walk.

Measurements describe logical bytes, not a deletion authorization. Shared files
are counted once globally and listed for each dependent run. Unknown ownership
remains visible as unassigned storage instead of inventing a deletable owner.
"""
from __future__ import annotations

from state import Cancelled, WizardError

PROJECT_AREAS = ("project-lifecycle", "prepare-resume", "project-content-transactions", "preparation-repair")
INTERMEDIATE_CATEGORIES = frozenset(("inputs", "converted-assets", "unity-projects", "unity-caches"))
STOPPED = frozenset(("ready", "failed", "blocked", "cancelled", "interrupted", "complete"))


class Ownership:
    def __init__(self, helper, root, build, cancel=None, *, progress=None):
        self.helper, self.root, self.build, self.cancel = helper, root, build, cancel
        self.progress = progress
        self.claims = {}; self.rows = {}; self.potential = {}; self.blocked = set()
        self.retained = set(); self.warnings = []; self.complete = True; self.expected_files = {}; self.project_groups = {}
        self.exclusive_bytes = self.shared_bytes = self.unassigned_bytes = 0
        self.reusable_bytes = self.deliverable_bytes = self.tool_bytes = self.diagnostic_bytes = 0
        self.fast_update_bytes = 0
        self._build()

    def check_cancel(self):
        self.helper._check_cancel(self.cancel)

    def report(self, path):
        self.check_cancel()
        self.helper._notify(self.progress, "storage-ownership", len(self.rows), None,
                            path.relative_to(self.root).as_posix(), files=0, bytes=0, folders=0,
                            currentPath=path.relative_to(self.root).as_posix())

    def warn(self, code, path):
        self.complete = False
        if len(self.warnings) < 32:
            self.warnings.append({"code": code, "path": str(path.relative_to(self.root))[:1024]})

    def claim(self, path, session, *, intermediate=False, reclaimable=False):
        if path == self.root or self.root not in path.parents: return
        row = self.claims.setdefault(path, {"owners": set(), "intermediate": False, "reclaimable": False})
        row["owners"].add(session)
        row["intermediate"] |= intermediate; row["reclaimable"] |= reclaimable

    def _folders(self, parent):
        if not parent.is_dir(): return []
        self.helper._stamp(parent)
        result = []
        for child in sorted(parent.iterdir(), key=lambda path: path.name):
            self.check_cancel()
            if not self.helper.SESSION.fullmatch(child.name): continue
            if len(result) >= self.helper.MAX_IDENTITIES:
                self.warn("ownership_limit", parent); break
            result.append(child)
        return result

    def _build(self):
        h = self.helper; proofs = {}; scopes = {}
        self.report(self.build / "manifests")
        try: manifests = h._manifests(self.build, proofs, cancel=self.cancel)
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            manifests = []; self.warn("unreadable_manifests", self.build / "manifests")
        # A retirement archives the same state and receipts. It never becomes a
        # resumable normal run again, but its remaining large caches stay visible.
        for parent, retired in ((self.root / "sessions", False), (self.root / "logs/removed-runs", True)):
            try: folders = self._folders(parent)
            except Cancelled: raise
            except (WizardError, OSError): self.warn("unreadable_runs", parent); continue
            for folder in folders:
                self.report(folder)
                try:
                    value = h._session(folder, proofs)
                    scope = h._session_scope(self.root, self.build, folder, value, manifests, proofs, cancel=self.cancel)
                except Cancelled: raise
                except (WizardError, OSError, ValueError, TypeError, KeyError):
                    self.warn("unreadable_run_owner", folder); continue
                if folder.name in self.rows:
                    self.warn("duplicate_run_owner", folder); continue
                choices = value["choices"]
                row = {"session": folder.name, "status": value.get("status"),
                       "created": value.get("created"), "updated": value.get("updated"),
                       "mode": choices.get("mode", "build"), "gameRoot": choices.get("gameRoot"),
                       "retired": retired, "exclusiveBytes": 0, "sharedBytes": 0,
                       "deliverableBytes": 0, "intermediateBytes": 0, "reclaimableBytes": 0,
                       "totalBytes": 0, "exact": True, "complete": True, "reclaimableExact": False}
                self.rows[folder.name] = row; scopes[folder.name] = scope
                self.claim(folder, folder.name)
                for key in scope.get("updates", ()):
                    self.claim(self.build / "updates" / key, folder.name)
                try:
                    for target in h._source_targets(self.root, folder, folder.name, proofs, [], cancel=self.cancel):
                        self.claim(target, folder.name, intermediate=True, reclaimable=True)
                except Cancelled: raise
                except (WizardError, OSError, ValueError, TypeError, KeyError):
                    self.warn("unreadable_source_owner", folder); row["complete"] = False
                for reference in h._references(self.root, self.build, scope["references"] | scope["sources"], cancel=self.cancel):
                    self.claim(reference, folder.name)
                # Early accepted qualification is enough to show what this run
                # depends on. An exact selected manifest additionally qualifies
                # its producer roots as possible cleanup candidates.
                for game_key in scope["games"]:
                    self.check_cancel()
                    self.claim(self.build / "inputs/game" / game_key, folder.name, intermediate=True)
                    key = h._project_key({"target": "game", "game": {"key": game_key, "unityVersion": "2021.3.5f1"}})
                    self.claim(self.build / "projects" / key, folder.name, intermediate=True)
                    for area in PROJECT_AREAS:
                        self.claim(self.build / "cache" / area / key, folder.name, intermediate=True)
                for manifest in scope["manifests"]:
                    self.check_cancel()
                    for kind, area in (("game", "game"), ("mod", "mod"), ("probeAssets", "probe"), ("startupProject", "startup")):
                        member = manifest.get(kind)
                        if not isinstance(member, dict) or not h._key(member.get("key")): continue
                        path = self.build / "inputs" / area / member["key"]
                        candidate = False
                        if path.is_dir():
                            try:
                                captured, _ = h._snapshot(path, proofs, verify_files=False, cancel=self.cancel)
                                candidate = captured["files"] == member.get("files")
                                if candidate:
                                    self.expected_files[path] = {item["path"] for item in captured["files"]} | {".snapshot.json"}
                            except Cancelled: raise
                            except (WizardError, OSError, ValueError, TypeError, KeyError): pass
                        self.claim(path, folder.name, intermediate=True, reclaimable=candidate)
                    key = h._project_key(manifest)
                    if not key: continue
                    project = self.build / "projects" / key
                    marker = self.build / "cache/project-lifecycle" / key / "owner.json"
                    candidate = False
                    if marker.is_file():
                        try:
                            owner = h._read(marker, proofs)
                            candidate = owner in (
                                {"schema": 2, "owner": "Quest generated project", "project": "projects/" + key, "workspaceKey": key},
                                {"schema": 1, "owner": "Quest generated project", "project": "projects/" + key, "inputKey": manifest["inputKey"]})
                        except (WizardError, OSError): pass
                    self.claim(project, folder.name, intermediate=True, reclaimable=candidate)
                    group = {project}
                    for area in PROJECT_AREAS:
                        related = self.build / "cache" / area / key
                        group.add(related)
                        self.claim(related, folder.name, intermediate=True, reclaimable=candidate)
                    self.project_groups[project] = group
        # Compact updates retain one compiler project per original game/ABI,
        # rather than a copy per Wizard run. Its producer-owned codeKey and
        # gameInputKey bind it to the exact captured plans already observed.
        try:
            for project in h._children(self.build / "updates/code-projects", cancel=self.cancel):
                marker = project / "QuestCodeUpdate/code-project-owner.json"
                if not marker.is_file(): continue
                value = h._read(marker, proofs)
                owner = value.get("owner")
                if set(value) != {"owner", "state"} or value.get("state") != "ready" or not isinstance(owner, dict): continue
                if not h._key(owner.get("codeKey")) or not h._key(owner.get("gameInputKey")): continue
                for session, scope in scopes.items():
                    if owner["codeKey"] in scope.get("updates", ()) or owner["gameInputKey"] in scope["inputs"]:
                        self.claim(project, session, intermediate=True)
            for key_folder in h._children(self.build / "updates/game-inputs", cancel=self.cancel):
                for session, scope in scopes.items():
                    if key_folder.name in scope["games"]: self.claim(key_folder, session, intermediate=True)
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            self.warn("unreadable_update_owner", self.build / "updates")
        try:
            self.report(self.build / "cache")
            raw_owners = h._raw_owners(self.build, manifests, proofs, verify_files=False, cancel=self.cancel)
            staged = {}
            for candidate in h._children(self.build / "cache/recovery", cancel=self.cancel):
                self.check_cancel()
                marker = candidate / "stage-owner.json"
                if not marker.is_file(): continue
                owner = h._read(marker, proofs)
                if owner == {"schema": 1, "owner": "Quest recovered Campaign stage", "key": candidate.name,
                             "gameKey": owner.get("gameKey")} and h._key(owner.get("gameKey")):
                    staged[candidate] = owner["gameKey"]
            for session, scope in scopes.items():
                self.check_cancel()
                for path, game_key in {**raw_owners, **staged}.items():
                    if game_key in scope["games"]:
                        self.claim(path, session, intermediate=True, reclaimable=True)
            self.retained = h._delivered_references(self.root, self.build, proofs, cancel=self.cancel)
        except Cancelled: raise
        except (WizardError, OSError, ValueError, TypeError, KeyError):
            self.warn("unreadable_shared_owner", self.build)
            # Unreadable dependency information must not advertise deletion.
            for claim in self.claims.values(): claim["reclaimable"] = False
        for path, claim in self.claims.items():
            if any(path == kept or path in kept.parents or kept in path.parents for kept in self.retained):
                claim["reclaimable"] = False

    def _matching(self, path):
        # Lookup by ancestors avoids an O(files * runs/resources) path search.
        result = []
        for ancestor in (path, *path.parents):
            if ancestor == self.root: break
            claim = self.claims.get(ancestor)
            if claim is not None: result.append((ancestor, claim))
        return result

    def obstacle(self, path):
        """Pins, links, surplus signing files and unreadable subtrees stay kept."""
        for ancestor, _ in self._matching(path): self.blocked.add(ancestor)
        for candidate in self.claims:
            if path == candidate or path in candidate.parents: self.blocked.add(candidate)
        for project, related in self.project_groups.items():
            if project in self.blocked: self.blocked.update(related)

    def add(self, path, size, category):
        matches = self._matching(path)
        for ancestor, _ in matches:
            if ancestor in self.expected_files and path.relative_to(ancestor).as_posix() not in self.expected_files[ancestor]:
                self.blocked.add(ancestor)
        owners = set().union(*(claim["owners"] for _, claim in matches)) if matches else set()
        if len(owners) == 1: self.exclusive_bytes += size
        elif owners: self.shared_bytes += size
        else: self.unassigned_bytes += size
        intermediate = category in INTERMEDIATE_CATEGORIES or any(claim["intermediate"] for _, claim in matches)
        if intermediate: self.reusable_bytes += size
        parts = path.relative_to(self.build).parts if self.build in path.parents else ()
        fast_update = (parts[:2] in (("updates", "code-projects"), ("updates", "source-inputs"), ("updates", "game-inputs"))
                       or len(parts) > 2 and parts[0] == "updates" and parts[2] in ("code-player.apk", "code-player.apk.code-build.json"))
        if fast_update: self.fast_update_bytes += size
        if category == "apk-content": self.deliverable_bytes += size
        if category == "tools": self.tool_bytes += size
        if category == "logs-captures": self.diagnostic_bytes += size
        for owner in owners:
            row = self.rows[owner]; row["totalBytes"] += size
            row["exclusiveBytes" if len(owners) == 1 else "sharedBytes"] += size
            if intermediate: row["intermediateBytes"] += size
            if category == "apk-content": row["deliverableBytes"] += size
        if path.name == ".keep": self.obstacle(path.parent)
        if path.suffix.lower() in (".keystore", ".jks", ".p12", ".pem", ".key"):
            self.obstacle(path)
        if len(owners) == 1:
            owner = next(iter(owners)); row = self.rows[owner]
            if row["status"] in STOPPED and category not in ("tools", "apk-content", "signing-backups"):
                # Choose one largest qualified root; it will be rejected as a
                # whole if the same size walk sees a pin or borrowed content.
                candidates = [(ancestor, claim) for ancestor, claim in matches if claim["reclaimable"]]
                if candidates:
                    ancestor = candidates[-1][0]
                    key = (owner, ancestor); self.potential[key] = self.potential.get(key, 0) + size

    def finish(self, *, complete):
        for (owner, path), size in self.potential.items():
            if complete and self.complete and path not in self.blocked: self.rows[owner]["reclaimableBytes"] += size
        for row in self.rows.values():
            row["complete"] &= complete and self.complete
            row["exact"] = row["complete"]
        ordered = sorted(self.rows.values(), key=lambda row: (row.get("updated") or 0, row["session"]), reverse=True)
        return {"runs": [row for row in ordered if not row["retired"]],
                "retiredRuns": [row for row in ordered if row["retired"]],
                "exclusiveBytes": self.exclusive_bytes, "sharedBytes": self.shared_bytes,
                "unassignedBytes": self.unassigned_bytes, "reusableBytes": self.reusable_bytes,
                "deliverableBytes": self.deliverable_bytes, "toolBytes": self.tool_bytes,
                "diagnosticBytes": self.diagnostic_bytes, "ownershipComplete": self.complete,
                "fastUpdateBytes": self.fast_update_bytes,
                "fastUpdatePolicy": {"schema": 1, "retainedOnRunRemoval": True,
                    "messages": {"en": "Fast-update compiler data is shared between runs and retained when old runs are removed.",
                                 "de": "Daten für schnelle Updates werden von mehreren Läufen genutzt und beim Entfernen alter Läufe behalten."}},
                "ownershipWarnings": self.warnings}
