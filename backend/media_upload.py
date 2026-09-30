"""Chunked media upload that never loads the whole file into memory."""

from __future__ import annotations

import asyncio
import io
import mimetypes
import os
from typing import Any, Callable, Optional

from twikit.errors import InvalidMedia

# Twitter media upload segment size (same as twikit.Client.upload_media).
CHUNK_SIZE = 8 * 1024 * 1024

VIDEO_EXTENSIONS = {".mp4", ".mov", ".m4v"}

ProgressCallback = Callable[[str, int], Any]
# phase: "uploading" | "processing"
# percent: 0-100 (meaningful for uploading)


def is_video_path(path: str) -> bool:
    ext = os.path.splitext(path)[1].lower()
    return ext in VIDEO_EXTENSIONS


def _guess_media_type(path: str) -> str:
    guessed, _ = mimetypes.guess_type(path)
    if guessed:
        return guessed
    ext = os.path.splitext(path)[1].lower()
    return {
        ".mp4": "video/mp4",
        ".m4v": "video/mp4",
        ".mov": "video/quicktime",
        ".gif": "image/gif",
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".jpeg": "image/jpeg",
        ".webp": "image/webp",
    }.get(ext, "application/octet-stream")


async def _emit(
    on_progress: Optional[ProgressCallback],
    phase: str,
    percent: int,
) -> None:
    if on_progress is None:
        return
    result = on_progress(phase, max(0, min(100, percent)))
    if asyncio.iscoroutine(result):
        await result


async def upload_media_streaming(
    client: Any,
    path: str,
    *,
    media_category: str | None = "tweet_video",
    is_long_video: bool | None = None,
    wait_for_completion: bool = True,
    status_check_interval: float | None = None,
    on_progress: Optional[ProgressCallback] = None,
) -> str:
    """
    Upload a local file via INIT / APPEND / FINALIZE without reading it all at once.

    APPEND runs sequentially so peak RAM stays near one 8MB chunk.
    on_progress(phase, percent) is called during upload/processing.
    """
    if not os.path.isfile(path):
        raise FileNotFoundError(f"Media file not found: {path}")

    total_bytes = os.path.getsize(path)
    if total_bytes <= 0:
        raise ValueError(f"Media file is empty: {path}")

    media_type = _guess_media_type(path)

    # Prefer long-video endpoint for all videos so >2:20 can work when the
    # account tier allows it (X Premium etc.). Free / limited tiers still
    # reject over-limit durations at STATUS or create_tweet time.
    if is_long_video is None:
        is_long_video = True

    await _emit(on_progress, "uploading", 0)

    # ---- INIT ----
    response, _ = await client.v11.upload_media_init(
        media_type, total_bytes, media_category, is_long_video
    )
    media_id = response["media_id"]

    # ---- APPEND (sequential, one chunk in memory at a time) ----
    segment_index = 0
    bytes_sent = 0
    with open(path, "rb") as file:
        while True:
            chunk = file.read(CHUNK_SIZE)
            if not chunk:
                break
            chunk_stream = io.BytesIO(chunk)
            try:
                await client.v11.upload_media_append(
                    is_long_video, media_id, segment_index, chunk_stream
                )
            finally:
                chunk_stream.close()
            segment_index += 1
            bytes_sent += len(chunk)
            percent = int(bytes_sent * 100 / total_bytes) if total_bytes else 100
            await _emit(on_progress, "uploading", percent)
            del chunk

    await _emit(on_progress, "uploading", 100)

    # ---- FINALIZE ----
    await client.v11.upload_media_finelize(is_long_video, media_id)

    # ---- STATUS (processing) ----
    if wait_for_completion:
        await _emit(on_progress, "processing", 100)
        while True:
            state = await client.check_media_status(media_id, is_long_video)
            processing_info = state.get("processing_info")
            if not processing_info:
                break
            if "error" in processing_info:
                message = processing_info["error"]
                if isinstance(message, dict):
                    message = message.get("message", str(message))
                raise InvalidMedia(str(message))
            if processing_info.get("state") == "succeeded":
                break
            await _emit(on_progress, "processing", 100)
            delay = status_check_interval
            if delay is None:
                delay = float(processing_info.get("check_after_secs") or 2)
            await asyncio.sleep(delay)

    return str(media_id)
