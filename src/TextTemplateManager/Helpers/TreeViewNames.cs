using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TextTemplateManager.Helpers
{
    public static class TreeViewNames
    {
        /// <summary>Screen readers meet a TreeView as its inner list, which doesn't take the TreeView's own name:
        /// gives that list <paramref name="name"/> once the tree is loaded.</summary>
        public static void NameForScreenReaders(TreeView tree, string name) =>
            tree.Loaded += (_, _) =>
            {
                if (FindList(tree) is TreeViewList list) AutomationProperties.SetName(list, name);
            };

        private static TreeViewList? FindList(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TreeViewList list) return list;
                if (FindList(child) is TreeViewList nested) return nested;
            }
            return null;
        }
    }
}
