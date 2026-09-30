import asyncio
import json

from backend.get_notifications_twikit import get_notifications


if __name__ == "__main__":
    print(json.dumps(asyncio.run(get_notifications()), ensure_ascii=False, indent=2))
