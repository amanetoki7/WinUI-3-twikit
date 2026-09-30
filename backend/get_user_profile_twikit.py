from datetime import timezone, timedelta
from typing import Dict, List, Optional

from .twikit_client import client, login
from .tweet_serializer import tweet_to_dict


def normalize_screen_name(screen_name: str) -> str:
    name = (screen_name or "").strip()
    if name.startswith("@"):
        name = name[1:]
    return name.strip()


def _user_to_profile_dict(user) -> Dict:
    created_str = "不明"
    try:
        if hasattr(user, "created_at_datetime") and user.created_at_datetime:
            dt = user.created_at_datetime.astimezone(timezone(timedelta(hours=9)))
            created_str = dt.strftime("%Y/%m/%d")
    except Exception:
        created_str = getattr(user, "created_at", "不明")

    return {
        "id": user.id,
        "name": user.name,
        "screen_name": user.screen_name,
        "bio": user.description,
        "followers_count": user.followers_count,
        "following_count": user.following_count,
        "location": getattr(user, "location", None),
        "created_str": created_str,
        "profile_image_url": user.profile_image_url,
        "profile_banner_url": getattr(user, "profile_banner_url", None),
        "statuses_count": user.statuses_count,
        "favourites_count": getattr(user, "favourites_count", None),
        "verified": bool(
            getattr(user, "is_blue_verified", False)
            or getattr(user, "verified", False)
        ),
    }


async def get_user_profile(screen_name: str) -> Dict:
    login()
    name = normalize_screen_name(screen_name)
    if not name:
        return {"error": "ユーザー名が空です"}

    try:
        print(f"ユーザープロフィール取得中... screen_name={name}")
        user = await client.get_user_by_screen_name(name)
        return _user_to_profile_dict(user)
    except Exception as e:
        print(f"ユーザープロフィール取得エラー: {e}")
        return {"error": str(e)}


async def get_user_tweets(
    screen_name: str, count: int = 20, cursor: Optional[str] = None
) -> Dict:
    login()
    name = normalize_screen_name(screen_name)
    if not name:
        return {"error": "ユーザー名が空です", "tweets": [], "next_cursor": None}

    results: List[Dict] = []
    next_cursor = None

    try:
        print(
            f"ユーザーツイート取得中... screen_name={name} "
            f"count={count} cursor={'あり' if cursor else 'なし'}"
        )
        user = await client.get_user_by_screen_name(name)
        timeline = await client.get_user_tweets(
            user.id, "Tweets", count=count, cursor=cursor
        )

        if not timeline:
            print("ユーザーツイートが空です")
            return {"tweets": [], "next_cursor": None}

        seen = set()
        for t in timeline:
            if t.id in seen:
                continue
            seen.add(t.id)
            results.append(tweet_to_dict(t))

        if hasattr(timeline, "next_cursor") and timeline.next_cursor:
            next_cursor = timeline.next_cursor
            print(f"next_cursor 更新: {str(next_cursor)[:50]}...")
        else:
            next_cursor = None

    except Exception as e:
        print(f"ユーザーツイート取得エラー: {e}")
        return {"error": str(e), "tweets": [], "next_cursor": None}

    print(f"ユーザーツイート {len(results)} 件取得")
    return {"tweets": results, "next_cursor": next_cursor}
