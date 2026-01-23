using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using GeeksCoreLibrary.Core.DependencyInjection.Interfaces;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Modules.Exports.Interfaces;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Text;

namespace GeeksCoreLibrary.Modules.Exports.Services;

public class CsvService : ICsvService, IScopedService
{
    /// <inheritdoc />
    public string JsonArrayToCsv(JArray data, string delimiter = ";")
    {
        if (data == null || !data.Any())
        {
            return String.Empty;
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter
        };
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter, config);
        
        data = JsonHelpers.FlattenJsonArray(data);

        foreach (var pair in data.Cast<JObject>().First())
        {
            csvWriter.WriteField(pair.Key);
        }
        csvWriter.NextRecord();
        
        foreach (JObject row in data)
        {
            foreach (var pair in row)
            {
                csvWriter.WriteField(pair.Value.ToString());
            }

            csvWriter.NextRecord();
        }

        return stringWriter.ToString();
    }
    
    public static byte[] DataTableToCsvBytes(DataTable table, string delimiter = ";")
    {
        using var memoryStream = new MemoryStream();

        // Excel-friendly UTF-8 (with BOM)
        using var writer = new StreamWriter(
            memoryStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            leaveOpen: true
        );

        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = delimiter
        });

        // Header
        foreach (DataColumn column in table.Columns)
        {
            csv.WriteField(column.ColumnName);
        }
        csv.NextRecord();

        // Rows
        foreach (DataRow row in table.Rows)
        {
            foreach (var field in row.ItemArray)
            {
                csv.WriteField(field);
            }
            csv.NextRecord();
        }

        writer.Flush();
        return memoryStream.ToArray();
    }
    
}