using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextTemplateManager.Common;

namespace TextTemplateManager.Models
{
    [JsonDerivedType(typeof(Folder), typeDiscriminator: "folder")]
    [JsonDerivedType(typeof(Template), typeDiscriminator: "template")]
    public abstract partial class BaseItem : ObservableObject
    {
        // Written last, as the field-based [ObservableProperty] used to put it; the file format (shared
        // with 1.x via the sync files) must not change. Pinned by FileFormatTests.
        [ObservableProperty]
        [JsonPropertyOrder(1)]
        public partial string Title { get; set; } = string.Empty;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ParentId { get; set; }

        /// <summary>For synced items: the Id as stored in the sync file (null for local items).
        /// <see cref="Id"/> stays the app-wide id; SyncId maps back to the shared file's entry.</summary>
        public Guid? SyncId { get; set; }
        public ItemType ItemType { get; set; }
        public string LastChange { get; set; } = DateTime.Now.ToString("yyyyMMddHHmmss");

        public ObservableCollection<BaseItem> Children { get; set; } = new();

        /// <summary>Fields of a newer version's file. Written back behind Title, where that version puts them
        /// (see the data format page), so the file stays byte-identical. Null when there are none.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnknownFields { get; set; }

        /// <summary>A new item with this one's content under <paramref name="title"/>, without children.</summary>
        public BaseItem DuplicateAs(string title)
        {
            BaseItem copy = this is Template t
                ? new Template
                {
                    Title = title,
                    Content = t.Content,
                    TagsCsv = t.TagsCsv,
                    DefaultPasteMode = t.DefaultPasteMode,
                    SingleKeyShortcut = t.SingleKeyShortcut,
                    MultiKeyShortcut = t.MultiKeyShortcut,
                }
                : new Folder { Title = title };
            if (UnknownFields != null) copy.UnknownFields = new(UnknownFields);
            return copy;
        }

        // Screen readers name Quick Paste's tree rows by it (their content is the item itself).
        public override string ToString() => Title;

        /// <summary>Top-level folder representing a sync source. Transient; drives the icon badge.</summary>
        [JsonIgnore]
        public bool IsSyncRoot { get; set; }

        /// <summary>Sync-root folder whose file is missing; cached content still shown. Transient.</summary>
        [JsonIgnore]
        public bool SyncFileMissing { get; set; }

        /// <summary>Sync-root folder whose file couldn't be written: the message for the tree's warning, or null.
        /// Transient.</summary>
        [ObservableProperty]
        [JsonIgnore]
        public partial string? SyncWriteProblem { get; set; }

        [JsonIgnore]
        public bool HasSyncWriteProblem => SyncWriteProblem != null;

        partial void OnSyncWriteProblemChanged(string? value) => OnPropertyChanged(nameof(HasSyncWriteProblem));
    }
}