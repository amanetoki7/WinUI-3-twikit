from contextlib import asynccontextmanager
import asyncio

from pydantic import BaseModel
from fastapi.responses import FileResponse
from fastapi import FastAPI, HTTPException
from typing import List

# 各機能モジュール
from . import twikit_client
from . import post_tweet as pt
from . import get_timeline_twikit as gtt
from . import get_my_profile as gmp
from . import get_notifications_twikit as gnt
from . import get_search_twikit as gst
from . import get_lists_twikit as glt
from . import get_user_profile_twikit as gup
from .action_queue import ActionJob, action_queue
from .tweet_jobs import job_store
from .paths import favicon_path

import sys

# 安全な文字化け対策
try:
    if sys.stdout.encoding != "utf-8":
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if sys.stderr.encoding != "utf-8":
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except:
    pass  # 失敗しても無視

@asynccontextmanager
async def lifespan(app: FastAPI):
    await action_queue.start_worker()
    yield


app = FastAPI(title="Twikit API", lifespan=lifespan)


# --- 投稿 ---
class Input(BaseModel):
    text: str


class TweetIn(BaseModel):
    """Local-path tweet body (same machine as the WinUI client)."""
    text: str = ""
    paths: List[str] = []


# --- 投稿（ローカルパス渡し・同期・互換用） ---
@app.post("/tweet")
async def tweet(body: TweetIn):
    twikit_client.login()
    try:
        tweet_id = await pt.tweeting_with_media(body.text, body.paths)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except FileNotFoundError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except Exception as e:
        # Surface media / API failures without opaque 500 when possible.
        name = type(e).__name__
        if "InvalidMedia" in name or "Twitter" in name or "TooMany" in name:
            raise HTTPException(status_code=400, detail=str(e)) from e
        raise
    return {"result": f"ツイート完了 ID: {tweet_id}"}


# --- 投稿ジョブ（進捗ポーリング用） ---
@app.post("/tweet/start")
async def tweet_start(body: TweetIn):
    """Start async tweet job; poll GET /tweet/jobs/{job_id} every ~1s."""
    twikit_client.login()
    try:
        job = pt.start_tweet_job(body.text, body.paths)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except FileNotFoundError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e

    asyncio.create_task(pt.run_tweet_job(job.job_id))
    return {"job_id": job.job_id, "state": job.state}


@app.get("/tweet/jobs/{job_id}")
async def tweet_job_status(job_id: str):
    job = job_store.get(job_id)
    if not job:
        raise HTTPException(status_code=404, detail="job not found")
    return job.to_dict()


# --- タイムライン ---
@app.get("/timeline")
async def get_timeline(
    pages: int = 1,
    count: int | None = None,
    type: str = "for_you",
    cursor: str | None = None,
):
    twikit_client.login()
    tweets = await gtt.get_timeline_tweets(
        pages=pages,
        count_per_page=count,
        timeline_type=type,
        cursor=cursor,
    )
    return tweets


# --- いいね ---
@app.post("/like/{tweet_id}")
async def like_tweet(tweet_id: str):
    await action_queue.enqueue(ActionJob(action="like", tweet_id=tweet_id))
    return {"success": True, "action": "like", "queued": True}


# --- いいね解除 ---
@app.delete("/like/{tweet_id}")
async def unlike_tweet(tweet_id: str):
    await action_queue.enqueue(ActionJob(action="unlike", tweet_id=tweet_id))
    return {"success": True, "action": "unlike", "queued": True}


# --- リツイート ---
@app.post("/retweet/{tweet_id}")
async def retweet_tweet(tweet_id: str):
    await action_queue.enqueue(ActionJob(action="retweet", tweet_id=tweet_id))
    return {"success": True, "action": "retweet", "queued": True}


