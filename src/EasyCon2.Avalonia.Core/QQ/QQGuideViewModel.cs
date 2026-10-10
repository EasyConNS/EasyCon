using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasyCon2.Avalonia.Core.QQ;

public partial class QQGuideViewModel : ObservableObject
{
    private int _selectedStepIndex;

    [ObservableProperty] private int _selectedSectionIndex;

    public IReadOnlyList<QQGuideStep> Steps => QQGuideCatalog.Steps;
    public QQGuideStep CurrentStep => Steps[SelectedStepIndex];
    public string StepProgress => $"第 {SelectedStepIndex + 1} / {Steps.Count} 步";
    public string NextStepLabel => SelectedStepIndex == Steps.Count - 1 ? "继续：接入 EasyCon →" : "下一步 →";
    public bool CanGoPrevious => SelectedStepIndex > 0;

    public int SelectedStepIndex
    {
        get => _selectedStepIndex;
        set
        {
            int index = Math.Clamp(value, 0, Steps.Count - 1);
            if (!SetProperty(ref _selectedStepIndex, index))
                return;
            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(StepProgress));
            OnPropertyChanged(nameof(NextStepLabel));
            OnPropertyChanged(nameof(CanGoPrevious));
            PreviousStepCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousStep() => SelectedStepIndex--;

    [RelayCommand]
    private void NextStep()
    {
        if (SelectedStepIndex < Steps.Count - 1)
            SelectedStepIndex++;
        else
            ShowConnection();
    }

    [RelayCommand]
    private void ShowConnection() => SelectedSectionIndex = 1;

    [RelayCommand]
    private void ShowScript() => SelectedSectionIndex = 2;
}