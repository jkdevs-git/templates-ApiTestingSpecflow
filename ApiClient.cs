using System;
using System.Collections.Generic;
using System.Threading;
using RestSharp;
using RestSharp.Authenticators;

namespace Helper
{
    public class ApiClient
    {
        public IAuthenticator Authenticator { get; set; }
        public string Resource { get; set; }
        public Method Verb { get; set; }
        public Uri Uri { get; set; }
        public ICollection<KeyValuePair<string, object>> Parameters { get; set; }
        public ICollection<KeyValuePair<string, string>> Headers { get; set; }
        public Dictionary<string, object> Body { get; set; }

        public IRestResponse SendRequest()
        {
            RestClient restClient = new();
            restClient.BaseUrl = Uri;

            if (Authenticator != null)
            {
                restClient.Authenticator = Authenticator;
            }

            RestRequest restRequest = new()
            {
                Resource = Resource,
                Method = Verb
            };

            if(Parameters != null)
            {
                foreach (var parameter in Parameters)
                {
                    if (parameter.Key == "application/json")
                        restRequest.AddParameter(parameter.Key, parameter.Value, ParameterType.RequestBody);
                    else restRequest.AddParameter(parameter.Key, parameter.Value);
                }
            }

            if (Headers != null)
            {
                restRequest.AddHeaders(Headers);
            }

            if (Body?.Count > 0)
            {
                if (Body.ContainsKey("JsonBody"))
                    restRequest.AddJsonBody(Body["JsonBody"]);
                else if (Body.ContainsKey("TextBody"))
                {
                    restRequest.AddHeader("Content-Type", "text/plain");
                    restRequest.AddParameter("text/xml", Body["TextBody"], ParameterType.RequestBody);
                }
            }
            Thread.Sleep(3000);
            var response = restClient.Execute(restRequest);
            if (!response.IsSuccessful) //One more try
                response = restClient.Execute(restRequest);
            return response;
        }
    }
}
