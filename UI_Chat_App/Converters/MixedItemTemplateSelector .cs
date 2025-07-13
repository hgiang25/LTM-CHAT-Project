using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ChatApp.Models;

namespace UI_Chat_App
{
    public class MixedItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate UserTemplate { get; set; }
        public DataTemplate GroupTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is UserData)
                return UserTemplate;
            if (item is GroupData)
                return GroupTemplate;

            return base.SelectTemplate(item, container);
        }
    }
}
