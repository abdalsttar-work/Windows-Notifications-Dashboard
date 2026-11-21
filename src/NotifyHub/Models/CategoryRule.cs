using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NotifyHub.Models;

public class CategoryRule : INotifyPropertyChanged
{
    private int _id;
    private string _categoryName = "Other";
    private RuleMatchType _matchType = RuleMatchType.AppNameContains;
    private string _pattern = string.Empty;
    private int _priority = 10;
    private bool _isActive = true;

    public int Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string CategoryName
    {
        get => _categoryName;
        set => SetField(ref _categoryName, value);
    }

    public RuleMatchType MatchType
    {
        get => _matchType;
        set => SetField(ref _matchType, value);
    }

    public string Pattern
    {
        get => _pattern;
        set => SetField(ref _pattern, value);
    }

    public int Priority
    {
        get => _priority;
        set => SetField(ref _priority, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
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
