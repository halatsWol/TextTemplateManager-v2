using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
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