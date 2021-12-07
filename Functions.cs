using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Amazon;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using TechTalk.SpecFlow;

namespace Helper
{
    public class Functions
    {
        /// <summary>
        /// Get Stream contents of an embedded resource file
        /// </summary>
        public static Stream GetEmbeddedResourceStream(string filename)
        {
            var thisAssembly = Assembly.GetExecutingAssembly();
            var stream = thisAssembly.GetManifestResourceStream(filename);
            return stream;
        }

        public static List<KeyValuePair<string, object>> ConvertQueryParametersToKeyValuePairs(string namevaluepairs, ScenarioContext scenarioContext)
        {
            if (string.IsNullOrEmpty(namevaluepairs)) return null;
            var keyValuePairs = new List<KeyValuePair<string, object>>();
            var namevaluesplit = namevaluepairs.Split('&');
            foreach (var namevalue in namevaluesplit)
            {
                if (string.IsNullOrEmpty(namevalue)) continue;
                var name = namevalue.Split('=')[0].Trim();
                var val = namevalue.Split('=')[1].Trim();

                object oVal = val;
                if (val.StartsWith("[") && val.EndsWith("]"))
                {
                    var listval = val.TrimStart('[').TrimEnd(']').Split(',');
                    var listoVal = new List<object>();
                    foreach(var lval in listval)
                    {
                        oVal = lval;
                        if (lval.StartsWith("{") && lval.EndsWith("}"))
                            oVal = scenarioContext[lval.Trim('{', '}').ToString()];
                        else if (lval.ToLower().Contains("datetime"))
                            oVal = ConvertToDateTime(lval.Split('>')[1]);
                        else if (lval.ToLower().Contains("_randomnum"))
                            oVal = lval.Replace("_RandomNum", new Random().Next(100000000).ToString());

                        if (!Functions.IsNumeric(oVal.GetType()) && !Functions.IsDateTime(oVal.ToString()) && !oVal.ToString().Contains('"'))
                            oVal = (object)Functions.GetSpecialCharactersHexEscaped(oVal.ToString());
                        oVal = oVal?.ToString().Replace('"', ' ').Trim();
                        listoVal.Add(oVal);
                    }
                    keyValuePairs.Add(new KeyValuePair<string, object>(name, listoVal));
                }
                else
                {
                    if (val.StartsWith("{") && val.EndsWith("}"))
                        oVal = scenarioContext[val.Trim('{', '}').ToString()];
                    else if (val.ToLower().Contains("datetime"))
                        oVal = ConvertToDateTime(val.Split('>')[1]);
                    else if (val.ToLower().Contains("_randomnum"))
                        oVal = val.Replace("_RandomNum", new Random().Next(100000000).ToString());

                    if (!Functions.IsNumeric(oVal.GetType()) && !Functions.IsDateTime(oVal.ToString()) && !oVal.ToString().Contains('"'))
                        oVal = (object)Functions.GetSpecialCharactersHexEscaped(oVal.ToString());
                    oVal = oVal?.ToString().Replace('"', ' ').Trim();

                    if (val.ToLower() == "null") oVal = null;
                    keyValuePairs.Add(new KeyValuePair<string, object>(name, oVal));
                }                                               
            }
            return keyValuePairs;
        }

        public static dynamic ConvertJsonBodyToKeyValuePairs(string jsonbody, ScenarioContext scenarioContext)
        {
            if (string.IsNullOrEmpty(jsonbody)) return null;

            var keyValuePairs = new List<KeyValuePair<string, object>>();
            var namevaluepairs = jsonbody.TrimStart('{');
            if (namevaluepairs.EndsWith("}"))
                namevaluepairs = namevaluepairs.Remove(namevaluepairs.Length - 1);
            if (jsonbody.StartsWith('[') && jsonbody.EndsWith(']'))
            {
                jsonbody = jsonbody.TrimStart('[').TrimEnd(']');
                var listKeyValuePairs = new List<List<KeyValuePair<string, object>>>();
                var arrayobj = jsonbody.Split(',');
                foreach (var obj in arrayobj)
                {
                    namevaluepairs = obj.TrimStart('{').TrimEnd('}');
                    if (string.IsNullOrEmpty(namevaluepairs)) continue;
                    keyValuePairs = new List<KeyValuePair<string, object>>();
                    keyValuePairs.AddRange(ConvertQueryParametersToKeyValuePairs(namevaluepairs, scenarioContext));
                    listKeyValuePairs.Add(keyValuePairs);
                }
                return listKeyValuePairs;
            }
            keyValuePairs.AddRange(ConvertQueryParametersToKeyValuePairs(namevaluepairs, scenarioContext));
            return keyValuePairs;
        }

        public static dynamic SetPropertyValue(string obj, List<List<KeyValuePair<string, object>>> objlist)
        {
            var list = new List<object>();
            foreach (var namevaluelist in objlist)
            {
                var actualObject = SetPropertyValue(obj, namevaluelist);
                list.Add(actualObject);
            }
            return list;    
        }

