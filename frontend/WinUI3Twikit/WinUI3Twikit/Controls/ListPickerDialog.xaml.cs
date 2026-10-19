using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUI3Twikit.Controls
{
    /// <summary>タブ追加ダイアログの 1 行。リスト本体に、フォロー中 / 追加済みの注記を添える。</summary>
    public sealed class ListPickerRow
    {
        public required ListItemViewModel Source { get; init; }

        /// <summary>「フォロー中 · @所有者」「追加済み」など。空なら表示しない。</summary>
        public string Note { get; init; } = string.Empty;

        public string Name => Source.Name;

        public string MemberInfo => Source.MemberInfo;

        public string Description => Source.Description;

        public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DescriptionVisibility => Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// ログイン中ユーザーのリスト（作成したもの + フォロー中のもの）から 1 つ選ぶ
    /// （WinUI-3-twikit-deck の ListPickerDialog より移植）。ホームのタブの「+」から開く。ダブルクリックでも決定できる。
    /// </summary>
    public sealed partial class ListPickerDialog : ContentDialog
    {
        private static readonly HttpClient Http = new();

        private readonly IReadOnlySet<string> _addedListIds;
        private bool _doubleTapped;

        /// <param name="addedListIds">すでにタブになっているリストの ID（「追加済み」と表示する。選ぶことはできる）。</param>
        public ListPickerDialog(IReadOnlySet<string>? addedListIds = null)
        {
            InitializeComponent();
            _addedListIds = addedListIds ?? new HashSet<string>(StringComparer.Ordinal);
            IsPrimaryButtonEnabled = false;
            Opened += OnOpened;
        }

        public ListItemViewModel? SelectedList => (ListsView.SelectedItem as ListPickerRow)?.Source;

        public bool WasConfirmed(ContentDialogResult result)
            => SelectedList is not null && (result == ContentDialogResult.Primary || _doubleTapped);

        private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            try
            {
                var lists = await ListsViewModel.FetchListsAsync(Http);
                var rows = lists
                    .OrderByDescending(l => l.IsOwned)   // 作成したリストを先に（それ以外は API の並びのまま）
                    .Select(l => new ListPickerRow { Source = l, Note = NoteFor(l) })
                    .ToList();
                ListsView.ItemsSource = rows;
                EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                EmptyText.Text = $"リストを取得できませんでした: {ex.Message}";
                EmptyText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private string NoteFor(ListItemViewModel list)
        {
            var notes = new List<string>(2);
            if (!list.IsOwned)
            {
                notes.Add(list.OwnerScreenName.Length > 0 ? $"フォロー中 · @{list.OwnerScreenName}" : "フォロー中");
            }

            if (_addedListIds.Contains(list.Id))
            {
                notes.Add("追加済み");
            }

            return string.Join(" · ", notes);
        }

        private void ListsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => IsPrimaryButtonEnabled = SelectedList is not null;

        private void ListsView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (SelectedList is null)
            {
                return;
            }

            _doubleTapped = true;
            Hide();
        }
    }
}
