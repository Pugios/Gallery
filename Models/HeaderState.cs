using System;
using System.Collections.Generic;
using System.Text;
using Wpf.Ui.Controls;

namespace Gallery2.Models;

public partial class HeaderState : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _showComboBox;
    [ObservableProperty] private SymbolRegular _icon;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
}

