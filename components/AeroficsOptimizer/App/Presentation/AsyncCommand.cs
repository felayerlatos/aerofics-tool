#define TRACE
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SystemOptimizer.App.Presentation;

public sealed class AsyncCommand : ICommand
{
	private readonly Func<object?, Task> _execute;

	private bool _isExecuting;
	private readonly bool _requiresAdministrator;
	private static bool _mutationBusy;
	private static event Action? MutationStateChanged;

	public event EventHandler? CanExecuteChanged;

	public AsyncCommand(Func<Task> execute, bool requiresAdministrator = false)
		: this((object? _) => execute(), requiresAdministrator)
	{
		ArgumentNullException.ThrowIfNull(execute, "execute");
	}

	public AsyncCommand(Func<object?, Task> execute, bool requiresAdministrator = false)
	{
		_execute = execute ?? throw new ArgumentNullException("execute");
		_requiresAdministrator = requiresAdministrator;
		MutationStateChanged += () => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}

	public bool CanExecute(object? parameter)
	{
		return !_isExecuting && (!_requiresAdministrator || (App.IsWriteAllowed && !_mutationBusy));
	}

	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter))
		{
			return;
		}
		_isExecuting = true;
		if (_requiresAdministrator) { _mutationBusy = true; MutationStateChanged?.Invoke(); }
		CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		try
		{
			await _execute(parameter);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Trace.TraceError("Unhandled command failure: {0}", ex2);
			MessageBox.Show("操作未完成，系统没有把这次操作报告为成功。\n\n" + ex2.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			_isExecuting = false;
			if (_requiresAdministrator) { _mutationBusy = false; MutationStateChanged?.Invoke(); }
			CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
