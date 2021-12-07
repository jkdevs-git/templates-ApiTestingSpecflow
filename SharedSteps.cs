using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Authenticators;
using TechTalk.SpecFlow;

namespace Features
{
    [Binding]
    public class SharedSteps
    {

        private readonly ScenarioContext _scenarioContext;

        public SharedSteps(ScenarioContext scenarioContext)
        {
            _scenarioContext = scenarioContext;
        }

        [Given(@"Set request parameters based on (.*)")]
        public void SetRequestParameters(string query)
        {
            var parameters = new List<KeyValuePair<string, object>>();
            if (_scenarioContext.ContainsKey("Request.Parameters"))
                parameters.AddRange((List<KeyValuePair<string, object>>)_scenarioContext["Request.Parameters"]);
            if (!string.IsNullOrEmpty(query) && query.Trim().ToLower() != "none")
                parameters.AddRange(Functions.ConvertQueryParametersToKeyValuePairs(query, _scenarioContext));
            _scenarioContext["Request.Parameters"] = parameters;
            _scenarioContext["InputParameters"] = parameters;
        }

        [Given("Set (.*) to the request (.*)")]
        public void SetRawJsonToRequestJsonBody(string body, string bodytype)
        {
            if (string.IsNullOrEmpty(body)) return;
            switch (bodytype.ToLower())
            {
                case "jsonbody":
                    _scenarioContext["JsonData"] = body;
                    _scenarioContext["Request.RawJsonBody"] = body;
                    break;
                case "body":
                    _scenarioContext["Request.Body"] = body;
                    break;
                case "data":
                    _scenarioContext["Request.data"] = body;
                    break;
            }
        }

        [Given("Convert (.*) to (.*) and set request jsonbody")]
        public void SetRequestJsonBody(string jsonbody, string obj)
        {
            if (string.IsNullOrEmpty(jsonbody)) return;
            var body = Functions.SetPropertyValue(obj, Functions.ConvertJsonBodyToKeyValuePairs(jsonbody, _scenarioContext));
            _scenarioContext["Request.JsonBody"] = body;
            _scenarioContext["Data"] = body;
        }

        [Given("Convert (.*) to (.*) and update (.*) in request jsonbody")]
        public void UpdateRequestJsonBody(string namevalues, string obj, string parentproperty)
        {
            if (string.IsNullOrEmpty(namevalues) || !_scenarioContext.ContainsKey("Request.JsonBody")) return;
            var value = Functions.SetPropertyValue(obj, Functions.ConvertJsonBodyToKeyValuePairs(namevalues, _scenarioContext));
            var childbody = new KeyValuePair<string, object>(parentproperty, value);
            var body = Functions.UpdatePropertyValue(_scenarioContext["Request.JsonBody"], childbody);

            _scenarioContext["Request.JsonBody"] = body;
            _scenarioContext["Data"] = body;
        }

