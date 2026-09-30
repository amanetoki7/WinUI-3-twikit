import argparse
import asyncio
import json

from backend.get_timeline_twikit import get_timeline_tweets


parser = argparse.ArgumentParser()
parser.add_argument("--type", default="for_you", choices=("for_you", "latest"))
parser.add_argument("--count", type=int, default=30)
args = parser.parse_args()

if __name__ == "__main__":
    result = asyncio.run(
        get_timeline_tweets(count_per_page=args.count, timeline_type=args.type)
    )
    print(json.dumps(result, ensure_ascii=False, indent=2))
