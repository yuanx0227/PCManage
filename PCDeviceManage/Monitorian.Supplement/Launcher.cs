﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitorian.Supplement
{
	/// <summary>
	/// A wrapper class of <see cref="Windows.System.Launcher"/>
	/// </summary>
	public static class Launcher
	{
		/// <summary>
		/// Launches the default app associated with the URI scheme name for a specified URI.
		/// </summary>
		/// <param name="uri">Uri</param>
		/// <returns>True if successfully launches</returns>
		public static Task<bool> LaunchAsync(Uri uri)
		{
			return Windows.System.Launcher.LaunchUriAsync(uri).AsTask();
		}
	}
}