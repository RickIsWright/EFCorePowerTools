﻿using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RevEng.Core.Abstractions;
using RevEng.Core.Abstractions.Metadata;
using RevEng.Core.Abstractions.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace RevEng.Core.Procedures
{
    public class SqlServerFunctionModelFactory : SqlServerRoutineModelFactory, IFunctionModelFactory
    {
        public SqlServerFunctionModelFactory()
        {
            RoutineType = "FUNCTION";
        }

        public RoutineModel Create(string connectionString, ModuleModelFactoryOptions options)
        {
            return GetRoutines(connectionString, options);
        }

        protected override List<List<ModuleResultElement>> GetResultElementLists(SqlConnection connection, Routine module, bool multipleResults, bool useLegacyResultSetDiscovery)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            using var dtResult = new DataTable();
            var list = new List<ModuleResultElement>();

            var sql = $@"
SELECT 
    c.name,
    COALESCE(type_name(c.system_type_id), type_name(c.user_type_id)) AS type_name,
    c.column_id AS column_ordinal,
    c.is_nullable
FROM sys.columns c
WHERE object_id = OBJECT_ID('{module.Schema}.{module.Name}');";

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
            using var adapter = new SqlDataAdapter
            {
                SelectCommand = new SqlCommand(sql, connection),
            };
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

            adapter.Fill(dtResult);

            int rCounter = 0;

            foreach (DataRow res in dtResult.Rows)
            {
                var parameter = new ModuleResultElement()
                {
                    Name = string.IsNullOrEmpty(res["name"].ToString()) ? $"Col{rCounter}" : res["name"].ToString(),
                    StoreType = res["type_name"].ToString(),
                    Ordinal = int.Parse(res["column_ordinal"].ToString(), CultureInfo.InvariantCulture),
                    Nullable = (bool)res["is_nullable"],
                };

                list.Add(parameter);

                rCounter++;
            }

            var result = new List<List<ModuleResultElement>>
            {
                list,
            };

            return result;
        }
    }
}
