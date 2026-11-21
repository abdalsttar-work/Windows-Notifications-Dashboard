using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotifyHub.Models;

public class Category : INotifyPropertyChanged
{
    private int _id;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _iconGlyph = "📁";
    private string _colorHex = "#4A90E2";
    private bool _isSystem;
    private int _notificationCount;

    public int Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetField(ref _iconGlyph, value);
    }

    public string ColorHex
    {
        get => _colorHex;
        set => SetField(ref _colorHex, value);
    }

    public bool IsSystem
    {
        get => _isSystem;
        set => SetField(ref _isSystem, value);
    }

    public int NotificationCount
    {
        get => _notificationCount;
        set => SetField(ref _notificationCount, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
