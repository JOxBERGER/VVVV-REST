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
using System.Threading;
using System.Threading.Tasks;

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
		public string FileType;
		public Stream FileContent; //byte[] FileContent;
		public string FileName; 
	}
	
	public struct KeyValueParameter
	{
		public string Value;
		public string Key;
	}

	public struct Body
	{
		public string Type;
		public string Data;
	}
	
	public struct Header
	{
		public string HeaderName;
		public string HeaderValue;
	}

	#region PluginInfo
	[PluginInfo(Name = "HTTP-REST", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net", AutoEvaluate = true)]
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
		
		[Input("Header")]
		public ISpread<ISpread<Header>> FInputHeader;
		
		[Input("Key Value Parameter")]
		public ISpread<ISpread<KeyValueParameter>> FInputKeyValueParameter;
		
		[Input("Body")]
		public ISpread<Body> FInputBody;
		
		[Input("File Upload")]
		public ISpread<ISpread<FileUpload>> FInputFileUpload;
		
		[Input("Authentication")]
		public ISpread<IAuthenticator> FInputAuthentication;
		
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
		
		
		
		#endregion fields & pins-
		
		// Called when this plugin was created
		public void OnImportsSatisfied()
		{
			// Do any initialization logic here. In this example we won't need to
			// do anything special.
			FLogger.Log(LogType.Message, "Initialized HTTP REST Node.");
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
			SpreadMax = FInputBaseURL.SliceCount;
			
			// Set the slice counts of our outputs.
			FOutputHeader.SliceCount = SpreadMax;
			FOutputSuccess.SliceCount = SpreadMax;
			FOutputStatus.SliceCount = SpreadMax;
			FRAWOutputResponse.ResizeAndDispose(SpreadMax, () => new MemoryStream());			
			
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
				
				if (FInputExecute[i])
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
							var AddParameter = false;
							var AddBody = false;
							
							//##################
							//## Setup Client ##
							// Setup RestClient 
							client[index] = new RestClient();
							client[index].BaseUrl = new Uri(FInputBaseURL[index]);
							client[index].ClearHandlers();

							//####################
							//## Authentication ##
							//client[index].Authenticator = new OAuthAuthenticator();
							if(FInputAuthentication[index] != null)
							{
								FLogger.Log(LogType.Debug, "Auth!");
								client[index].Authenticator = FInputAuthentication[index];//new HttpBasicAuthenticator(FInputUsername[index], FInputPassword[index]);
							}
							FOutputStatus[index] = getTimestamp()+" - Client Setup.\n";
							
							
							//###################
							//## Setup Request ##
							request[index] = new RestRequest();
							request[index].Method = FInputHttpMethod[index]; // REST method eg. POST, GET, DELETE etc.
							//request[index].Parameters.Clear();
							
							
							//#################
							//## Adding Body ##
							if (FInputBody[index].Data != null)
							{
								FLogger.Log(LogType.Message, "Body length: " + FInputBody[index].Data.Length);
								AddBody = true;
								request[index].AddParameter(FInputBody[index].Type, FInputBody[index].Data, ParameterType.RequestBody);
							}
							
							
							//#################
							//## Adding File ##
							var FileData = FInputFileUpload[index];	
							for (int ii=0; ii<FileData.SliceCount; ii++)
							{
								if(FileData[ii].FileContent != null)
								{
									FLogger.Log(LogType.Message, "File Uplaod! Name:"+FileData[ii].FileName+" File Type:"+FileData[ii].FileType);
									//request[index].AddFile(FileData[ii].FileName, FileData[ii].FileContent, FileData[ii].FileName, FileData[ii].FileType);
									request[index].AddFile("name", BytestreamToArray(FileData[ii].FileContent), "name");
									//request[index].AddParameter("", BytestreamToArray(FileData[ii].FileContent), ParameterType.RequestBody); // tried to overcome forced multipart behaviert. 
								}				
							}

							//request[index].Parameters.Clear();
							
							//#######################
							//## Adding Parameters ##
							var Paramters = FInputKeyValueParameter[index];							
							for (int ii=0; ii<Paramters.SliceCount; ii++)
							{
								if(Paramters[ii].Key != null)
								{
									FLogger.Log(LogType.Message, "Parameter Key: " + Paramters[ii].Key +"  Value: "+ Paramters[ii].Value);
									AddParameter = true;
									request[index].AddParameter(Paramters[ii].Key, Paramters[ii].Value); // works!
								}
							}							
							
							
							//###################
							//## Adding Header ##
							var Headers = FInputHeader[index];							
							for (int ii=0; ii<Headers.SliceCount; ii++)
							{
								if(Headers[ii].HeaderName != null)
								{
									FLogger.Log(LogType.Message, "Header Name: " + Headers[ii].HeaderName +"  Value: "+ Headers[ii].HeaderValue);
									request[index].AddHeader(Headers[ii].HeaderName, Headers[ii].HeaderValue);
									//request[index].AddHeader(Headers[ii].HeaderName, Headers[ii].HeaderValue);
								}
							}
							
							//##################################
							//## Check for Body and Parameter ##
							/* Adding Parameter and Body at once is not possible. See also: https://groups.google.com/forum/#!topic/restsharp/3NVVMridDJ0 */
							if (AddParameter&&AddBody)
							{
								var warning = "\nSorry. You can't add Body and Parameter at once due to limitations of the used library. \n Body data will be ignored.\n\n";
								FLogger.Log(LogType.Error, "HTTP REST Node: " + warning);
								FOutputStatus[index] += warning; 
							}
					
							//request[index].Parameters.Clear();
							
							string infor = request[index].Files.ToString();
							FLogger.Log(LogType.Error, "FileCount: " + request[index].Files.Count);
							
							//#####################
							//## Execute Request ##							
							response[index] = client[index].Execute(request[index]);// TODO Add cancleation token here?
							
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
		[PluginInfo(Name = "HTTP_Basic_Authentication", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
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

		/* Oauth is still not testest properly and therefore commented out
		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth Step 1. Get Request Token", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_OAuth_Step1_Get_Request_Token : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Url", DefaultString = null)]
			public ISpread<string> FInputUrl;
			
			[Input("Consumer Key", DefaultString = null)]
			public ISpread<string> FInputConsumerKey;
			
			[Input("Consumer Secret", DefaultString = null)]
			public ISpread<string> FInputConsumerSecret;
			
			[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
			public IDiffSpread<bool> FInputExecute;
			
			[Output("Content")]
			public ISpread<string> FOutputContent;
			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputContent.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					if (FInputExecute[i])
					{
						FOutputContent[i] = null;
						
						var client = new RestClient(FInputUrl[i]);
						
						client.Authenticator = OAuth1Authenticator.ForRequestToken(FInputConsumerKey[i], FInputConsumerSecret[i]);
						
						var request = new RestRequest("", Method.POST);
						var response = client.Execute(request);
						FOutputContent[i] = response.Content;
					}					
				}		
			}	
		}
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth Step 2. Get Access", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_OAuth_Step2_Get_Access : IPluginEvaluate
		{		
			#region fields & pins
			[Input("Url", DefaultString = null)]
			public ISpread<string> FInputUrl;
			
			
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
			
			[Input("Execute", IsBang = true, DefaultValue = 0, IsSingle = false)]
			public IDiffSpread<bool> FInputExecute;
			
			[Output("Output")]
			public ISpread<string> FOutput;
			
			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutput.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					
					if (FInputExecute[i])
					{
						FOutput[i]=null;
						
						var client = new RestClient(FInputUrl[i]);
						var request = new RestRequest("", Method.POST);
						client.Authenticator = OAuth1Authenticator.ForAccessToken(FInputConsumerKey[i], FInputConsumerSecret[i], FInputToken[i], FInputTokenSecret[i], FInputTokenVerification[i]);
						var response = client.Execute(request);

						//request = new RestRequest("account/verify_credentials.xml");
						//client.Authenticator = OAuth1Authenticator.ForAccessToken(FInputConsumerKey[i], FInputConsumerSecret[i], _token, _secret_token);

						//response = FInputClient[i].Execute(request);

						//var url = FInputClient[i].BuildUri(request).ToString();	
						FOutput[i] = response.Content;	
					}					
				}		
			}	
		}

		
		#region PluginInfo
		[PluginInfo(Name = "HTTP OAuth 1.0 Authentication", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")] //AutoEvaluate = true
		#endregion PluginInfo
		public class HTTP_OAuth_1_Authentication : IPluginEvaluate
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
					//FOutputAuthentication[i] = OAuth2AuthorizationRequestHeaderAuthenticator(	
					FOutput[i] = FOutputAuthentication[i].ToString();
				}		
			}	
		}
*/
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP_Attach_File", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Attach_FileNode : IPluginEvaluate
		{		
			#region fields & pins		
			[Input("File")]
			public ISpread<Stream> FInputFile;
			
			[Input("File Type", DefaultString = "file")]
			public ISpread<string> FInputFileType;
			
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
						// When working with streams make sure to reset their position
						// before reading from them.
						var byteStream = FInputFile[i];
						byteStream.Position = 0; // resetting the byte stream position is important otherwise the stream won't be read in the next frame!
						
						var newFile = new FileUpload();
						newFile.FileType = FInputFileType[i];
						newFile.FileContent = byteStream; //BytestreamToArray(byteStream);
						newFile.FileName = FInputFileName[i];
						FOutputFileUpload[i] = newFile;
					}
					//FStreamOut.Flush(true);
					FOutputFileUpload.Flush(true);
			}	
		}

		#region PluginInfo
		[PluginInfo(Name = "HTTP_Add_KeyValueParameter", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Add_KeyValueParameter : IPluginEvaluate
		{		
			#region fields & pins		
			
			[Input("Key", DefaultString = "Key")]
			public ISpread<string> FInputParameterKey;
			
			[Input("Value", DefaultString = "Value")]
			public ISpread<string> FInputParameterValue;
			
			[Output("Key Value Parameter")]
			public ISpread<KeyValueParameter> FOutputParameter;

			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputParameter.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					var newParameter = new KeyValueParameter();
					newParameter.Key = FInputParameterKey[i];
					newParameter.Value = FInputParameterValue[i];
					FOutputParameter[i] = newParameter;
				}
			}	
		}
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP_Add_Header", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Add_Header : IPluginEvaluate
		{		
			#region fields & pins		
			
			[Input("Header Name", DefaultString = "Header Name")]
			public ISpread<string> FInputHeaderName;
			
			[Input("Header Value", DefaultString = "Header Value")]
			public ISpread<string> FInputHeaderValue;
			
			[Output("Key Value Parameter")]
			public ISpread<Header> FOutputHeader;

			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{	
				// Set the slice counts of our outputs.
				FOutputHeader.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					var newHeader = new Header();
					newHeader.HeaderName = FInputHeaderName[i];
					newHeader.HeaderValue = FInputHeaderValue[i];
					FOutputHeader[i] = newHeader;
				}
			}	
		}
		
		#region PluginInfo
		[PluginInfo(Name = "HTTP_Add_Body", Category = "Network", Version = "1.0", Help = "Interact with RESTful Web API's.", Credits = "Based on the wonderful RestSharp.org REST and HTTP API Client for .NET.", Tags = "REST, HTTP, NETWORK", Author = "Jochen Leinberger: explorative-environments.net")]
		#endregion PluginInfo
		public class HTTP_Add_Body : IPluginEvaluate
		{		
			#region fields & pins		
			
			[Input("Key", DefaultString = "text/plain")]
			public ISpread<string> FInputParameterType;
			
			[Input("Value", DefaultString = "Text")]
			public ISpread<string> FInputParameterData;
			
			[Output("Body")]
			public ISpread<Body> FOutputBody;

			[Import()]
			public ILogger FLogger;
			#endregion

			public void Evaluate(int SpreadMax)
			{		
				// Set the slice counts of our outputs.
				FOutputBody.SliceCount = SpreadMax;
				
				// start doing stuff foreach spread item
				for (int i = 0; i < SpreadMax; i++)
				{
					var newBody = new Body();
					newBody.Type= FInputParameterType[i];
					newBody.Data = FInputParameterData[i];
					FOutputBody[i] = newBody;
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
		
		public static byte[] BytestreamToArray(Stream input)
		{
			byte[] byteArray;
			var br = new BinaryReader(input);
			byteArray = br.ReadBytes((int)input.Length);
			return byteArray;
		}
	}
}
