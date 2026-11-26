using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TextTemplateManager.Models
{
    /// <summary>Three-way merge of one item: the cached (app) copy and the file's copy, each compared field by field
    /// with the copy last read from or written to the file here. A field changed on one side only is taken from that
    /// side, so edits made on two devices to different fields or language tabs are both kept. A field changed on both
    /// sides goes to the newer item, as the whole item did before.</summary>
    internal static class SyncMerge
    {
        // The first tab of a template without other tabs has no id.
        private const string NoId = "";

        /// <summary>Writes the merge of <paramref name="cached"/> and <paramref name="file"/> into
        /// <paramref name="cached"/>. All three must be the same kind of item.</summary>
        public static void Into(BaseItem synced, BaseItem cached, BaseItem file)
        {
            bool cacheNewer = string.CompareOrdinal(cached.LastChange, file.LastChange) > 0;

            string title = Pick(synced.Title, cached.Title, file.Title, cacheNewer);
            var unknown = Pick(synced.UnknownFields, cached.UnknownFields, file.UnknownFields, cacheNewer, SameFields);

            bool sameAsFile = title == file.Title && SameFields(unknown, file.UnknownFields);
            bool sameAsCache = title == cached.Title && SameFields(unknown, cached.UnknownFields);

            if (synced is Template st && cached is Template ct && file is Template ft)
            {
                string single = Pick(st.SingleKeyShortcut, ct.SingleKeyShortcut, ft.SingleKeyShortcut, cacheNewer);
                string multi = Pick(st.MultiKeyShortcut, ct.MultiKeyShortcut, ft.MultiKeyShortcut, cacheNewer);
                string tags = Pick(st.TagsCsv, ct.TagsCsv, ft.TagsCsv, cacheNewer);
                var paste = Pick(st.DefaultPasteMode, ct.DefaultPasteMode, ft.DefaultPasteMode, cacheNewer);
                var tabs = MergeTabs(st, ct, ft, cacheNewer);

                sameAsFile &= single == ft.SingleKeyShortcut && multi == ft.MultiKeyShortcut && tags == ft.TagsCsv
                    && paste == ft.DefaultPasteMode && SameTabs(tabs, TabsOf(ft));
                sameAsCache &= single == ct.SingleKeyShortcut && multi == ct.MultiKeyShortcut && tags == ct.TagsCsv
                    && paste == ct.DefaultPasteMode && SameTabs(tabs, TabsOf(ct));

                ct.SingleKeyShortcut = single;
                ct.MultiKeyShortcut = multi;
                ct.TagsCsv = tags;
                ct.DefaultPasteMode = paste;
                SetTabs(ct, tabs);
                ct.LanguagesKeptLocally = false;
                ct.DefaultChangedSinceLanguages = false;
            }

            cached.Title = title;
            cached.UnknownFields = unknown;
            // A merge that holds edits from both sides is a new version: newer than either, so a device that has no
            // copy to compare with still takes it.
            cached.LastChange = sameAsFile ? file.LastChange
                : sameAsCache ? cached.LastChange
                : Max(DateTime.Now.ToString("yyyyMMddHHmmss"), Max(cached.LastChange, file.LastChange));
        }

        private static T Pick<T>(T synced, T cached, T file, bool cacheNewer, Func<T, T, bool>? same = null)
        {
            same ??= EqualityComparer<T>.Default.Equals;
            bool cacheChanged = !same(cached, synced), fileChanged = !same(file, synced);
            if (cacheChanged && !fileChanged) return cached;
            if (cacheChanged && fileChanged && cacheNewer && !same(cached, file)) return cached;
            return file;
        }

        private static string Max(string? a, string? b) => string.CompareOrdinal(a ?? "", b ?? "") >= 0 ? a ?? "" : b ?? "";

        private static bool SameFields(Dictionary<string, JsonElement>? a, Dictionary<string, JsonElement>? b)
        {
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            if (a == null || b == null) return true;
            return a.All(kv => b.TryGetValue(kv.Key, out var other) && kv.Value.GetRawText() == other.GetRawText());
        }

        // ---- language tabs ----

        private sealed record Tab(string Id, string? Language, string Content, Dictionary<string, JsonElement>? Unknown);

        private static bool SameTab(Tab? a, Tab? b) =>
            a == null || b == null ? a == b
            : a.Id == b.Id && a.Language == b.Language && a.Content == b.Content && SameFields(a.Unknown, b.Unknown);

        private static bool SameTabs(List<Tab> a, List<Tab> b) => a.Count == b.Count && a.Zip(b).All(p => SameTab(p.First, p.Second));

        private static List<Tab> TabsOf(Template t, string? firstId = null)
        {
            var tabs = new List<Tab> { new(firstId ?? t.LanguageId ?? NoId, t.Language, t.Content, null) };
            foreach (var alt in t.AltLanguages ?? [])
                tabs.Add(new(alt.Id ?? NoId, alt.Language, alt.Content, alt.UnknownFields));
            return tabs;
        }

        private static List<Tab> MergeTabs(Template synced, Template cached, Template file, bool cacheNewer)
        {
            // The first tab gets an id when a second tab is added, so two sides may know it under different ids, or
            // one of them under none. They are the same tab unless the id is another tab on the other side (a tab
            // was made the first one there).
            string first = file.LanguageId ?? cached.LanguageId ?? synced.LanguageId ?? NoId;
            var all = new[] { synced, cached, file };
            List<Tab> Aligned(Template t) =>
                t.LanguageId != first
                && t.AltLanguages?.Any(a => a.Id == first) != true
                && !all.Any(o => o.AltLanguages?.Any(a => a.Id == (t.LanguageId ?? NoId)) == true)
                    ? TabsOf(t, first)
                    : TabsOf(t);

            var s = Aligned(synced);
            var c = Aligned(cached);
            var f = Aligned(file);

            var merged = new Dictionary<string, Tab>();
            foreach (string id in c.Concat(f).Select(t => t.Id).Distinct())
            {
                Tab? ts = s.Find(t => t.Id == id), tc = c.Find(t => t.Id == id), tf = f.Find(t => t.Id == id);
                Tab? tab;
                if (tc != null && tf != null)
                    tab = ts == null
                        ? (cacheNewer ? tc : tf)
                        : new(id,
                            Pick(ts.Language, tc.Language, tf.Language, cacheNewer),
                            Pick(ts.Content, tc.Content, tf.Content, cacheNewer),
                            Pick(ts.Unknown, tc.Unknown, tf.Unknown, cacheNewer, SameFields));
                // Missing on one side: added on the other, or removed there. A tab removed on one side and
                // changed on the other is kept.
                else if (tc != null) tab = ts != null && SameTab(tc, ts) ? null : tc;
                else tab = ts != null && SameTab(tf, ts) ? null : tf;
                if (tab != null) merged[id] = tab;
            }

            var order = Pick(s, c, f, cacheNewer, (a, b) => SameOrder(a, b));
            var other = ReferenceEquals(order, c) ? f : c;
            var tabs = order.Concat(other).Select(t => t.Id).Distinct()
                .Where(merged.ContainsKey).Select(id => merged[id]).ToList();
            if (tabs.Count == 0) tabs = c;
            if (tabs.Count == 1) tabs[0] = tabs[0] with { Id = NoId };
            return tabs;
        }

        // The same order of the tabs both have; tabs only one of them has don't count.
        private static bool SameOrder(List<Tab> a, List<Tab> b) =>
            a.Select(t => t.Id).Where(id => b.Exists(t => t.Id == id))
                .SequenceEqual(b.Select(t => t.Id).Where(id => a.Exists(t => t.Id == id)));

        private static void SetTabs(Template t, List<Tab> tabs)
        {
            var first = tabs[0];
            t.Content = first.Content;
            t.Language = first.Language;
            if (tabs.Count == 1)
            {
                t.AltLanguages = null;
                t.LanguageId = null;
                return;
            }
            var taken = new HashSet<string>(tabs.Select(tab => tab.Id).Where(id => id != NoId));
            t.LanguageId = first.Id != NoId ? first.Id : TemplateLanguages.NewId(taken);
            t.AltLanguages = tabs.Skip(1).Select(tab => new TemplateLanguage
            {
                Id = tab.Id != NoId ? tab.Id : TemplateLanguages.NewId(taken),
                Language = tab.Language,
                Content = tab.Content,
                UnknownFields = tab.Unknown,
            }).ToList();
        }
    }
}