        public static dynamic SetPropertyValue(string obj, List<KeyValuePair<string, object>> namevaluelist)
        {
            var assemblyType = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetTypes().Any(y => y.Name.Equals(obj, StringComparison.OrdinalIgnoreCase))).GetTypes();
            var objectType = assemblyType.First(y => y.Name.Equals(obj, StringComparison.OrdinalIgnoreCase));

            var actualObject = Activator.CreateInstance(Type.GetType(objectType.AssemblyQualifiedName));
            foreach (var namevalue in namevaluelist)
            {
                var propinfo = actualObject.GetType().GetProperty(namevalue.Key);
                Type proptype = propinfo.PropertyType;
                if (Nullable.GetUnderlyingType(proptype) != null)
                    proptype = Nullable.GetUnderlyingType(proptype);
                if (namevalue.Value == null)
                    continue;
                else if (proptype.IsEnum)
                    propinfo.SetValue(actualObject, Enum.Parse(proptype, namevalue.Value.ToString()));
                else if (proptype.IsGenericType && proptype.GetGenericTypeDefinition() == typeof(List<>))
                    propinfo.SetValue(actualObject,  ((List<object>)namevalue.Value).ConvertAll(o=> o.ToString()));
                else if (namevalue.Value.ToString() != "null")
                    propinfo.SetValue(actualObject, Convert.ChangeType(namevalue.Value, proptype));
            }
            return actualObject;
        }

        public static dynamic UpdatePropertyValue(object obj, KeyValuePair<string, object> namevalue)
        {
            var propinfo = obj.GetType().GetProperty(namevalue.Key);
            var proptype = propinfo.PropertyType;
            if (Nullable.GetUnderlyingType(proptype) != null)
                proptype = Nullable.GetUnderlyingType(proptype);

            if (namevalue.Value is IList)
            {
                Type containedType = proptype.GenericTypeArguments.First(); ;
                var castMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Cast)).MakeGenericMethod(containedType);
                var toListMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.ToList)).MakeGenericMethod(containedType);
                var castedItems = castMethod.Invoke(null, new[] { namevalue.Value });
                propinfo.SetValue(obj, toListMethod.Invoke(null, new[] { castedItems }));
            }
            else
            {
                propinfo.SetValue(obj, Convert.ChangeType(namevalue.Value, proptype));
            }
            return obj;
        }

        public static DateTime Truncate(DateTime dateTime, TimeSpan timeSpan)
        {
            if (timeSpan == TimeSpan.Zero) return dateTime;
            if (dateTime == DateTime.MinValue || dateTime == DateTime.MaxValue) return dateTime;
            return dateTime.AddTicks(-(dateTime.Ticks % timeSpan.Ticks));
        }

        public static DateTime ConvertToDateTime(string dt)
        {
            var date = DateTime.UtcNow;
            foreach (var x in dt.Split('`'))
            {
                var numdigit = int.Parse(new string(x.Where(Char.IsDigit).ToArray()));
                var num = x.Contains("-") ? -1 * numdigit : numdigit;
                if (x.Contains("Y"))
                    date = date.AddYears(-num).AddDays(-1);
                else if (x.Contains("M"))
                    date = date.AddMonths(-num).AddDays(-1);
                else if (x.Contains("D"))
                    date = date.AddDays(-num);
            }

            return Truncate(date, TimeSpan.FromMilliseconds(1000));
        }

        public static string GetSpecialCharactersHexEscaped(string value)
        {
            var hexEscape = value;
            foreach (char ch in value.ToCharArray())
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    var oldvalue = ch.ToString();
                    var newvalue = Uri.HexEscape(ch).ToString();
                    hexEscape = hexEscape.Replace(oldvalue, newvalue);
                }
            }
            return hexEscape;
        }

        public static bool IsNumeric(Type myType)
        {
            return NumericTypes.Contains(Nullable.GetUnderlyingType(myType) ?? myType);
        }

        public static bool IsDateTime(string val)
        {
            string[] formats = {"M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt",
                   "MM/dd/yyyy hh:mm:ss", "M/d/yyyy h:mm:ss",
                   "M/d/yyyy hh:mm tt", "M/d/yyyy hh tt",
                   "M/d/yyyy h:mm", "M/d/yyyy h:mm",
                   "MM/dd/yyyy hh:mm", "M/dd/yyyy hh:mm"};
            return DateTime.TryParseExact(val, formats, new CultureInfo("en-US"), DateTimeStyles.None, out DateTime datevalue);
        }

        private static readonly HashSet<Type> NumericTypes = new HashSet<Type>
        {
            typeof(int),  typeof(double),  typeof(decimal),
            typeof(long), typeof(short),   typeof(sbyte),
            typeof(byte), typeof(ulong),   typeof(ushort),
            typeof(uint), typeof(float)
        };
    }
}
