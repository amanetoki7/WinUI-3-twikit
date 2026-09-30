from twikit.client.v11 import Endpoint
from .twikit_client import client, login
from datetime import timezone, timedelta, datetime
from typing import Dict, List

from .tweet_serializer import _metric_count, _normalize_text

# 次ページ用。refresh で捨てる。
_notifications_cursor: str | None = None
_notifications_exhausted = False

_JST = timezone(timedelta(hours=9))
_MONTHS = {
    "Jan": 1,
    "Feb": 2,
    "Mar": 3,
    "Apr": 4,
    "May": 5,
    "Jun": 6,
    "Jul": 7,
    "Aug": 8,
    "Sep": 9,
    "Oct": 10,
    "Nov": 11,
    "Dec": 12,
}


def _bottom_cursor(response: dict) -> str | None:
    instructions = response.get("timeline", {}).get("instructions", [])
    for instruction in instructions:
        entries = instruction.get("addEntries", {}).get("entries", [])
        for entry in entries:
            if not str(entry.get("entryId", "")).startswith("cursor-bottom"):
                continue
            content = entry.get("content") or {}
            value = (
                (content.get("operation") or {}).get("cursor") or {}
            ).get("value") or content.get("value")
            if value:
                return value
    return None


def _timestamp_ms_from_twitter(created_at: str) -> int:
    # "Tue Sep 29 03:04:05 +0000 2026"。日本語ロケールの strptime に依存しない。
    parts = (created_at or "").split()
    if len(parts) < 6:
        return 0
    month = _MONTHS.get(parts[1])
    if month is None:
        return 0
    try:
        day = int(parts[2])
        hour, minute, second = (int(part) for part in parts[3].split(":"))
        year = int(parts[5])
        tz = parts[4]
        sign = 1 if tz.startswith("+") else -1
        offset = timezone(
            timedelta(hours=sign * int(tz[1:3]), minutes=sign * int(tz[3:5]))
        )
        dt = datetime(year, month, day, hour, minute, second, tzinfo=offset)
    except (TypeError, ValueError):
        return 0
    return int(dt.timestamp() * 1000)


def _format_timestamp_ms(timestamp_ms: int) -> str:
    if not timestamp_ms:
        return "不明"
    dt = datetime.fromtimestamp(timestamp_ms / 1000, tz=timezone.utc).astimezone(_JST)
    return dt.strftime("%Y/%m/%d %H:%M:%S")


def _view_count(tweet: dict) -> int:
    for key in ("ext_views", "views"):
        views = tweet.get(key)
        if isinstance(views, dict):
            return _metric_count(views.get("count"))
    return 0


def _tweet_text(tweet: dict) -> str:
    if not tweet:
        return ""
    note = (
        ((tweet.get("note_tweet") or {}).get("note_tweet_results") or {}).get("result")
        or {}
    ).get("text")
    return note or tweet.get("full_text") or tweet.get("text") or ""


def _strip_leading_reply_mention(text: str, screen_name: str) -> str:
    """返信先として別行に出す @ユーザー名を、本文先頭から外す。"""
    name = (screen_name or "").strip().lstrip("@")
    if not text or not name:
        return text
    prefix = "@" + name
    if len(text) < len(prefix) or text[: len(prefix)].lower() != prefix.lower():
        return text
    rest = text[len(prefix) :]
    if rest and rest[0].isascii() and (rest[0].isalnum() or rest[0] == "_"):
        return text
    return rest.lstrip(" \t\r\n\u3000")


def _reply_to_screen_name(tweet: dict, users: dict, tweets: dict) -> str:
    name = str(tweet.get("in_reply_to_screen_name") or "").strip().lstrip("@")
    if name:
        return name

    user_id = str(
        tweet.get("in_reply_to_user_id_str") or tweet.get("in_reply_to_user_id") or ""
    )
    user = users.get(user_id) or {}
    name = str(user.get("screen_name") or "").strip().lstrip("@")
    if name:
        return name

    parent_id = str(
        tweet.get("in_reply_to_status_id_str") or tweet.get("in_reply_to_status_id") or ""
    )
    parent = tweets.get(parent_id) or {}
    parent_user_id = str(parent.get("user_id_str") or parent.get("user_id") or "")
    parent_user = users.get(parent_user_id) or {}
    return str(parent_user.get("screen_name") or "").strip().lstrip("@")


def _user_verified(user: dict) -> bool:
    return bool(
        user.get("verified")
        or user.get("is_blue_verified")
        or user.get("ext_is_blue_verified")
    )


def _actor(users: dict, user_id) -> dict:
    user = users.get(str(user_id), {}) if user_id else {}
    if not user:
        return {
            "actor_name": "Unknown",
            "actor_screen_name": "",
            "actor_profile_image": "",
        }
    return {
        "actor_name": user.get("name") or "Unknown",
        "actor_screen_name": user.get("screen_name") or "",
        "actor_profile_image": (
            user.get("profile_image_url_https") or user.get("profile_image_url") or ""
        ),
    }


