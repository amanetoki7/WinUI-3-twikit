using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ページ送り用の <see cref="Result{T}"/> をトークンで保持する。
    /// </summary>
    /// <remarks>
    /// twikit-dotnet は X が <c>count</c> を超えて返した分を <see cref="Result{T}"/> の内部に蓄え、
    /// <see cref="Result{T}.NextAsync"/> で順に返す。<see cref="Result{T}.NextCursor"/> をそのまま
    /// 次のリクエストに渡すとその分を読み飛ばすため、FastAPI 版が返していた <c>next_cursor</c> の代わりに
    /// このストアのトークンを返し、続きは <see cref="Result{T}.NextAsync"/> で取得する。
    /// ViewModel 側は <c>next_cursor</c> を不透明な文字列として扱うので変更は不要。
    /// </remarks>
    internal sealed class PageCursorStore<T>
    {
        private const string TokenPrefix = "bridge:";

        private readonly int _capacity;
        private readonly object _gate = new();
        private readonly Dictionary<string, Result<T>> _pages = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();

        public PageCursorStore(int capacity = 256)
        {
            _capacity = capacity;
        }

        public static bool IsToken(string? cursor)
            => cursor is not null && cursor.StartsWith(TokenPrefix, StringComparison.Ordinal);

        public string Register(Result<T> page)
        {
            var token = TokenPrefix + Guid.NewGuid().ToString("N");
            lock (_gate)
            {
                _pages[token] = page;
                _order.Enqueue(token);
                while (_order.Count > _capacity)
                {
                    _pages.Remove(_order.Dequeue());
                }
            }

            return token;
        }

        public bool TryGet(string token, [MaybeNullWhen(false)] out Result<T> page)
        {
            lock (_gate)
            {
                return _pages.TryGetValue(token, out page);
            }
        }

        /// <summary>
        /// カーソルに対応するページを取得する。このストアのトークンなら蓄えたページの続き、
        /// X のカーソル文字列（または null）ならそのまま <paramref name="fetch"/> に渡す。
        /// </summary>
        /// <returns>取得したページと、続きがあるときはそのトークン（無ければ null）。</returns>
        public async Task<(Result<T> Page, string? NextCursor)> FetchAsync(
            string? cursor,
            int count,
            Func<string?, Task<Result<T>>> fetch)
        {
            Result<T> page;
            if (!string.IsNullOrEmpty(cursor) && TryGet(cursor, out var previous))
            {
                page = await previous.NextAsync().ConfigureAwait(false);
            }
            else if (IsToken(cursor))
            {
                // 保持上限を超えて捨てられたトークン。終端として扱う。
                page = Result<T>.Empty();
            }
            else
            {
                page = await fetch(string.IsNullOrEmpty(cursor) ? null : cursor).ConfigureAwait(false);
            }

            // 余剰分は page.Count == count のときにしか存在しない。
            var hasMore = page.NextCursor is not null || page.Count >= count;
            return (page, hasMore ? Register(page) : null);
        }
    }
}
