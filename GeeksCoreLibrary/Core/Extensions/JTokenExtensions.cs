using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using GeeksCoreLibrary.Core.Models;
using Newtonsoft.Json.Linq;

namespace GeeksCoreLibrary.Core.Extensions;

public static class JTokenExtensions
{
    public static DataTable ToDeepFlattenedDataTable(this JToken token)
    {
        List<Dictionary<string, object?>> rows = new List<Dictionary<string, object?>>();
        
        Dictionary<int, int> depthCounters = new Dictionary<int, int>();
        
        List<Scope> rootScopes = ExtractScopes(token);

        foreach (Scope scope in rootScopes)
        {
            List<Dictionary<string, object?>> materializedRows = Materialize(scope, depthCounters);

            foreach (Dictionary<string, object?> row in materializedRows)
            {
                rows.Add(row);
            }
        }

        DataTable table = new DataTable();

        IEnumerable<string> columns = rows.SelectMany(r => r.Keys).Distinct();

        foreach (string column in columns)
        {
            table.Columns.Add(column);
        }

        foreach (Dictionary<string, object?> row in rows)
        {
            DataRow dataRow = table.NewRow();

            foreach (KeyValuePair<string, object?> keyValuePair in row)
            {
                dataRow[keyValuePair.Key] = keyValuePair.Value ?? DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }
    
    private static List<Scope> ExtractScopes(JToken token)
    {
        List<Scope> scopes = new List<Scope>();

        if (token.Type == JTokenType.Array)
        {
            foreach (JToken child in token.Children())
            {
                Scope scope = ExtractScope(child, string.Empty);
                scopes.Add(scope);
            }
        }
        else
        {
            Scope scope = ExtractScope(token, string.Empty);
            scopes.Add(scope);
        }

        return scopes;
    }

    private static Scope ExtractScope(JToken token, string prefix)
    {
        Scope scope = new Scope();

        if (token.Type == JTokenType.Object)
        {
            foreach (JProperty property in token.Children<JProperty>())
            {
                string key = Combine(prefix, property.Name);

                if (property.Value.Type == JTokenType.Object)
                {
                    Scope childScope = ExtractScope(property.Value, key);
                    scope.Merge(childScope);
                }
                else if (property.Value.Type == JTokenType.Array)
                {
                    JArray array = (JArray)property.Value;
                    scope.Arrays[key] = array.Children().ToList();
                }
                else
                {
                    scope.Scalars[key] = property.Value.ToObject<object?>();
                }
            }
        }

        return scope;
    }
    
    private static List<Dictionary<string, object?>> Materialize(Scope scope, Dictionary<int, int> depthCounters)
    {
        int depth = 1;
        
        int maxLength = scope.Arrays.Count == 0
            ? 1
            : scope.Arrays.Values.Max(a => a.Count);

        List<Dictionary<string, object?>> rows = new List<Dictionary<string, object?>>();

        for (int i = 0; i < maxLength; i++)
        {
            Dictionary<string, object?> row = new Dictionary<string, object?>();

            foreach (KeyValuePair<string, object?> scalar in scope.Scalars)
            {
                row[scalar.Key] = scalar.Value;
            }
            
            // Ensure counter exists for this depth.
            depthCounters.TryAdd(depth, 0);
                
            // Increase depth in the depth counters.
            depthCounters[depth]++;
                
            // Add idN column.
            int groupId = depthCounters[depth];
            row[$"id{depth}"] = groupId;

            foreach (KeyValuePair<string, List<JToken>> array in scope.Arrays)
            {
                JToken? value = i < array.Value.Count
                    ? array.Value[i]
                    : null;

                row[array.Key] = ConvertToken(value);
            }

            rows.Add(row);
        }

        return rows;
    }
    
    private static object? ConvertToken(JToken? token)
    {
        if (token == null)
        {
            return null;
        }

        if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
        {
            return token.ToString();
        }

        JValue value = (JValue)token;
        return value.Value;
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return name;
        }

        return prefix + "." + name;
    }
}