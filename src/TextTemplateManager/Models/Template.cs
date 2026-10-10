using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextTemplateManager.Common;
using TextTemplateManager.Helpers;
using TextTemplateManager.ViewModels;


namespace TextTemplateManager.Models
{
    /// <summary>A text template with optional single/multi-key shortcuts and tags.</summary>
    public partial class Template : BaseItem
    {
        [ObservableProperty]
        public partial string Content { get; set; } = string.Empty;

        public string SingleKeyShortcut
        {
            get => _singleKeyShortcut;
            set => SetShortcut(ref _singleKeyShortcut, value);
        }
        private string _singleKeyShortcut = string.Empty;

        public string MultiKeyShortcut
        {
            get => _multiKeyShortcut;
            set => SetShortcut(ref _multiKeyShortcut, value);
        }
        private string _multiKeyShortcut = string.Empty;

        [ObservableProperty]
        public partial string TagsCsv { get; set; } = string.Empty;

        [ObservableProperty]
        public partial PasteMode DefaultPasteMode { get; set; }

        [ObservableProperty]
        public partial bool HasSingleKeyConflict { get; set; }

        [ObservableProperty]
        public partial bool HasMultiKeyConflict { get; set; }

        /// <summary>Same single key used in another area — allowed, surfaced as a warning. Transient.</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial bool HasSingleKeyCrossAreaWarning { get; set; }

        /// <summary>Same effective multikey used in another area — allowed, surfaced as a warning. Transient.</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial bool HasMultiKeyCrossAreaWarning { get; set; }

        /// <summary>Display-only: the multikey as typed (sync-prefixed, e.g. "and-msg").</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial string EffectiveMultiKey { get; set; } = string.Empty;

        /// <summary>Display-only: the parent root — a sync folder's name, or "local".</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial string SourceLabel { get; set; } = string.Empty;

        /// <summary>Display-only: true when not inside a sync folder.</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial bool IsLocalSource { get; set; }

        /// <summary>Owning ViewModel, required for conflict validation.</summary>
        [JsonIgnore]
        public MainViewModel? Owner { get; set; }

        private static readonly Regex _shortcutFilter = new(@"[^A-Z0-9]", RegexOptions.Compiled);

        /// <summary>A shortcut as stored: trimmed, uppercase, letters A-Z and digits only. '-' and '.' are
        /// reserved as the separator between a sync folder's prefix and the shortcut.</summary>
        public static string CleanShortcut(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : _shortcutFilter.Replace(value.Trim().ToUpperInvariant(), string.Empty);

        public Template()
        {
            ItemType = ItemType.Template;
            DefaultPasteMode = PasteMode.Auto;
        }

        #region Observable Property Handlers

        // Stores the cleaned value, so only that is ever announced. Raw input that cleans to the stored value
        // ("a" over "A") is still announced, so an editing TextBox shows the cleaned text again.
        private void SetShortcut(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            string clean = CleanShortcut(value);
            if (SetProperty(ref field, clean, propertyName)) ValidateShortcuts();
            else if (!string.Equals(value, clean, StringComparison.Ordinal)) OnPropertyChanged(propertyName);
        }

        #endregion

        #region Shortcut Conflict Validation

        /// <summary>Flag this template's shortcut conflicts against the rest of the Owner's tree.</summary>
        public void ValidateShortcuts()
        {
            if (Owner == null) return;

            var otherTemplates = Owner.AllItems
                .SelectManyRecursive(i => i.Children)
                .OfType<Template>()
                .Where(t => t != this)
                .ToList();

            Guid thisArea = Owner.GetAreaKey(this);

            // Single-key: same key in the SAME area is a conflict; the same key in another area is
            // allowed (local wins, then sync order) and only flagged as a warning.
            bool hasSingle = !string.IsNullOrWhiteSpace(SingleKeyShortcut);
            HasSingleKeyConflict = hasSingle && otherTemplates.Any(t =>
                Owner.GetAreaKey(t) == thisArea &&
                string.Equals(t.SingleKeyShortcut, SingleKeyShortcut, StringComparison.OrdinalIgnoreCase));
            HasSingleKeyCrossAreaWarning = hasSingle && !HasSingleKeyConflict && otherTemplates.Any(t =>
                Owner.GetAreaKey(t) != thisArea &&
                string.Equals(t.SingleKeyShortcut, SingleKeyShortcut, StringComparison.OrdinalIgnoreCase));

            // Multi-key compared on the effective (prefixed) shortcut, same rule as single-key: a
            // same-area clash is a conflict, across areas it's an allowed warning.
            string thisEffective = Owner.GetEffectiveMultiKey(this);
            bool hasMulti = !string.IsNullOrWhiteSpace(MultiKeyShortcut);
            HasMultiKeyConflict = hasMulti && otherTemplates.Any(t =>
                Owner.GetAreaKey(t) == thisArea &&
                string.Equals(Owner.GetEffectiveMultiKey(t), thisEffective, StringComparison.OrdinalIgnoreCase));
            HasMultiKeyCrossAreaWarning = hasMulti && !HasMultiKeyConflict && otherTemplates.Any(t =>
                Owner.GetAreaKey(t) != thisArea &&
                string.Equals(Owner.GetEffectiveMultiKey(t), thisEffective, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
