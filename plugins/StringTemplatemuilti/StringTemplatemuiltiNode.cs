#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "Templatemuilti", Category = "String", Help = "Basic template with one string in/out", Tags = "")]
	#endregion PluginInfo
	public class StringTemplatemuiltiNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input", DefaultString = "hello c#")]
		public ISpread<string> FInput;
		


		[Output("Output")]
		public ISpread<string> FOutput;
		
		[Output("object")]
		public ISpread<object> FOutputObject;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			FOutput.SliceCount = SpreadMax;
			FOutputObject.SliceCount = SpreadMax;

			for (int i = 0; i < SpreadMax; i++)
			{
				FOutput[i] = FInput[i].Replace("c#", "vvvv");
				FOutputObject[i] = FInput[i].Replace("c#", "vvvv");
				
			}

			//FLogger.Log(LogType.Debug, "Logging to Renderer (TTY)");
		}
	}
	
	#region PluginInfo
	[PluginInfo(Name = "Result", Category = "String", Help = "Basic template with one string in/out", Tags = "")]
	#endregion PluginInfo
	public class StringResultNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input", DefaultString = "hello c#")]
		public ISpread<object> FInput;

		[Output("Output")]
		public ISpread<string> FOutput;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			FOutput.SliceCount = SpreadMax;

			for (int i = 0; i < SpreadMax; i++)
				FOutput[i] = FInput[i].ToString();

			//FLogger.Log(LogType.Debug, "Logging to Renderer (TTY)");
		}
	}
}
