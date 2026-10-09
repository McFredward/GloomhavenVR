"""Reject unavailable native capacity before expensive retained preparation.

Capacity is a host observation, never an original-asset producer. Recheck at
the actual launch because other programs may consume capacity during recovery.
The same bounded snapshot owns the console message and structured failure.
"""
from __future__ import annotations

from storage import BuildError

PHASE = "native-memory-check"


class NativeMemoryUnavailable(BuildError):
    code = "native_memory_unavailable"
    failureStage = PHASE

    def __init__(self, policy):
        super().__init__(policy["admissionMessage"])
        self.resources = {
            "host": dict(policy["hostMemoryEvidence"]),
            "policy": {key: policy.get(key) for key in (
                "phase", "largestWorkerReserveBytes", "additionalWorkerReserveBytes", "parentReserveBytes",
                "osReserveBytes", "requiredCommitHeadroomBytes",
                "nativeLaunchAllowed", "pagingRequired", "jobs")},
        }


def require_capacity(resources, progress, output, *, target):
    if target not in ("startup", "game"):
        return resources.phase_budget("unity", output)
    progress.event(PHASE, detail="Checking available native compiler memory and commit", status="start")
    policy = resources.phase_budget("il2cpp", output)
    if not policy["nativeLaunchAllowed"]:
        progress.event(PHASE, detail=policy["admissionMessage"], status="failed")
        raise NativeMemoryUnavailable(policy)
    progress.event(PHASE, 1, 1, "checks", "Native compiler capacity is available", status="complete")
    return policy


def persist_failure(error, output, writer):
    """Recover typed capacity context after an outer stage records its failure."""
    if not isinstance(error, NativeMemoryUnavailable):
        return
    writer(output / "last-failure.json", {
        "schema": 1, "stage": PHASE, "failureStage": PHASE,
        "code": error.code, "error": type(error).__name__,
        "message": str(error), "resources": error.resources, "retained": True,
    })
