using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DeskDuck.Models;

namespace DeskDuck.ViewModels;

/// <summary>One row in the trigger checklist.</summary>
public sealed class TriggerEntry : INotifyPropertyChanged
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public string ProcessName { get; }
    public string Label { get; }

    /// <summary>App icon. Null = text only.</summary>
    public ImageSource? Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public TriggerEntry(string processName, string label, bool selected, Action onChanged, ImageSource? icon = null)
    {
        ProcessName = processName;
        Label = label;
        _isSelected = selected;
        _onChanged = onChanged;
        Icon = icon;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
