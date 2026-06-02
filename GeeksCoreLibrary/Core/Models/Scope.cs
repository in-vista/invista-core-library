using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GeeksCoreLibrary.Core.Models;

public class Scope
{
    public Dictionary<string, object> Scalars { get; } = new Dictionary<string, object?>();
    public Dictionary<string, List<JToken>> Arrays { get; } = new Dictionary<string, List<JToken>>();

    public void Merge(Scope other)
    {
        foreach (KeyValuePair<string, object?> scalar in other.Scalars)
        {
            Scalars[scalar.Key] = scalar.Value;
        }

        foreach (KeyValuePair<string, List<JToken>> array in other.Arrays)
        {
            Arrays[array.Key] = array.Value;
        }
    }
}