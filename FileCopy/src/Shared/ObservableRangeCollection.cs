using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// ObservableCollection with AddRange / ReplaceAll that fire a single Reset
    /// notification instead of one per item or a ranged Add.
    ///
    /// WPF's CollectionViewSource (used by DataGrid) does NOT support ranged Add
    /// notifications — it throws "Range actions are not supported" when NewItems
    /// contains more than one element. We therefore always use Reset, which is
    /// universally safe. The caller's RefreshFilter() / _itemsView.Refresh() handles
    /// the view update after a Reset.
    /// </summary>
    public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotify;

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = items as IList<T> ?? items.ToList();
            if (list.Count == 0) return;

            _suppressNotify = true;
            try { foreach (var item in list) Items.Add(item); }
            finally { _suppressNotify = false; }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void ReplaceAll(IEnumerable<T> items)
        {
            _suppressNotify = true;
            try { Items.Clear(); foreach (var item in items) Items.Add(item); }
            finally { _suppressNotify = false; }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotify) base.OnCollectionChanged(e);
        }
    }
}
