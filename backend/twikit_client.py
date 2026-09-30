from twikit import Client
from pathlib import Path

from .paths import cookies_path

# ClientTransaction 互換パッチ（KEY_BYTE / /home 優先）。
# パッチなし動作確認時は False。必要なら True に戻す。
ENABLE_TRANSACTION_PATCH = False

if ENABLE_TRANSACTION_PATCH:
    # Must run before any Client.request (transaction id / KEY_BYTE scrape).
    from . import twikit_transaction_patch

    twikit_transaction_patch.apply()
    print("twikit_transaction_patch: enabled")
else:
    print("twikit_transaction_patch: disabled")

client = Client("ja-JP")


def login(cookies_file: str | None = None):
    """中央集中ログイン関数"""
    cookies_file = cookies_file or str(cookies_path())
    if not Path(cookies_file).exists():
        print("Cookiesファイルが見つかりません")
        return False

    try:
        client.load_cookies(cookies_file)
        # 初回のみ詳細表示（2回目以降は簡略化したい場合）
        if not hasattr(login, "logged_in"):
            print("クッキーロード完了（twikit_client）")
            login.logged_in = True
        return True
    except Exception as e:
        print(f"ログイン失敗: {e}")
        return False
