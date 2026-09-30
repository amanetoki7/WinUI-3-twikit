import asyncio
import json

from backend.get_lists_twikit import get_user_lists


if __name__ == "__main__":
    print(json.dumps(asyncio.run(get_user_lists()), ensure_ascii=False, indent=2))
