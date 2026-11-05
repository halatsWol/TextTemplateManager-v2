using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TextTemplateManager.Common;

namespace TextTemplateManager.Models
{
    /// <summary>Merges a loaded sync file into the app-side folder. Items are matched by
    /// <see cref="BaseItem.SyncId"/> and reused in place, so tree identity (expansion/selection)
    /// survives. With <c>allowSave</c>, the newer LastChange wins; otherwise the file wins.</summary>
    public static class SyncEngine
    {
        public static void Merge(Folder target, Folder fileRoot, string name, Guid sourceId, bool allowSave)
        {
            // A queue per id: a file can hold an id twice (hand edits, merged conflict copies), and one object in two
            // places breaks the tree. Each occurrence takes its own cached item, in tree order.
            var cache = new Dictionary<Guid, Queue<BaseItem>>();
            foreach (var item in target.Children.Descendants())
                if (item.SyncId is Guid sid)
                {
                    if (!cache.TryGetValue(sid, out var items)) cache[sid] = items = new Queue<BaseItem>();
                    items.Enqueue(item);
                }

            var merged = fileRoot.Children.Select(fc => BuildMerged(fc, cache, allowSave)).ToList();

            target.Id = sourceId;
            target.SyncId = fileRoot.Id;   // the sync file's root id (usually Guid.Empty)
            target.Title = name;
            target.ItemType = ItemType.Folder;
            // Adopt the file's root timestamp. Without this, each device keeps its own cached value
            // and writes it back on load, so a read-only open re-serializes differently and churns
            // the shared file — which makes OneDrive spawn conflict copies across devices.
            target.LastChange = fileRoot.LastChange;
            target.UnknownFields = fileRoot.UnknownFields;

            SyncChildren(target.Children, merged);
        }

        private static BaseItem BuildMerged(BaseItem fileItem, Dictionary<Guid, Queue<BaseItem>> cache, bool allowSave)
        {
            BaseItem? cached = null;
            if (cache.TryGetValue(fileItem.Id, out var items)) items.TryDequeue(out cached);

            // The cached (app) copy wins only if saving is allowed AND it is strictly newer.
            bool cacheWins = allowSave && cached != null &&
                string.CompareOrdinal(cached.LastChange ?? "", fileItem.LastChange ?? "") > 0;

            BaseItem result;
            if (cached != null)
            {
                // Reuse the existing object so its tree identity (expansion/selection) is preserved.
                result = cached;
                if (!cacheWins) CopyContent(fileItem, result); // adopt the file's newer content
            }
            else
            {
                // New item introduced by the file.
                result = fileItem is Template ? new Template() : new Folder();
                result.Id = Guid.NewGuid();
                CopyContent(fileItem, result);
            }

            result.SyncId = fileItem.Id;

            var mergedChildren = fileItem.Children.Select(fc => BuildMerged(fc, cache, allowSave)).ToList();
            SyncChildren(result.Children, mergedChildren);

            return result;
        }

        private static void CopyContent(BaseItem from, BaseItem to)
        {
            to.Title = from.Title;
            to.LastChange = from.LastChange;
            to.UnknownFields = from.UnknownFields;
            if (from is Template ft && to is Template tt)
            {
                tt.Content = ft.Content;
                tt.SingleKeyShortcut = ft.SingleKeyShortcut;
                tt.MultiKeyShortcut = ft.MultiKeyShortcut;
                tt.TagsCsv = ft.TagsCsv;
                tt.DefaultPasteMode = ft.DefaultPasteMode;
            }
        }

        /// <summary>Updates <paramref name="target"/> in place to match <paramref name="desired"/>
        /// (add/remove/move), preserving the reused item objects and their containers.</summary>
        private static void SyncChildren(ObservableCollection<BaseItem> target, List<BaseItem> desired)
        {
            for (int i = target.Count - 1; i >= 0; i--)
                if (!desired.Contains(target[i])) target.RemoveAt(i);

            for (int i = 0; i < desired.Count; i++)
            {
                int cur = target.IndexOf(desired[i]);
                if (cur < 0) target.Insert(i, desired[i]);
                else if (cur != i) target.Move(cur, i);
            }
        }
    }
}
