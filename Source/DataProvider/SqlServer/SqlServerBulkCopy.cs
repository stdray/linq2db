﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace LinqToDB.DataProvider.SqlServer
{
	using Data;
	using SqlProvider;

	class SqlServerBulkCopy : BasicBulkCopy
	{
		public SqlServerBulkCopy(SqlServerDataProvider dataProvider)
		{
			_dataProvider = dataProvider;
		}

		readonly SqlServerDataProvider _dataProvider;

		protected override BulkCopyRowsCopied ProviderSpecificCopy<T>(
			[JetBrains.Annotations.NotNull] DataConnection dataConnection,
			BulkCopyOptions options,
			IEnumerable<T>  source)
		{
			if (dataConnection == null) throw new ArgumentNullException("dataConnection");

			var connection = dataConnection.Connection as SqlConnection;

			if (connection != null)
			{
				var ed      = dataConnection.MappingSchema.GetEntityDescriptor(typeof(T));
				var columns = ed.Columns.Where(c => !c.SkipOnInsert || options.KeepIdentity == true && c.IsIdentity).ToList();
				var sb      = _dataProvider.CreateSqlBuilder();
				var rd      = new BulkCopyReader(_dataProvider, columns, source);
				var sqlopt  = SqlBulkCopyOptions.Default;
				var rc      = new BulkCopyRowsCopied();

				if (options.CheckConstraints == true) sqlopt |= SqlBulkCopyOptions.CheckConstraints;
				if (options.KeepIdentity     == true) sqlopt |= SqlBulkCopyOptions.KeepIdentity;

				using (var bc = new SqlBulkCopy(connection, sqlopt, (SqlTransaction)dataConnection.Transaction))
				{
					if (options.NotifyAfter != 0 && options.RowsCopiedCallback != null)
					{
						bc.NotifyAfter = options.NotifyAfter;

						bc.SqlRowsCopied += (sender,e) =>
						{
							rc.RowsCopied = e.RowsCopied;
							options.RowsCopiedCallback(rc);
							if (rc.Abort)
								e.Abort = true;
						};
					}

					if (options.MaxBatchSize.   HasValue) bc.BatchSize       = options.MaxBatchSize.   Value;
					if (options.BulkCopyTimeout.HasValue) bc.BulkCopyTimeout = options.BulkCopyTimeout.Value;

					var sqlBuilder = _dataProvider.CreateSqlBuilder();
					var descriptor = dataConnection.MappingSchema.GetEntityDescriptor(typeof(T));
					var tableName  = GetTableName(sqlBuilder, descriptor);

					bc.DestinationTableName = tableName;

					for (var i = 0; i < columns.Count; i++)
						bc.ColumnMappings.Add(new SqlBulkCopyColumnMapping(
							i,
							sb.Convert(columns[i].ColumnName, ConvertType.NameToQueryField).ToString()));

					bc.WriteToServer(rd);

					rc.RowsCopied = rd.Count;

					return rc;
				}
			}

			return MultipleRowsCopy(dataConnection, options, source);
		}
	}
}
