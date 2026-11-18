using Microsoft.UI.Xaml.Data;
using System;
using TextTemplateManager.Models;

namespace TextTemplateManager.Helpers
{
    /// <summary>The tree icon of an item: the Folder or Document glyph of the symbol font, as on the Add
    /// buttons. Sync roots add their badge in the template.</summary>
    public partial class ItemIconConverter : IValueConverter
    {
        public const string FolderGlyph = "";
        public const string TemplateGlyph = "";

        public static string For(BaseItem? item) => item is Folder ? FolderGlyph : TemplateGlyph;

        public object Convert(object value, Type targetType, object parameter, string language) => For(value as BaseItem);

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
