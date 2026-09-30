import argparse
import asyncio

from backend.post_tweet import tweeting_with_media


parser = argparse.ArgumentParser()
parser.add_argument("text")
parser.add_argument("paths", nargs="*")
args = parser.parse_args()

if __name__ == "__main__":
    print(asyncio.run(tweeting_with_media(args.text, args.paths)))
