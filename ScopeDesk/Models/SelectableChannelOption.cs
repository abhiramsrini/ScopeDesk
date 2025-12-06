using CommunityToolkit.Mvvm.ComponentModel;

namespace ScopeDesk.Models
{
    public class SelectableChannelOption : ObservableObject
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
