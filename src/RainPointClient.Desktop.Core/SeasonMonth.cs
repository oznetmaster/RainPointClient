// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.ComponentModel;
namespace RainPointClient.Desktop.Core;

public sealed class SeasonMonth : INotifyPropertyChanged
	{
	internal SeasonMonth (string name)
		{
		Name = name;
		}
	public string Name
		{
		get;
		}
	private string _value = string.Empty;
	public string Value
		{
		get => _value;
		set
			{
			_value = value ?? string.Empty;
			PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (nameof (Value)));
			}
		}
	public event PropertyChangedEventHandler? PropertyChanged;
	}