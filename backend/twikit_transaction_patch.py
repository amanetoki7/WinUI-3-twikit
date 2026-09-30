"""
Compatibility patch for twikit ClientTransaction against current X frontend.

As of mid-2026, GET https://x.com often returns a logged-out shell
(entry-client-logged-out) that does NOT embed the ondemand.s webpack chunk.
Without that chunk, get_indices() raises:
    Exception: Couldn't get KEY_BYTE indices

Authenticated https://x.com/home still includes ondemand.s, so we prefer /home
when initializing the transaction id helpers.

Also hardens ON_DEMAND_HASH_PATTERN to accept single- or double-quoted hashes.
"""

from __future__ import annotations

import re

import bs4

_APPLIED = False


def apply() -> None:
    global _APPLIED
    if _APPLIED:
        return

    from twikit.x_client_transaction import transaction as tx
    from twikit.x_client_transaction import utils as tx_utils

    # New webpack format: ,1234:"ondemand.s"  ...  ,1234:"abc123"
    tx.ON_DEMAND_FILE_REGEX = re.compile(
        r""",(\d+):["']ondemand\.s["']""",
        flags=(re.VERBOSE | re.MULTILINE),
    )
    # Accept both quote styles for the hash value.
    tx.ON_DEMAND_HASH_PATTERN = r""",{}:["']([0-9a-fA-F]+)["']"""
    tx.INDICES_REGEX = re.compile(r"""\[(\d+)\],\s*16""")

    migration_redirection_regex = re.compile(
        r"""(http(?:s)?://(?:www\.)?(twitter|x){1}\.com(/x)?/migrate([/?])?tok=[a-zA-Z0-9%\-_]+)+""",
        re.VERBOSE,
    )

    async def handle_x_migration_prefer_home(session, headers):
        """Like twikit's handle_x_migration, but try /home first for ondemand.s."""

        async def _load(url: str):
            response = await session.request(method="GET", url=url, headers=headers)
            page = bs4.BeautifulSoup(response.content, "lxml")
            return response, page

        response, home_page = await _load("https://x.com/home")
        # Fallback when cookies are missing / session is logged out.
        if "ondemand" not in str(home_page) and 'name="twitter-site-verification"' not in str(
            home_page
        ).lower().replace("'", '"'):
            response, home_page = await _load("https://x.com")
        elif "ondemand" not in str(home_page):
            # Has verification meta sometimes without ondemand; still try root as backup.
            response2, home2 = await _load("https://x.com")
            if "ondemand" in str(home2):
                response, home_page = response2, home2

        migration_url = home_page.select_one("meta[http-equiv='refresh']")
        migration_redirection_url = re.search(
            migration_redirection_regex, str(migration_url)
        ) or re.search(migration_redirection_regex, str(response.content))
        if migration_redirection_url:
            response = await session.request(
                method="GET",
                url=migration_redirection_url.group(0),
                headers=headers,
            )
            home_page = bs4.BeautifulSoup(response.content, "lxml")

        migration_form = home_page.select_one("form[name='f']") or home_page.select_one(
            "form[action='https://x.com/x/migrate']"
        )
        if migration_form:
            url = migration_form.attrs.get("action", "https://x.com/x/migrate") + "/?mx=2"
            method = migration_form.attrs.get("method", "POST")
            request_payload = {
                input_field.get("name"): input_field.get("value")
                for input_field in migration_form.select("input")
            }
            response = await session.request(
                method=method, url=url, data=request_payload, headers=headers
            )
            home_page = bs4.BeautifulSoup(response.content, "lxml")

        return home_page

    # Patch both the utils module and the already-imported name in transaction.
    tx_utils.handle_x_migration = handle_x_migration_prefer_home
    tx.handle_x_migration = handle_x_migration_prefer_home

    # Replace get_indices with a clearer two-step lookup (matches upstream PRs).
    async def get_indices_patched(self, home_page_response, session, headers):
        key_byte_indices = []
        response = self.validate_response(home_page_response) or self.home_page_response
        response_str = str(response)

        on_demand_match = tx.ON_DEMAND_FILE_REGEX.search(response_str)
        if on_demand_match:
            chunk_index = on_demand_match.group(1)
            hash_match = re.search(
                tx.ON_DEMAND_HASH_PATTERN.format(chunk_index), response_str
            )
            if hash_match:
                file_hash = hash_match.group(1)
                on_demand_file_url = (
                    f"https://abs.twimg.com/responsive-web/client-web/"
                    f"ondemand.s.{file_hash}a.js"
                )
                on_demand_file_response = await session.request(
                    method="GET", url=on_demand_file_url, headers=headers
                )
                for item in tx.INDICES_REGEX.finditer(on_demand_file_response.text):
                    key_byte_indices.append(item.group(1))

        if not key_byte_indices:
            raise Exception(
                "Couldn't get KEY_BYTE indices "
                "(ondemand.s not found in homepage HTML — try refreshing cookies / login)"
            )

        key_byte_indices = list(map(int, key_byte_indices))
        return key_byte_indices[0], key_byte_indices[1:]

    tx.ClientTransaction.get_indices = get_indices_patched

    _APPLIED = True
