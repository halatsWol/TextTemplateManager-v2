using System.Text.Json.Serialization;
using TextTemplateManager.Models;

namespace TextTemplateManager.Data;

/// <summary>Compile-time JSON metadata for the app's own files and the sync files. The options
/// (indentation, escaping) come from <see cref="StorageService"/>.</summary>
[JsonSerializable(typeof(Folder))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SyncSettings))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
