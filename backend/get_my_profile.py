from typing import Dict, List, Optional

from .get_user_profile_twikit import _user_to_profile_dict
from .twikit_client import client, login
from .tweet_serializer import tweet_to_dict


async def _authenticated_user():
    """ログイン中のユーザー。ユーザー名はコードに書かず、セッションから取る。

    account/settings.json の screen_name を UserByScreenName に渡す。
    Client.user() が続ける UserByRestId は Cloudflare に 403 で拒まれる。
    """
    response, _ = await client.v11.settings()
    if not isinstance(response, dict):
        raise RuntimeError("アカウント設定の応答が不正です")

    screen_name = str(response.get("screen_name") or "").strip().lstrip("@")
    if not screen_name:
        raise RuntimeError("ログイン中ユーザー名を取得できませんでした")

    return await client.get_user_by_screen_name(screen_name)


async def get_own_profile():
    login()  # 中央集中ログインを使用

    try:
        user = await _authenticated_user()
        return _user_to_profile_dict(user)
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
        user = await _authenticated_user()
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
