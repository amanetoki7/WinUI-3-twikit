from .twikit_client import client, login
from datetime import timezone, timedelta
from typing import Dict, List, Optional

from .tweet_serializer import tweet_to_dict


async def get_own_profile():
    login()  # 中央集中ログインを使用

    try:
        user = await client.user()

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
            "favourites_count": user.favourites_count,
        }
    except Exception as e:
        print(f"プロフィール取得エラー: {e}")
        return {"error": str(e)}


async def get_own_tweets(
    count: int = 20, cursor: Optional[str] = None
) -> Dict:
    """自分のツイート一覧。認証済みユーザーを解決してから取得する。"""
    login()

    results: List[Dict] = []
    next_cursor = None

    try:
        print(
            f"自分のツイート取得中... count={count} "
            f"cursor={'あり' if cursor else 'なし'}"
        )
        user = await client.user()
        timeline = await client.get_user_tweets(
            user.id, "Tweets", count=count, cursor=cursor
        )

        if not timeline:
            print("自分のツイートが空です")
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
        print(f"自分のツイート取得エラー: {e}")
        return {"error": str(e), "tweets": [], "next_cursor": None}

    print(f"自分のツイート {len(results)} 件取得")
    return {"tweets": results, "next_cursor": next_cursor}
