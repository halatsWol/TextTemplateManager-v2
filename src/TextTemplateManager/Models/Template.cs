using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextTemplateManager.Common;


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

        // The language fields come after Title (order 1), where older versions write them back as unknown fields.

        /// <summary>The language of <see cref="Content"/> (the first tab), or null when it isn't known.</summary>
        [ObservableProperty]
        [JsonPropertyOrder(2)]
        public partial string? Language { get; set; }

        /// <summary>The first tab's id, unique within the template; only while there are other languages.</summary>
        [ObservableProperty]
        [JsonPropertyOrder(3)]
        public partial string? LanguageId { get; set; }

        /// <summary>The other tabs, in tab order; null when there are none.</summary>
        [JsonPropertyOrder(4)]
        public IReadOnlyList<TemplateLanguage>? AltLanguages
        {
            get => _altLanguages;
            set
            {
                if (SetProperty(ref _altLanguages, value is { Count: > 0 } ? value : null))
                    OnPropertyChanged(nameof(HasLanguageConflict));
            }
        }
        private IReadOnlyList<TemplateLanguage>? _altLanguages;

        /// <summary>A language set on two tabs; only the first of them is offered. Transient.</summary>
        [JsonIgnore]
        public bool HasLanguageConflict => TemplateLanguages.HasDuplicate(this);

        /// <summary>The languages were kept here although the sync file came back without them (written by a version
        /// that doesn't know them): they aren't written back until the template is edited here. Transient.</summary>
        [JsonIgnore]
        public bool LanguagesKeptLocally { get; set; }

        /// <summary>While the languages were kept, the sync file brought a different first tab. Transient.</summary>
        [JsonIgnore]
        public bool DefaultChangedSinceLanguages { get; set; }

        /// <summary>The content of the tab with <paramref name="languageId"/>; the first tab for null or an unknown id.</summary>
        public string ContentFor(string? languageId) =>
            languageId == null || languageId == LanguageId
                ? Content
                : AltLanguages?.FirstOrDefault(a => a.Id == languageId)?.Content ?? Content;

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

        partial void OnLanguageChanged(string? value) => OnPropertyChanged(nameof(HasLanguageConflict));

        // Stores the cleaned value, so only that is ever announced. Raw input that cleans to the stored value
        // ("a" over "A") is still announced, so an editing TextBox shows the cleaned text again.
        private void SetShortcut(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            string clean = CleanShortcut(value);
            if (!SetProperty(ref field, clean, propertyName) && !string.Equals(value, clean, StringComparison.Ordinal))
                OnPropertyChanged(propertyName);
        }

        #endregion
    }
}
