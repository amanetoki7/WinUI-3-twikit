import argparse
import asyncio
import json

from backend.get_search_twikit import search_tweets


parser = argparse.ArgumentParser()
parser.add_argument("query")
parser.add_argument("--count", type=int, default=20)
args = parser.parse_args()

if __name__ == "__main__":
    print(
        json.dumps(
            asyncio.run(search_tweets(args.query, count=args.count)),
            ensure_ascii=False,
            indent=2,
        )
    )
