// Simple Node to interact with RESTful Web API's. Based on RestSharp.org.


#region usings
using System;
using System.ComponentModel.Composition;
using VVVV.PluginInterfaces.V2;

using VVVV.Core.Logging;
using RestSharp;
#endregion usings

// ToDo
//// Nice way to enter URL URI as in MQTT Node

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "HTTP-REST", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's. Based on RestSharp.org", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
	#endregion PluginInfo
	public class HTTP_RESTNode : IPluginEvaluate
	{
		#region enums
		public enum HttpMethod { GET, POST, PUT, DELETE }
		public enum RequestFormat { JSON, XML }
		#endregion enums
		
		#region fields & pins
		[Input("BaseURL", StringType = StringType.URL, DefaultString = "http://localhost")]
		public ISpread<string> FInputBaseURL;
		
		[Input("MimeType", DefaultString ="")]
		public ISpread<string> FInputMimeType;
		
		[Input("RequestFormat", DefaultEnumEntry = "JSON")]
		public ISpread<RestSharp.DataFormat> FInputRequestFormat;
		
		[Input("Message Body", DefaultString = null)]
		public ISpread<string> FInputMessageBody;
		
		[Input("Use Basic Authentication", IsBang = false, DefaultValue = 0, IsSingle = false)]
		IDiffSpread<bool> FInputUseBasicAuthentication;
		
		[Input("Username", DefaultString = null)]
		public ISpread<string> FInputUsername;
		
		[Input("Password", DefaultString = null)]
		public ISpread<string> FInputPassword;
	
		[Input("HttpMethod", DefaultEnumEntry = "GET")]
		public ISpread<RestSharp.Method> FInputHttpMethod;
		
		[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
		IDiffSpread<bool> FInputExecute;

		[Output("Output")]
		public ISpread<string> FOutputResponse;
		
		[Output("Header")]
        public ISpread<string>  FOutputHeader;
		
		[Output("Success", DefaultValue = 0)]
		public ISpread<bool> FOutputSuccess;
		
		[Output("Status")]
		public ISpread<string> FOutputStatus;

		[Import()]
		public ILogger FLogger;
		#endregion fields & pins

		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			
			FOutputResponse.SliceCount = SpreadMax;
			FOutputHeader.SliceCount = SpreadMax;
			FOutputSuccess.SliceCount = SpreadMax;
			FOutputStatus.SliceCount = SpreadMax;

			//// start doing stuff foreach spread item
			for (int i = 0; i < SpreadMax; i++)
			{
				
				try{
					
					
					if (FInputExecute[i])
					{
						// Clear Fields
						FOutputResponse[i] = "";
						FOutputHeader[i] = "";
						FOutputSuccess[i] = false;
						FOutputStatus[i] = "";
						
						var client = new RestClient();
						client.BaseUrl = new Uri(FInputBaseURL[i]);
						
						if(FInputUseBasicAuthentication[i])
						{
							client.Authenticator = new HttpBasicAuthenticator("username", "password");
						}
						
						
						var request = new RestRequest();
						request.Method = FInputHttpMethod[i]; // Set the REST method						
						request.RequestFormat = FInputRequestFormat[i];
						
						request.AddParameter(FInputMimeType[i], FInputMessageBody[i], ParameterType.RequestBody);
						
						
						
						IRestResponse response = client.Execute(request);
						
						FOutputResponse[i] = response.Content;
						
						if(response.StatusCode.ToString() == "OK" || response.StatusCode.ToString() == "Created")
						{
						FOutputSuccess[i] = true;
						}
						else
						{
						FOutputSuccess[i] = false;							
						}
						
						
						
						// get Complete Header
						int FHeaderItems = response.Headers.Count;
						if(FHeaderItems > 0)
						{
							FOutputHeader[i] = "";
							for(int j=0; j < FHeaderItems; j++)
							{
								FOutputHeader[i] += j.ToString()+" "+response.Headers[j]+"\n";
							}
						}
						
						
						FOutputStatus[i] += "Time:		"+getTimestamp()+"\n";
						FOutputStatus[i] += "ResponseStatus:	"+response.ResponseStatus+"\n";
						FOutputStatus[i] += "StatusDescription:	"+response.StatusDescription +"\n";
						FOutputStatus[i] += "StatusCode:	"+response.StatusCode.ToString() +"\n";
						FOutputStatus[i] += "Request:		"+response.Request.RequestFormat +"\n";
						FOutputStatus[i] += "ResponseUri:	"+response.ResponseUri +"\n";
						//FOutputStatus[i] += "Headers:	"+response.Headers[0];//Count.ToString() +"\n";
						
						//FOutputStatus[i] += "ErrorException:	"+response.ErrorException.Message.ToString() +"\n";
						//FOutputStatus[i] += "ErrorMessage:	"+response.ErrorMessage +"\n";
					}
					
				}
				catch (Exception e)
				{
					FLogger.Log(e);
					FOutputStatus[i] = e.Message.ToString();
					FOutputSuccess[i] = false;	
				}
				
			}	
		}
		
		private string getTimestamp()
		{
			string timeStamp = DateTime.Now.ToString();
			return timeStamp;
		}

		//FLogger.Log(LogType.Debug, "Logging to Renderer (TTY)");
	}
}
