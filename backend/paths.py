import os
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent


def cookies_path() -> Path:
    configured = os.environ.get("COOKIES_FILE")
    return Path(configured) if configured else REPOSITORY_ROOT / "data" / "cookies.json"


def favicon_path() -> Path:
    return REPOSITORY_ROOT / "static" / "favicon.ico"
