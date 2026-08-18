using System;
using System.Data;
using System.Data.Common;
using MySqlConnector;

namespace GeeksCoreLibrary.Core.Extensions
{
    public static class DbDataReaderExtensions
    {
        /// <summary>
        /// Gets a string value from a <see cref="DbDataReader"/> and returns an empty string if the value is <see langword="null"/>.
        /// </summary>
        /// <param name="reader">The <see cref="DbDataReader"/> to get the value of.</param>
        /// <param name="columnIndex">The index of the column to get the value of.</param>
        /// <returns>A <see langword="string"/> with the value.</returns>
        public static string GetStringHandleNull(this DbDataReader reader, int columnIndex)
        {
            return reader.IsDBNull(columnIndex) ? String.Empty : reader.GetString(columnIndex);
        }

        /// <summary>
        /// Gets a string value from a <see cref="DbDataReader"/> and returns an empty string if the value is <see langword="null"/>.
        /// </summary>
        /// <param name="reader">The <see cref="DbDataReader"/> to get the value of.</param>
        /// <param name="columnName">The name of the column to get the value of.</param>
        /// <returns>A <see langword="string"/> with the value.</returns>
        public static string GetStringHandleNull(this DbDataReader reader, string columnName)
        {
            if (!reader.HasColumn(columnName))
            {
                return String.Empty;
            }

            return reader.IsDBNull(reader.GetOrdinal(columnName)) ? String.Empty : reader.GetString(columnName);
        }

        /// <summary>
        /// Checks if a columns exists in a data reader.
        /// </summary>
        /// <param name="reader">The <see cref="IDataRecord"/> to check.</param>
        /// <param name="columnName">The name of the column to check.</param>
        /// <returns></returns>
        public static bool HasColumn(this IDataRecord reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Safely retrieves the value even if it would be returned as <see cref="DBNull"/>.
        /// </summary>
        /// <param name="reader">The reader to read the value from.</param>
        /// <param name="columnOrdinal">The index of the column to retrieve the value from.</param>
        /// <typeparam name="T">The type to retrieve the value as.</typeparam>
        /// <returns>The value of the given column ordinal as a safe value.</returns>
        public static T GetSafeValue<T>(this DbDataReader reader, int columnOrdinal)
        {
            object value = reader.GetValue(columnOrdinal);
            return value == DBNull.Value ? default : (T)value;
        }
    }
}