def _items_from_response(response: dict) -> List[Dict]:
    global_objects = response.get("globalObjects") or {}
    users = global_objects.get("users") or {}
    tweets = global_objects.get("tweets") or {}
    extracted: List[Dict] = []
    seen: set[str] = set()

    for notification in (global_objects.get("notifications") or {}).values():
        actions = (notification.get("template") or {}).get("aggregateUserActionsV1")
        if not actions:
            continue
        notification_id = str(notification.get("id") or "")
        if not notification_id or notification_id in seen:
            continue
        seen.add(notification_id)

        from_users = actions.get("fromUsers") or []
        user_id = (from_users[0].get("user") or {}).get("id") if from_users else None
        target_objects = actions.get("targetObjects") or []
        target_id = (
            (target_objects[0].get("tweet") or {}).get("id") if target_objects else None
        )
        target = tweets.get(str(target_id), {}) if target_id else {}
        try:
            timestamp_ms = int(notification.get("timestampMs") or 0)
        except (TypeError, ValueError):
            timestamp_ms = 0

        extracted.append(
            {
                "id": notification_id,
                "type": "unknown",
                "text": _normalize_text(
                    (notification.get("message") or {}).get("text") or ""
                ),
                "created_at": _format_timestamp_ms(timestamp_ms),
                "target_tweet_text": _normalize_text(_tweet_text(target)),
                "_timestamp_ms": timestamp_ms,
                **_actor(users, user_id),
            }
        )

    # リプライは notifications 辞書に入らず、timeline の tweet entry として届く。
    instructions = (response.get("timeline") or {}).get("instructions") or []
    for instruction in instructions:
        entries = (instruction.get("addEntries") or {}).get("entries") or []
        for entry in entries:
            item = (entry.get("content") or {}).get("item") or {}
            event = (item.get("clientEventInfo") or {}).get("element")
            if event != "user_replied_to_your_tweet":
                continue
            tweet_id = ((item.get("content") or {}).get("tweet") or {}).get("id")
            reply_id = str(tweet_id or "")
            if not reply_id or reply_id in seen:
                continue
            tweet = tweets.get(reply_id) or {}
            if not tweet:
                continue
            seen.add(reply_id)
            timestamp_ms = _timestamp_ms_from_twitter(tweet.get("created_at") or "")
            user = users.get(str(tweet.get("user_id_str")), {})
            reply_to = _reply_to_screen_name(tweet, users, tweets)
            extracted.append(
                {
                    "id": reply_id,
                    "type": "reply",
                    "text": _strip_leading_reply_mention(
                        _normalize_text(_tweet_text(tweet)),
                        reply_to,
                    ),
                    "created_at": _format_timestamp_ms(timestamp_ms),
                    "target_tweet_text": "",
                    "reply_count": _metric_count(tweet.get("reply_count")),
                    "retweet_count": _metric_count(tweet.get("retweet_count")),
                    "favorite_count": _metric_count(tweet.get("favorite_count")),
                    "view_count": _view_count(tweet),
                    "is_liked": bool(tweet.get("favorited")),
                    "is_retweeted": bool(tweet.get("retweeted")),
                    "user_protected": bool(user.get("protected")),
                    "user_verified": _user_verified(user),
                    "reply_to_screen_name": reply_to,
                    "_timestamp_ms": timestamp_ms,
                    **_actor(users, tweet.get("user_id_str")),
                }
            )

    extracted.sort(key=lambda item: item.get("_timestamp_ms") or 0, reverse=True)
    for item in extracted:
        item.pop("_timestamp_ms", None)
    return extracted


async def get_notifications(count: int = 20, refresh: bool = True) -> List[Dict]:
    global _notifications_cursor, _notifications_exhausted
    login()

    results: List[Dict] = []

    try:
        if refresh:
            _notifications_cursor = None
            _notifications_exhausted = False
            cursor = None
            print("✅ 通知: 最新から取得")
        else:
            if _notifications_exhausted or not _notifications_cursor:
                print("これ以上通知はありません")
                return results
            cursor = _notifications_cursor
            print(".cursor で追加取得")

        params = {
            "count": count,
            "include_ext_views": "true",
            "include_reply_count": "1",
        }
        if cursor is not None:
            params["cursor"] = cursor
        response, _ = await client.get(
            Endpoint.NOTIFICATIONS_ALL,
            params=params,
            headers=client._base_headers,
        )
        results = _items_from_response(response)

        next_cursor = _bottom_cursor(response)
        if next_cursor and next_cursor != cursor:
            _notifications_cursor = next_cursor
            _notifications_exhausted = False
        else:
            _notifications_cursor = None
            _notifications_exhausted = True

        if not results:
            print("通知はありません")
            return results

        reply_count = sum(1 for item in results if item.get("type") == "reply")
        print(f"Notifications 取得: {len(results)} 件 (reply {reply_count})")

    except Exception as e:
        print(f"Notifications取得エラー: {e}")
        _notifications_cursor = None
        _notifications_exhausted = False

    return results
