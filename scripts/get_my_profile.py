import asyncio
import json

from backend.get_my_profile import get_own_profile


if __name__ == "__main__":
    print(json.dumps(asyncio.run(get_own_profile()), ensure_ascii=False, indent=2))
