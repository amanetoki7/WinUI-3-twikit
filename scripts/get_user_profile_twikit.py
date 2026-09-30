import argparse
import asyncio
import json

from backend.get_user_profile_twikit import get_user_profile


parser = argparse.ArgumentParser()
parser.add_argument("screen_name")
args = parser.parse_args()

if __name__ == "__main__":
    print(
        json.dumps(
            asyncio.run(get_user_profile(args.screen_name)),
            ensure_ascii=False,
            indent=2,
        )
    )