# --- リプライ ---
@app.post("/reply/{tweet_id}")
async def reply_to_tweet(tweet_id: str, data: Input):
    twikit_client.login()
    tweet = await gtt.client.create_tweet(
        text=data.text or "",
        reply_to=tweet_id,
    )
    return {
        "success": True,
        "action": "reply",
        "tweet_id": tweet_id,
        "new_tweet_id": str(tweet.id),
        "queued": False,
    }


class QuoteIn(BaseModel):
    text: str = ""
    paths: List[str] = []


# --- 引用ツイート ---
@app.post("/quote/{tweet_id}")
async def quote_tweet(tweet_id: str, data: QuoteIn):
    try:
        new_tweet_id = await pt.quote_with_media(tweet_id, data.text, data.paths)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except FileNotFoundError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e
    except Exception as e:
        name = type(e).__name__
        if "InvalidMedia" in name or "Twitter" in name or "TooMany" in name:
            raise HTTPException(status_code=400, detail=str(e)) from e
        raise
    return {
        "success": True,
        "action": "quote",
        "tweet_id": tweet_id,
        "new_tweet_id": str(new_tweet_id),
        "queued": False,
    }


# --- プロフィール ---
@app.get("/profile")
async def get_profile():
    twikit_client.login()
    return await gmp.get_own_profile()


@app.get("/profile/tweets")
async def get_profile_tweets(count: int = 20, cursor: str | None = None):
    """自分のツイート一覧（get_user_by_screen_name → get_user_tweets）。"""
    twikit_client.login()
    try:
        return await gmp.get_own_tweets(count=count, cursor=cursor)
    except Exception as e:
        print(f"Own Profile Tweets API Error: {e}")
        return {"error": str(e), "tweets": [], "next_cursor": None}


# --- 通知 ---
@app.get("/notifications")
async def get_notifications_endpoint(count: int = 20, refresh: bool = True):
    twikit_client.login()
    return await gnt.get_notifications(count=count, refresh=refresh)


# --- リスト ---
@app.get("/lists")
async def get_lists_endpoint(count: int = 100, cursor: str | None = None):
    twikit_client.login()
    try:
        return await glt.get_user_lists(count=count, cursor=cursor)
    except Exception as e:
        print(f"Lists API Error: {e}")
        return {"error": str(e), "lists": [], "next_cursor": None}


@app.get("/lists/{list_id}/tweets")
async def get_list_tweets_endpoint(
    list_id: str, count: int = 30, cursor: str | None = None
):
    twikit_client.login()
    try:
        return await glt.get_list_timeline(list_id=list_id, count=count, cursor=cursor)
    except Exception as e:
        print(f"List Tweets API Error: {e}")
        return {"error": str(e), "tweets": [], "next_cursor": None}


# --- 検索 ---
@app.get("/search")
async def search_tweets(
    query: str, count: int = 20, product: str = "Latest", cursor: str = None
):
    twikit_client.login()
    try:
        tweets = await gst.search_tweets(
            query=query, count=count, product=product, cursor=cursor
        )
        return tweets
    except Exception as e:
        print(f"Search API Error: {e}")
        return {"error": str(e)}


# --- ユーザープロフィール検索 ---
@app.get("/users/{screen_name}")
async def get_user_profile_endpoint(screen_name: str):
    twikit_client.login()
    try:
        return await gup.get_user_profile(screen_name)
    except Exception as e:
        print(f"User Profile API Error: {e}")
        return {"error": str(e)}


@app.get("/users/{screen_name}/tweets")
async def get_user_tweets_endpoint(
    screen_name: str, count: int = 20, cursor: str | None = None
):
    twikit_client.login()
    try:
        return await gup.get_user_tweets(
            screen_name=screen_name, count=count, cursor=cursor
        )
    except Exception as e:
        print(f"User Tweets API Error: {e}")
        return {"error": str(e), "tweets": [], "next_cursor": None}


# --- ヘルスチェック ---
@app.get("/")
async def root():
    return {"status": "ok"}


@app.get("/favicon.ico")
async def favicon():
    return FileResponse(favicon_path())
