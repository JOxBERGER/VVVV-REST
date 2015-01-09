// Simple Node to interact with RESTful Web API's. Based on RestSharp.org.


#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;
//using System.Collections.Generic;
//using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Core.Logging;

using RestSharp;
using RestSharp.Authenticators;
//using RestSharp.Authenticators.OAuth;


using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;


#endregion usings

// Done make the requests asynchron.
// TODO setup client only if parameter changed.
// DONE RAW File hnadling.
// TODO Progress indication.
// TODO add cancelation for RESTSHARP handle.

namespace VVVV.Nodes
{
	public struct FileUpload
	{
		public string FileParameterName;
		public Stream FileContent;
		public string FileName; 
	}
	
	#region PluginInfo
	[PluginInfo(Name = "HTTP REST", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
	#endregion PluginInfo
	public class HTTP_RESTNode : IPluginEvaluate
	{
		#region enums
		public enum HttpMethod { GET, POST, PUT, DELETE }
		public enum RequestFormat { JSON, XML }
		#endregion enums
		
		#region fields & pins
		[Input("BaseURL", StringType = StringType.URL, DefaultString = "http://localhost")]
		public IDiffSpread<string> FInputBaseURL;
		
		[Input("MimeType", DefaultString ="")]
		public ISpread<string> FInputMimeType;
		
		[Input("RequestFormat", DefaultEnumEntry = "JSON")]
		public ISpread<RestSharp.DataFormat> FInputRequestFormat;
		
		[Input("Message Body", DefaultString = null)]
		public ISpread<string> FInputMessageBody;
		
		[Input("File Upload")]
		public ISpread<FileUpload> FInputFileUpload;
		
		/*
		[Input("File", Visibility = PinVisibility.Hidden)]
		public ISpread<Stream> FInputFile;
		
		[Input("File Parameter Name", DefaultString = "file", Visibility = PinVisibility.Hidden)]
		public ISpread<string> FInputFileParameterName;
		
		[Input("File Name", DefaultString = "vvvv.file", Visibility = PinVisibility.Hidden)]
		public ISpread<string> FInputFileName;
		*/
		
		//[Input("File Path", DefaultString = "/files/", Visibility = PinVisibility.Hidden)]
		//public ISpread<string> FInputFilePath;

		[Input("Authentication")]
		public ISpread<IAuthenticator> FInputAuthentication;

		/*
		[Input("Use Basic Authentication", IsBang = false, DefaultValue = 0, IsSingle = false)]
		public IDiffSpread<bool> FInputUseBasicAuthentication;
		
		[Input("Username", DefaultString = null)]
		public ISpread<string> FInputUsername;
		
		[Input("Password", DefaultString = null)]
		public ISpread<string> FInputPassword;
		*/
		
		[Input("HttpMethod", DefaultEnumEntry = "GET")]
		public ISpread<RestSharp.Method> FInputHttpMethod;
		
		[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
		public IDiffSpread<bool> FInputExecute;

		[Input("Cancel", IsBang = true, DefaultValue = 0, IsSingle = true)]
		public IDiffSpread<bool> FInputCancel;
		
		[Output("Output")]
		public ISpread<Stream> FRAWOutputResponse;
		
		[Output("Header")]
		public ISpread<string>  FOutputHeader;
		
		[Output("Success", DefaultValue = 0)]
		public ISpread<bool> FOutputSuccess;
		
		[Output("Status")]
		public ISpread<string> FOutputStatus;

		[Import()]
		public ILogger FLogger;

		// Setup Spreads for Tasks and Cancelation Tokens.
		readonly  Spread<Task> FTasks = new Spread<Task>();
		readonly Spread<CancellationTokenSource> FCts = new Spread<CancellationTokenSource>();
		readonly Spread<CancellationToken> ct = new Spread<CancellationToken>();
		int TaskCount = 0;
		
		// Setups specific Spreads
		readonly Spread<RestClient> client = new Spread<RestClient>();
		readonly Spread<RestRequest> request = new Spread<RestRequest>();
		readonly Spread<IRestResponse> response = new Spread<IRestResponse>();
		
		//when dealing with byte streams (what we call Raw in the GUI) it's always
		//good to have a byte buffer around. we'll use it when copying the data.
		//readonly byte[] FBuffer = new byte[1];

		#endregion fields & pins-
		
		// Called when this plugin was created
		public void OnImportsSatisfied()
		{
			// Do any initialization logic here. In this example we won't need to
			// do anything special.
			FLogger.Log(LogType.Message, "Initialized HTTP REST Node.");
			
			//start with an empty stream output
			//FRAWOutputResponse.SliceCount = 0;
		}

		// Called when this plugin gets deleted
		public void Dispose()
		{
			// Should this plugin get deleted by the user or should vvvv shutdown
			// we need to wait until all still running tasks ran to a completion
			// state.
			for (int i = 0; i < TaskCount; i++)
			{
				int index = i;
				FLogger.Log(LogType.Message, "Dispose task:"+index);
				CancelRunningTasks(index);
			}
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{	
			// Set the slice counts of our outputs.
			//FOutputResponse.SliceCount = SpreadMax;
			FOutputHeader.SliceCount = SpreadMax;
			FOutputSuccess.SliceCount = SpreadMax;
			FOutputStatus.SliceCount = SpreadMax;
			FRAWOutputResponse.SliceCount = SpreadMax;
			//var FInputAuthentication.SliceCount = SpreadMax;
			
			
			// Set slice count for Task and cancelation Tokens.
			FTasks.SliceCount = SpreadMax;
			FCts.SliceCount = SpreadMax;
			ct.SliceCount = SpreadMax;
			TaskCount = SpreadMax;
			
			// Set Spread count for specific objects
			client.SliceCount = SpreadMax;
			request.SliceCount = SpreadMax;
			response.SliceCount = SpreadMax;
			
			
			//ResizeAndDispose will adjust the spread length and thereby call
			//the given constructor function for new slices and Dispose on old
			//slices.
			//FRAWOutputResponse.ResizeAndDispose(SpreadMax, () => new MemoryStream());
			
			// start doing stuff foreach spread item
			for (int i = 0; i < SpreadMax; i++)
			{
				// store i to a new variable so it won't change when tasks are running over longer period.
				// this is important to asign results to the right slice position.
				int index = i;
				
				if (FInputCancel[index])
				{
					CancelRunningTasks(index);
				}
				
				if (FInputExecute[index])
				{
					
					// Clear Fields
					//FOutputResponse[index] = "";
					FOutputHeader[index] = "";
					FOutputSuccess[index] = false;
					FOutputStatus[index] = "";
					FRAWOutputResponse[index] = null;

					// Let's first cancel all running tasks (if any).
					CancelRunningTasks(index);
					
					// Create a new task cancellation source object.
					FCts[index] = new CancellationTokenSource();
					// Retrieve the cancellation token from it which we'll use for
					// the new tasks we setup up now.
					ct[index] = FCts[index].Token;		
					
					FTasks[index] = Task.Factory.StartNew(() => 
					{
						// Should a cancellation be requested throw the task
						// canceled exception.
						ct[index].ThrowIfCancellationRequested();
						
						
						// >>> Start the Work
						try 
						{
							
							// Setup RestClient 
							client[index] = new RestClient();
							client[index].BaseUrl = new Uri(FInputBaseURL[index]);
							// Setup Authentication
							
							if(FInputAuthentication[index] != null)
							{
								FLogger.Log(LogType.Debug, "Auth!");
								client[index].Authenticator = FInputAuthentication[index];//new HttpBasicAuthenticator(FInputUsername[index], FInputPassword[index]);
							}
							
							
							FOutputStatus[index] = getTimestamp()+" - Client Setup.\n";
							
							// Setup Request
							request[index] = new RestRequest();
							request[index].Method = FInputHttpMethod[index]; // REST method eg. POST, GET, DELETE etc.
							request[index].RequestFormat = FInputRequestFormat[index]; // Request Format Xml, Json
							request[index].AddParameter(FInputMimeType[index], FInputMessageBody[index], ParameterType.RequestBody); // Mimeype and Body
							
							if(FInputFileUpload[index].FileContent != null)
							{
								FLogger.Log(LogType.Debug, "File Upload !");
								request[index].AddFile(FInputFileUpload[index].FileParameterName, ReadFully(FInputFileUpload[index].FileContent), FInputFileUpload[index].FileName, FInputMimeType[index]); // not sure about the parameter type here ?								
							}
							
							// Execute the Request
							//response[index] = client[index].ExecuteAsync(request[index]); // TODO Add cancleation token here?
							//var response = client.Execute(request);
							response[index] = client[index].Execute(request[index]);	
							//response[index] = client[index].Delete(request[index]);
						}
						catch (Exception e)
						{
							FLogger.Log(LogType.Debug, "HTTP-REST Exception:" + e);
							FOutputStatus[index] += e;
						}
						// <<< Done the Work 
						
						
						
						// Return the results
						return new {RESTResponse = response[index]};
						

					}, ct[index]).ContinueWith(t => 
					{
						//
						// Assign results to the output of vvvv.
						//
						
						// Get the response Body.
						var result = t.Result.RESTResponse;
						
						FRAWOutputResponse[index] = new MemoryStream(result.RawBytes);
						
						// get Complete Header
						int FHeaderItems = result.Headers.Count;
						if(FHeaderItems > 0)
						{
							FOutputHeader[index] = "";
							for(int j=0; j < FHeaderItems; j++)
							{
								FOutputHeader[index] += j.ToString()+" "+result.Headers[j]+"\n";
							}
						}		

						FOutputStatus[index] += getTimestamp()+" - Response.\n";
						FOutputStatus[index] += "ResponseStatus:	"+result.ResponseStatus+"\n";
						FOutputStatus[index] += "StatusDescription:	"+result.StatusDescription +"\n";
						FOutputStatus[index] += "StatusCode:	"+result.StatusCode.ToString() +"\n";
						FOutputStatus[index] += "Request:		"+result.Request.RequestFormat +"\n";
						FOutputStatus[index] += "ResponseUri:	"+result.ResponseUri +"\n";	
						
						if (result.StatusCode == System.Net.HttpStatusCode.OK || result.StatusCode == System.Net.HttpStatusCode.Created) // ToString() == "OK" || t.Result.RESTResponse.StatusCode.ToString() == "Created")
						{
							FOutputSuccess[index] = true;
						}
						else
						{
							FOutputSuccess[index] = false;							
						}		
						
						
					},ct[index],
					// Here we can specify some options under which circumstances the
					// continuation should run. In this case we only want it to run if
					// the task wasn't cancelled before.
					TaskContinuationOptions.OnlyOnRanToCompletion,
					// This way we tell the continuation to run on the main thread of vvvv.
					TaskScheduler.FromCurrentSynchronizationContext()
					);
				}
			}
		}
		
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP Basic Authentication", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Basic_AuthenticationNode : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Username", DefaultString = null)]
			public ISpread<string> FInputUsername;
			
			[Input("Password", DefaultString = null)]
			public ISpread<string> FInputPassword;
			
			[Output("Authentication")]
			public ISpread<IAuthenticator> FOutputAuthentication;
			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputAuthentication.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					//client[i] = new RestClient();
					FOutputAuthentication[i] = new HttpBasicAuthenticator(FInputUsername[i], FInputPassword[i]);
				}		
			}	
		}

		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth 01", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_OAuth_01Node : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Consumer Key", DefaultString = null)]
			public ISpread<string> FInputConsumerKey;
			
			[Input("Consumer Secret", DefaultString = null)]
			public ISpread<string> FInputConsumerSecret;
			
			[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
			public IDiffSpread<bool> FInputExecute;
			
			[Output("Verification URL")]
			public ISpread<string> FOutputVerificationUrl;
			
			[Output("Token")]
			public ISpread<string> FOutputToken;
			
			[Output("Token Secret")]
			public ISpread<string> FOutputTokenSecret;
			
			[Output("Client")]
			public ISpread<RestClient> FOutputClient;
			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputVerificationUrl.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					if (FInputExecute[i])
					{
						FOutputVerificationUrl[i]=null;

						var client = new RestClient("https://api.twitter.com");
						client.Authenticator = OAuth1Authenticator.ForRequestToken(FInputConsumerKey[i], FInputConsumerSecret[i], "http://explorative-environments.net/");
						
						var request = new RestRequest("/oauth/request_token", Method.POST);
						var response = client.Execute(request);
						
						//var qs = HttpUtility.ParseQueryString(response.Content);
						string _token = null;
						Regex findToken1 = new Regex(@"oauth_token=(.*?)&");
						Match matchedToken1 = findToken1.Match(response.Content);
						if (matchedToken1.Success)
						{
							_token=matchedToken1.Groups[1].Value;
							FOutputToken[i] = _token;
						}
						
						string _secret_token = null; 
						Regex findToken2 = new Regex(@"oauth_token_secret=(.*?)&");
						Match matchedToken2 = findToken2.Match(response.Content);
						if (matchedToken2.Success)
						{
							_secret_token=matchedToken2.Groups[1].Value;
							FOutputTokenSecret[i] =  _secret_token;
						}
						request = new RestRequest("oauth/authorize");
						request.AddParameter("oauth_token", _token);

						var url = client.BuildUri(request).ToString();	
						FOutputVerificationUrl[i] = url;
						FOutputClient[i] = client;
					}					
				}		
			}	
		}
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth 02", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_OAuth_02Node : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Consumer Key", DefaultString = null)]
			public ISpread<string> FInputConsumerKey;
			
			[Input("Consumer Secret", DefaultString = null)]
			public ISpread<string> FInputConsumerSecret;
			
			[Input("Token", DefaultString = null)]
			public ISpread<string> FInputToken;
			
			[Input("Token Secret", DefaultString = null)]
			public ISpread<string> FInputTokenSecret;
			
			[Input("Token Verification", DefaultString = null)]
			public ISpread<string> FInputTokenVerification;
			
			[Input("Client")]
			public ISpread<RestClient> FInputClient;
			
			[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
			public IDiffSpread<bool> FInputExecute;
			
			[Output("Token")]
			public ISpread<string> FOutputToken;
			
			[Output("Token Secret")]
			public ISpread<string> FOutputTokenSecret;
			
			[Output("Output")]
			public ISpread<string> FOutput;
			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutput.SliceCount = SpreadMax;
				FOutputToken.SliceCount = SpreadMax;
				FOutputTokenSecret.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					if (FInputExecute[i])
					{
						FOutput[i]=null;
						
						var request = new RestRequest("oauth/access_token", Method.POST);
						FInputClient[i].Authenticator = OAuth1Authenticator.ForAccessToken(FInputConsumerKey[i], FInputConsumerSecret[i], FInputToken[i], FInputTokenSecret[i], FInputTokenVerification[i]);
            			var response = FInputClient[i].Execute(request);

						
						//var qs = HttpUtility.ParseQueryString(response.Content);
						string _token = null;
						Regex findToken1 = new Regex(@"oauth_token=(.*?)&");
						Match matchedToken1 = findToken1.Match(response.Content);
						if (matchedToken1.Success)
						{
							_token=matchedToken1.Groups[1].Value;
							FOutputToken[i] = _token;
						}
						
						string _secret_token = null; 
						Regex findToken2 = new Regex(@"oauth_token_secret=(.*?)&");
						Match matchedToken2 = findToken2.Match(response.Content);
						if (matchedToken2.Success)
						{
							_secret_token=matchedToken2.Groups[1].Value;
							FOutputTokenSecret[i] = _secret_token;
						}
						
						//request = new RestRequest("account/verify_credentials.xml");
            			FInputClient[i].Authenticator = OAuth1Authenticator.ForAccessToken(FInputConsumerKey[i], FInputConsumerSecret[i], _token, _secret_token);

			            //response = FInputClient[i].Execute(request);

						//var url = FInputClient[i].BuildUri(request).ToString();	
						FOutput[i] = response.Content;	
					}					
				}		
			}	
		}

		
		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_OAuthNode : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Consumer Key", DefaultString = null)]
			public ISpread<string> FInputConsumerKey;
			
			[Input("Consumer Secret", DefaultString = null)]
			public ISpread<string> FInputConsumerSecret;
			
			[Input("Token", DefaultString = null)]
			public ISpread<string> FInputToken;
			
			[Input("Token Secret", DefaultString = null)]
			public ISpread<string> FInputTokenSecret;
						
			[Output("Token")]
			public ISpread<string> FOutputToken;
			
			[Output("Token Secret")]
			public ISpread<string> FOutputTokenSecret;
			
			[Output("Output")]
			public ISpread<string> FOutput;
			
			[Output("Authentication")]
			public ISpread<IAuthenticator> FOutputAuthentication;
						
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutput.SliceCount = SpreadMax;
				FOutputAuthentication.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
						FOutput[i]=null;
						FOutputAuthentication[i] = null;
						//var request = new RestRequest("oauth/access_token", Method.POST);
						FOutputAuthentication[i]= OAuth1Authenticator.ForProtectedResource(FInputConsumerKey[i], FInputConsumerSecret[i], FInputToken[i], FInputTokenSecret[i]);
						//FOutput[i] = FOutputAuthentication[i].GetType().ToString();
				}		
			}	
		}

		#region PluginInfo
		[PluginInfo(Name = "HTTP Attach File", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Attach_FileNode : IPluginEvaluate
		{		
			#region fields & pins		
			[Input("File")]
			public ISpread<Stream> FInputFile;
			
			[Input("File Parameter Name", DefaultString = "file")]
			public ISpread<string> FInputFileParameterName;
			
			[Input("File Name", DefaultString = "vvvv.file")]
			public ISpread<string> FInputFileName;
			
			[Output("File Upload")]
			public ISpread<FileUpload> FOutputFileUpload;

			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputFileUpload.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					FileUpload newFile;
					newFile.FileParameterName = FInputFileParameterName[i];
					newFile.FileContent = FInputFile[i];
					newFile.FileName = FInputFileName[i];
					FOutputFileUpload[i] = newFile;
				}		
			}	
		}
		
		
		// Worker and Helper Methods 
		private void CancelRunningTasks(int index)
		{
			if (FCts[index] != null)
			{
				// All our running tasks use the cancellation token of this cancellation
				// token source. Once we call cancel the ct.ThrowIfCancellationRequested()
				// will throw and the task will transition to the canceled state.
				FCts[index].Cancel();
				
				// Dispose the cancellation token source and set it to null so we know
				// to setup a new one in a next frame.
				FCts[index].Dispose();
				FCts[index] = null;
				
			}
		}
		
		private string getTimestamp()
		{
			string timeStamp = DateTime.Now.ToString();
			return timeStamp;
		}
		
		public static byte[] ReadFully(Stream input)
		{
			byte[] buffer = new byte[16*1024];
			using (MemoryStream ms = new MemoryStream())
			{
				int read;
				while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					ms.Write(buffer, 0, read);
				}
				return ms.ToArray();
			}
		}
		//FLogger.Log(LogType.Debug, "Logging to Renderer (TTY)");
	}
}
