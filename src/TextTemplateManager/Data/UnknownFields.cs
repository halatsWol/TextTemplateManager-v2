using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TextTemplateManager.Models;

namespace TextTemplateManager.Data;

/// <summary>Drops the unknown fields a loaded tree can't keep, so a file can never stop the app from saving.</summary>
internal static class UnknownFields
{
    // Below the writer's limit of 64: a cached or imported copy sits deeper in its file than the original did.
    private const int MaxDepth = 32;

    public static T? Clean<T>(T? root) where T : class
    {
        if (root is not BaseItem tree) return root;
        foreach (var item in tree.SelfAndDescendants())
        {
            if (item.UnknownFields is not { } fields) continue;
            // A '$' name only loads on a root, which isn't polymorphic; written on an item it makes the whole file unreadable.
            foreach (var (name, value) in fields.ToList())
                if (name.StartsWith('$') || !Writable(value)) fields.Remove(name);
            if (fields.Count == 0) item.UnknownFields = null;
        }
        return root;
    }

    // Some values load but can't be written, a lone surrogate escape for one: the read doesn't look inside them.
    private static bool Writable(JsonElement value)
    {
        try
        {
            using var writer = new Utf8JsonWriter(Stream.Null, new JsonWriterOptions { MaxDepth = MaxDepth });
            value.WriteTo(writer);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return false; }
    }
}
