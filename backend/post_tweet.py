from __future__ import annotations

import os
import re
from typing import List, Optional

from .media_upload import is_video_path, upload_media_streaming
from .tweet_jobs import TweetJob, job_store
from twikit.errors import InvalidMedia
from .twikit_client import client, login

# Keep in sync with TweetPage AllowedMediaExtensions (subset used for path validation).
ALLOWED_EXTENSIONS = {
    ".pjp",
    ".jfif",
    ".jpe",
    ".pjpeg",
    ".jpeg",
    ".jpg",
    ".png",
    ".webp",
    ".gif",
    ".m4v",
    ".mp4",
    ".mov",
}


def _validate_media_path(path: str) -> str:
    if not path or not str(path).strip():
        raise ValueError("Empty media path")

    normalized = os.path.normpath(path.strip())
    if not os.path.isabs(normalized):
        raise ValueError(f"Media path must be absolute: {path}")
    if not os.path.isfile(normalized):
        raise ValueError(f"Media file not found: {normalized}")

    ext = os.path.splitext(normalized)[1].lower()
    if ext not in ALLOWED_EXTENSIONS:
        raise ValueError(f"Unsupported media extension: {ext}")

    return normalized


def _friendly_invalid_media_message(exc: Exception) -> str:
    """Turn X/twikit media errors into a readable Japanese message."""
    raw = str(exc)
    if re.search(r"Duration too long", raw, flags=re.IGNORECASE):
        max_m = re.search(r"maximum:\s*([^\s,]+)", raw, flags=re.IGNORECASE)
        act_m = re.search(r"actual:\s*([^\s,(]+)", raw, flags=re.IGNORECASE)
        max_s = max_m.group(1) if max_m else "不明"
        act_s = act_m.group(1) if act_m else "不明"
        return (
            "動画の長さがアカウントの上限を超えています。"
            f" 上限={max_s} / 実際={act_s}。"
            " 短い動画に切り出すか、X Premium 等で長い動画が許可されているアカウントを使ってください。"
            f" (詳細: {raw})"
        )
    return f"メディアが拒否されました: {raw}"


async def upload_media_ids(paths: Optional[List[str]] = None) -> list[str]:
    """Upload local files and return media IDs. Empty/None paths → []."""
    media_ids: list[str] = []
    if not paths:
        return media_ids

    try:
        for raw_path in paths:
            path = _validate_media_path(raw_path)
            if is_video_path(path):
                media_id = await upload_media_streaming(
                    client,
                    path,
                    media_category="tweet_video",
                    is_long_video=True,
                    wait_for_completion=True,
                )
            else:
                media_id = await client.upload_media(path)
            media_ids.append(str(media_id))
    except InvalidMedia as e:
        raise ValueError(_friendly_invalid_media_message(e)) from e

    return media_ids


async def tweeting_with_media(text: str, paths: Optional[List[str]] = None):
    """
    Synchronous (blocking request) path — kept for compatibility.
    Prefer start_tweet_job + run_tweet_job for progress UI.
    """
    login()

    try:
        media_ids = await upload_media_ids(paths)
        tweet = await client.create_tweet(text=text or "", media_ids=media_ids)
    except InvalidMedia as e:
        raise ValueError(_friendly_invalid_media_message(e)) from e

    print(f"投稿完了  ID: {tweet.id} (メディア: {len(media_ids)}件)")
    return tweet.id


async def quote_with_media(
    tweet_id: str,
    text: str,
    paths: Optional[List[str]] = None,
):
    """Create a quote tweet, optionally attaching local media."""
    login()
    attachment_url = f"https://x.com/i/status/{tweet_id}"
    try:
        media_ids = await upload_media_ids(paths)
        tweet = await client.create_tweet(
            text=text or "",
            media_ids=media_ids or None,
            attachment_url=attachment_url,
        )
    except InvalidMedia as e:
        raise ValueError(_friendly_invalid_media_message(e)) from e

    print(
        f"引用ツイート完了  ID: {tweet.id} "
        f"(引用元: {tweet_id}, メディア: {len(media_ids)}件)"
    )
    return tweet.id


def start_tweet_job(text: str, paths: Optional[List[str]] = None) -> TweetJob:
    """Validate paths and create a queued job (does not start work)."""
    validated: list[str] = []
    if paths:
        for raw in paths:
            validated.append(_validate_media_path(raw))
    return job_store.create(text or "", validated)


async def run_tweet_job(job_id: str) -> None:
    """Background worker: upload media with progress, then create_tweet."""
    job = job_store.get(job_id)
    if not job:
        return

    job_store.set_state(job_id, "running", message="投稿処理中...")
    login()

    # Mark all as waiting initially.
    for i in range(len(job.items)):
        job_store.update_item(job_id, i, phase="waiting", percent=0)

    media_ids: list[str] = []
    try:
        for index, item in enumerate(job.items):
            path = item.path

            def make_progress_cb(i: int):
                async def _cb(phase: str, percent: int) -> None:
                    job_store.update_item(job_id, i, phase=phase, percent=percent)

                return _cb

            progress_cb = make_progress_cb(index)

            if is_video_path(path):
                job_store.update_item(job_id, index, phase="uploading", percent=0)
                media_id = await upload_media_streaming(
                    client,
                    path,
                    media_category="tweet_video",
                    is_long_video=True,
                    wait_for_completion=True,
                    on_progress=progress_cb,
                )
            else:
                job_store.update_item(job_id, index, phase="uploading", percent=0)
                media_id = await client.upload_media(path)
                job_store.update_item(job_id, index, phase="uploading", percent=100)

            job_store.update_item(job_id, index, phase="done", percent=100)
            media_ids.append(str(media_id))

        job_store.set_state(job_id, "running", message="ツイート作成中...")
        tweet = await client.create_tweet(text=job.text or "", media_ids=media_ids)
        msg = f"ツイート完了 ID: {tweet.id}"
        print(f"投稿完了  ID: {tweet.id} (メディア: {len(media_ids)}件)")
        job_store.set_state(job_id, "succeeded", message=msg, tweet_id=str(tweet.id))
    except InvalidMedia as e:
        friendly = _friendly_invalid_media_message(e)
        # Mark current unfinished items as failed if still mid-flight
        job_now = job_store.get(job_id)
        if job_now:
            for i, it in enumerate(job_now.items):
                if it.phase not in ("done", "failed"):
                    job_store.update_item(job_id, i, phase="failed", error=friendly)
        job_store.set_state(job_id, "failed", message=friendly)
    except Exception as e:
        message = str(e)
        job_now = job_store.get(job_id)
        if job_now:
            for i, it in enumerate(job_now.items):
                if it.phase not in ("done", "failed"):
                    job_store.update_item(job_id, i, phase="failed", error=message)
        job_store.set_state(job_id, "failed", message=message)
        print(f"投稿ジョブ失敗 {job_id}: {e}")
