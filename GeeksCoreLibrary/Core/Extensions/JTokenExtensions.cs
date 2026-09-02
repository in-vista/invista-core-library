using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GeeksCoreLibrary.Core.Extensions;

public static class JTokenExtensions
{
    public static DataTable ToDeepFlattenedDataTable(this JToken token)
    {
        List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();

        if (token.Type == JTokenType.Array)
        {
            foreach (JToken child in token.Children())
            {
                rows.AddRange(FlattenObject(child, 1));
            }
        }
        else
        {
            rows.AddRange(FlattenObject(token, 1));
        }

        DataTable table = new DataTable();

        IEnumerable<string> columns = rows
            .SelectMany(r => r.Keys)
            .Distinct();

        foreach (string column in columns)
            table.Columns.Add(column);

        foreach (Dictionary<string, object> row in rows)
        {
            DataRow dataRow = table.NewRow();

            foreach (KeyValuePair<string, object> item in row)
            {
                dataRow[item.Key] = item.Value ?? DBNull.Value;
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }

    private static List<Dictionary<string, object>> FlattenObject(
        JToken token,
        int depth)
    {
        Dictionary<string, object> scalars =
            new Dictionary<string, object>();

        List<JArray> arrays =
            new List<JArray>();

        foreach (JProperty property in token.Children<JProperty>())
        {
            if (property.Value.Type == JTokenType.Array)
            {
                arrays.Add((JArray)property.Value);
            }
            else if (property.Value.Type == JTokenType.Object)
            {
                List<Dictionary<string, object>> nestedRows =
                    FlattenObject(property.Value, depth + 1);

                foreach (Dictionary<string, object> nestedRow in nestedRows)
                {
                    foreach (KeyValuePair<string, object> item in nestedRow)
                    {
                        scalars[item.Key] = item.Value;
                    }
                }
            }
            else
            {
                string columnName = property.Name + depth;

                scalars[columnName] =
                    property.Value.ToObject<object>();
            }
        }
        
        if (arrays.Count == 0)
        {
            return new List<Dictionary<string, object>>
            {
                scalars
            };
        }

        List<Dictionary<string, object>> rows =
            new List<Dictionary<string, object>>();

        int maxLength = arrays.Max(a => a.Count);

        for (int i = 0; i < maxLength; i++)
        {
            List<Dictionary<string, object>> childRows =
                new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>()
                };

            foreach (JArray array in arrays)
            {
                if (i >= array.Count)
                    continue;

                List<Dictionary<string, object>> flattenedChildren =
                    FlattenObject(array[i], depth + 1);

                List<Dictionary<string, object>> newChildRows =
                    new List<Dictionary<string, object>>();

                foreach (Dictionary<string, object> existingRow in childRows)
                {
                    foreach (Dictionary<string, object> flattenedChild in flattenedChildren)
                    {
                        Dictionary<string, object> combined =
                            new Dictionary<string, object>(existingRow);

                        foreach (KeyValuePair<string, object> item in flattenedChild)
                        {
                            combined[item.Key] = item.Value;
                        }

                        newChildRows.Add(combined);
                    }
                }

                childRows = newChildRows;
            }

            foreach (Dictionary<string, object> childRow in childRows)
            {
                Dictionary<string, object> row =
                    new Dictionary<string, object>(scalars);

                foreach (KeyValuePair<string, object> item in childRow)
                {
                    row[item.Key] = item.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }
}