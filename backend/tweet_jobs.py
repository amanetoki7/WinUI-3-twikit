"""In-memory tweet upload jobs for progress polling (local API)."""

from __future__ import annotations

import asyncio
import threading
import time
import uuid
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional


@dataclass
class MediaItemProgress:
    path: str
    file_name: str
    phase: str = "pending"  # pending | waiting | uploading | processing | done | failed
    percent: int = 0
    error: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "path": self.path,
            "file_name": self.file_name,
            "phase": self.phase,
            "percent": self.percent,
            "error": self.error,
        }


@dataclass
class TweetJob:
    job_id: str
    text: str
    items: List[MediaItemProgress]
    state: str = "queued"  # queued | running | succeeded | failed
    message: str = ""
    tweet_id: Optional[str] = None
    created_at: float = field(default_factory=time.time)
    finished_at: Optional[float] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "job_id": self.job_id,
            "state": self.state,
            "message": self.message,
            "tweet_id": self.tweet_id,
            "items": [item.to_dict() for item in self.items],
        }


class TweetJobStore:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._jobs: Dict[str, TweetJob] = {}

    def create(self, text: str, paths: List[str]) -> TweetJob:
        import os

        job_id = uuid.uuid4().hex
        items = [
            MediaItemProgress(
                path=p,
                file_name=os.path.basename(p) or p,
                phase="pending",
                percent=0,
            )
            for p in paths
        ]
        job = TweetJob(job_id=job_id, text=text, items=items)
        with self._lock:
            self._jobs[job_id] = job
            self._prune_unlocked()
        return job

    def get(self, job_id: str) -> Optional[TweetJob]:
        with self._lock:
            return self._jobs.get(job_id)

    def update_item(
        self,
        job_id: str,
        index: int,
        *,
        phase: Optional[str] = None,
        percent: Optional[int] = None,
        error: Optional[str] = None,
    ) -> None:
        with self._lock:
            job = self._jobs.get(job_id)
            if not job or index < 0 or index >= len(job.items):
                return
            item = job.items[index]
            if phase is not None:
                item.phase = phase
            if percent is not None:
                item.percent = max(0, min(100, int(percent)))
            if error is not None:
                item.error = error

    def set_state(
        self,
        job_id: str,
        state: str,
        *,
        message: str = "",
        tweet_id: Optional[str] = None,
    ) -> None:
        with self._lock:
            job = self._jobs.get(job_id)
            if not job:
                return
            job.state = state
            job.message = message
            if tweet_id is not None:
                job.tweet_id = tweet_id
            if state in ("succeeded", "failed"):
                job.finished_at = time.time()

    def _prune_unlocked(self, max_age_sec: float = 3600.0, max_jobs: int = 50) -> None:
        now = time.time()
        stale = [
            jid
            for jid, job in self._jobs.items()
            if job.finished_at and (now - job.finished_at) > max_age_sec
        ]
        for jid in stale:
            del self._jobs[jid]
        if len(self._jobs) <= max_jobs:
            return
        # Drop oldest finished jobs first.
        finished = sorted(
            (
                (jid, job)
                for jid, job in self._jobs.items()
                if job.finished_at is not None
            ),
            key=lambda x: x[1].finished_at or 0,
        )
        while len(self._jobs) > max_jobs and finished:
            jid, _ = finished.pop(0)
            self._jobs.pop(jid, None)


job_store = TweetJobStore()