        [Given(@"Send a (.*) request to url \""(.*)\""")]
        [When(@"Send a (.*) request to url \""(.*)\""")]
        public void SendRequest(string verb, string resource)
        {
            string baseurl = "";

            //Update Ids in Url
            if (resource.Contains("{"))
            {
                foreach (var id in _scenarioContext)
                {
                    if (!resource.Contains(id.Key)) continue;
                    resource = resource.Replace("{" + $"{id.Key}" + "}", id.Value.ToString());
                }
            }

            var apiClient = new ApiClient
            {
                Uri = new Uri(baseurl),
                Authenticator = _scenarioContext.ContainsKey("Request.Authentication") && (bool)_scenarioContext["Request.Authentication"] ?
                    new HttpBasicAuthenticator(_scenarioContext["UserEmail"].ToString(), _scenarioContext["Password"].ToString()) : null,
                Headers = _scenarioContext.ContainsKey("Request.Headers") ? (ICollection<KeyValuePair<string, string>>)_scenarioContext["Request.Headers"] : null,
                Parameters = _scenarioContext.ContainsKey("Request.Parameters") ? (ICollection<KeyValuePair<string, object>>)_scenarioContext["Request.Parameters"] : null,
                Resource = resource,
                Verb = Enum.Parse<Method>(verb.ToUpper()),
                Body = new Dictionary<string, object>()
            };

            if (_scenarioContext.ContainsKey("Request.JsonBody"))
            {
                apiClient.Body.Add("JsonBody", _scenarioContext["Request.JsonBody"]);
            }
            else if (_scenarioContext.ContainsKey("Request.Body"))
            {
                apiClient.Body.Add("TextBody", _scenarioContext["Request.Body"]);
            }
            else if (_scenarioContext.ContainsKey("Request.RawJsonBody"))
            {
                apiClient.Parameters ??= new List<KeyValuePair<string, object>>();
                apiClient.Parameters.Add(new KeyValuePair<string, object>("application/json", _scenarioContext["Request.RawJsonBody"]));
            }

            var response = apiClient.SendRequest();

            if (response != null)
            {
                _scenarioContext["Response.StatusCode"] = response.StatusCode;
                _scenarioContext["Response.Content"] = response.Content;
            }

            //Clear used request
            if (_scenarioContext.ContainsKey("Request.Parameters")) _scenarioContext.Remove("Request.Parameters");
            if (_scenarioContext.ContainsKey("Request.Authentication")) _scenarioContext.Remove("Request.Authentication");
            if (_scenarioContext.ContainsKey("Request.Headers")) _scenarioContext.Remove("Request.Headers");
            if (_scenarioContext.ContainsKey("Request.RawJsonBody")) _scenarioContext.Remove("Request.RawJsonBody");
            if (_scenarioContext.ContainsKey("Request.JsonBody")) _scenarioContext.Remove("Request.JsonBody");
            if (_scenarioContext.ContainsKey("Request.Body")) _scenarioContext.Remove("Request.Body");
        }

        [Then(@"Verify response code is (.*)")]
        public void ThenVerifyResponseCodeIs(string responseCode)
        {
            if (!_scenarioContext.ContainsKey("Response.StatusCode")) return;

            var expectedresponseCode = new List<int>();
            if (responseCode.Contains(','))
                expectedresponseCode.AddRange(responseCode.Split(',').Select(x => Convert.ToInt32(x)));
            else expectedresponseCode.Add(Convert.ToInt32(responseCode));

            var actualResponseCode = (int)_scenarioContext["Response.StatusCode"];
            expectedresponseCode.Should().Contain(actualResponseCode, _scenarioContext["Response.Content"].ToString());
        }

        [Then(@"Verify error message is (.*)")]
        public void VerifyErrorMessage(string expectedmessage)
        {
            if (string.IsNullOrEmpty(expectedmessage) || expectedmessage.ToLower() == "null") return;

            var errors = JsonConvert.DeserializeObject<dynamic>(_scenarioContext["Response.Content"].ToString());

            string actualmessage;
            if (errors.GetType().FullName == "Newtonsoft.Json.Linq.JArray")
                actualmessage = errors[0].message;
            else
            {
                actualmessage = errors.message;
                if (string.IsNullOrEmpty(actualmessage))
                    actualmessage = errors.messages[0].text;
            }

            actualmessage.ToString().Should().Contain(expectedmessage, _scenarioContext["Response.Content"].ToString());
        }

        [Then(@"Verify boolean response is (.*)")]
        public void VerifyBoolResponse(bool? success)
        {
            if (success == null) return;

            var response = JsonConvert.DeserializeObject<bool>(_scenarioContext["Response.Content"].ToString());
            response.Should().Be(success.Value);
        }

        [Then(@"Verify response content object list is not null")]
        public void VerifyResponseContentObject()
        {
            var response = JsonConvert.DeserializeObject<dynamic>(_scenarioContext["Response.Content"].ToString());
            List<dynamic> list = JsonConvert.DeserializeObject<List<dynamic>>(response.list.ToString());
            list.Should().NotBeNull();
        }

        private void VerifyResponseContent(string actualResponse, string expectedResponse)
        {
            var actualdata = JsonConvert.DeserializeObject<IDictionary<string, object>>(actualResponse);
            foreach (var keyvalue in expectedResponse.Split(','))
            {
                var key = keyvalue.Split('=')[0];
                var value = keyvalue.Split('=')[1];
                if (value.StartsWith('{') && value.EndsWith('}'))
                {
                    value = _scenarioContext[value.Trim('{', '}')]?.ToString();
                }

                object actualValue;
                if (key.Contains('.'))
                {
                    var subkeys = key.Split('.');
                    for (var i = 0; i < subkeys.Length - 1; i++)
                    {
                        actualdata = JsonConvert.DeserializeObject<IDictionary<string, object>>(actualdata[subkeys[i]].ToString());
                    }
                    actualValue = actualdata[subkeys[^1]];
                }
                else
                {
                    actualValue = actualdata[key];
                }

                switch (value?.ToLower())
                {
                    case "notnull":
                        actualValue.Should().NotBeNull(actualResponse);
                        break;
                    case "null":
                    case null:
                        actualValue.Should().BeNull(actualResponse);
                        break;
                    default:
                        value.ToLower().Should().Be(actualValue.ToString().ToLower(), actualResponse);
                        break;
                }
            }
        }

        [Then(@"Verify response content object properties from list (.*)")]
        public void VerifyResponseContentFirstObject(string expectedresponse)
        {
            var response = JsonConvert.DeserializeObject<dynamic>(_scenarioContext["Response.Content"].ToString());
            List<dynamic> list = JsonConvert.DeserializeObject<List<dynamic>>(response.list.ToString());
            VerifyResponseContent(list.First().ToString(), expectedresponse);
        }


        [Then(@"Verify response content properties (.*)")]
        public void VerifyResponseContentProperty(string expectedresponse)
        {
            VerifyResponseContent(_scenarioContext["Response.Content"].ToString(), expectedresponse);
        }

        [Then("Verify actual response of object type (.*) is equal to expected data in context (.*)")]
        public void VerifyResponseObject(string obj, string val)
        {
            var assemblyType = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetTypes().Any(y => y.Name.Equals(obj, StringComparison.OrdinalIgnoreCase))).GetTypes();
            var objectType = assemblyType.First(y => y.Name.Equals(obj, StringComparison.OrdinalIgnoreCase));

            var expected = Convert.ChangeType(_scenarioContext[val], objectType);
            var actual = JsonConvert.DeserializeObject(_scenarioContext["Response.Content"].ToString(), objectType);
            actual.Should().BeEquivalentTo(expected, _scenarioContext["Response.Content"].ToString());
        }
   }
}
