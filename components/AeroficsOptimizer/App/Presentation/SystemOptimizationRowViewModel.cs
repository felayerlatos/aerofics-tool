using System.ComponentModel;
using SystemOptimizer.Windows.SystemTools;

namespace SystemOptimizer.App.Presentation;

internal sealed class SystemOptimizationRowViewModel : INotifyPropertyChanged
{
	private string _stateLabel;

	private bool _isBusy;

	public string Id { get; }

	public string Title { get; }

	public string Description { get; }

	public string RiskLabel { get; }

	public bool RequiresRestart { get; }

	public bool IsApplied { get; }

	public bool CanRestore { get; }

	public string ApplyLabel { get; }

	public string StateLabel
	{
		get
		{
			return _stateLabel;
		}
		set
		{
			if (!(_stateLabel == value))
			{
				_stateLabel = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("StateLabel"));
			}
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (_isBusy != value)
			{
				_isBusy = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsBusy"));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public SystemOptimizationRowViewModel(SystemOptimizationDescriptor descriptor)
	{
		Id = descriptor.Id;
		Title = descriptor.Title;
		Description = descriptor.Description;
		RiskLabel = descriptor.RiskLabel;
		RequiresRestart = descriptor.RequiresRestart;
		IsApplied = descriptor.IsApplied;
		_stateLabel = descriptor.StateLabel;
		CanRestore = descriptor.CanRestore;
		ApplyLabel = descriptor.ApplyLabel;
	}
}
