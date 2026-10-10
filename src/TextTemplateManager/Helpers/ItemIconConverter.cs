using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using TextTemplateManager.Models;

namespace TextTemplateManager.Helpers
{
    /// <summary>The tree icon of an item: a folder or a document. Sync roots add their badge in the template.</summary>
    public partial class ItemIconConverter : IValueConverter
    {
        public static Symbol For(BaseItem? item) => item is Folder ? Symbol.Folder : Symbol.Document;

        public object Convert(object value, Type targetType, object parameter, string language) => For(value as BaseItem);

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
