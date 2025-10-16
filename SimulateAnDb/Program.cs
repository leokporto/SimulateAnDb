// Program.cs
// .NET 9
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;

namespace SimulateAnDb
{
    // CLI settings
    public sealed class SimOptions : CommandSettings
    {
        [CommandOption("-t|--table <TABLE>")]
        public string Table { get; init; }

        [CommandOption("-i|--interval <MINUTES>")]
        public int Interval { get; init; } = 1;

        [CommandOption("-s|--startdate <DD-MM-YYYY>")]
        public string StartDate { get; init; }

        [CommandOption("-e|--enddate <DD-MM-YYYY>")]
        public string EndDate { get; init; }
    }

    public sealed class SimulateCommand : AsyncCommand<SimOptions>
    {
        private readonly IConfiguration _config;

        public SimulateCommand()
        {
            _config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
        }

        public override async Task<int> ExecuteAsync(CommandContext context, SimOptions settings)
        {
            try
            {
                ValidateSettings(settings);

                var connStrFull = _config.GetConnectionString("Default");
                if (string.IsNullOrWhiteSpace(connStrFull))
                    throw new InvalidOperationException("Connection string não encontrada em appsettings.json (Database:ConnectionString).");

                var providerName = ExtractProvider(connStrFull);
                var connStr = RemoveProviderFromConnectionString(connStrFull);

                AnsiConsole.MarkupLine($"[green]Provider:[/] {providerName}");
                AnsiConsole.MarkupLine($"[green]Connection string (trimmed):[/] {connStr}");

                using var connection = CreateDbConnection(providerName, connStr);
                await connection.OpenAsync();

                // discover columns
                var allColumns = await DiscoverTableColumnsAsync(connection, providerName, settings.Table);
                if (!allColumns.Any())
                {
                    AnsiConsole.MarkupLine($"[red]Tabela '{settings.Table}' não encontrada ou sem colunas.[/]");
                    return -1;
                }

                // identify measure columns (non-underscore numeric-like) and their quality columns _<name>_Q
                var measureColumns = allColumns
                    .Where(c => !c.Equals("ID", StringComparison.OrdinalIgnoreCase)
                                && !c.Equals("UTCTimestamp_Ticks", StringComparison.OrdinalIgnoreCase)
                                && !c.Equals("LogType", StringComparison.OrdinalIgnoreCase)
                                && !c.Equals("NotSync", StringComparison.OrdinalIgnoreCase)
                                && !c.StartsWith("_"))
                    .ToList();

                if (!measureColumns.Any())
                {
                    AnsiConsole.MarkupLine("[red]Nenhuma coluna de medida detectada.[/]");
                    return -1;
                }

                var qualityColumns = measureColumns.Select(m => $"_{m}_Q").ToList();

                AnsiConsole.MarkupLine($"[blue]Medidas:[/] {string.Join(", ", measureColumns)}");
                AnsiConsole.MarkupLine($"[blue]Qualidades:[/] {string.Join(", ", qualityColumns)}");

                // clear table
                await CleanTableAsync(connection, providerName, settings.Table);

                // simulation settings                
                int commitBatchSize = string.IsNullOrWhiteSpace(_config["Simulation:CommitBatchSize"]) ? 1000 : int.Parse(_config["Simulation:CommitBatchSize"]);
                float minVal = _config["Simulation:ValueMin"] == null ? 40f : float.Parse(_config["Simulation:ValueMin"]);
                float maxVal = _config["Simulation:ValueMax"] == null ? 95f : float.Parse(_config["Simulation:ValueMax"]);
                float noiseAmp = _config["Simulation:NoiseAmplitude"] == null ? 0.5f : float.Parse(_config["Simulation:NoiseAmplitude"]);

                // prepare per-measure initial states
                var rnd = new Random();
                var measureState = new Dictionary<string, MeasureState>();
                foreach (var m in measureColumns)
                {
                    measureState[m] = new MeasureState
                    {
                        Base = (float)(minVal + rnd.NextDouble() * (maxVal - minVal)), // initial base
                        Phase = rnd.NextDouble() * Math.PI * 2.0,
                        PeriodMinutes = rnd.Next(60, 360), // 1h..6h
                        Amplitude = (float)((maxVal - minVal) * (0.25 + rnd.NextDouble() * 0.4)) // fraction of range
                    };
                }

                // time window
                var start = ParseDate(settings.StartDate).Date; // local date 00:00:00
                var endInclusive = ParseDate(settings.EndDate).Date.AddDays(1).AddSeconds(-1); // up to dayFinal 23:59:59
                var interval = TimeSpan.FromMinutes(settings.Interval);

                AnsiConsole.MarkupLine($"[green]Iniciando simulação de {start:yyyy-MM-dd HH:mm:ss} até {endInclusive:yyyy-MM-dd HH:mm:ss} com intervalo {settings.Interval} minutos.[/]");

                // iterate and insert in batches; for safety, we'll use a transaction per batch
                var totalInserted = 0;
                var batchList = new List<Dictionary<string, object>>(commitBatchSize);
                var currentTime = start;
                var stepIndex = 0;

                while (currentTime <= endInclusive)
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["UTCTimestamp_Ticks"] = DateTime.SpecifyKind(currentTime, DateTimeKind.Utc).Ticks,
                        ["LogType"] = 1,
                        ["NotSync"] = 0
                    };

                    // compute values for each measure (non-strict sine) using its own state
                    foreach (var m in measureColumns)
                    {
                        var st = measureState[m];

                        // advance phase according to interval and period
                        var deltaPhase = (2.0 * Math.PI) * (settings.Interval / (double)st.PeriodMinutes);
                        st.Phase += deltaPhase;

                        // sine base
                        var sine = Math.Sin(st.Phase);

                        // mapped value around base using amplitude
                        var value = st.Base + (float)(sine * st.Amplitude);

                        // add non-strict noise
                        var noise = ((float)rnd.NextDouble() - 0.5f) * noiseAmp; // +- noiseAmp/2
                        value += noise;

                        // clamp to min/max
                        value = Math.Clamp(value, minVal, maxVal);

                        // store as float
                        row[m] = value;

                        // quality
                        var qName = $"_{m}_Q";
                        row[qName] = rnd.NextDouble() < 0.9 ? (short)192 : (short)0;
                    }

                    batchList.Add(row);
                    totalInserted++;
                    stepIndex++;

                    // if batch reached commit size, flush
                    if (batchList.Count >= commitBatchSize)
                    {
                        await InsertBatchAndCommitAsync(connection, providerName, settings.Table, measureColumns, batchList);
                        AnsiConsole.MarkupLine($"[green]{totalInserted} registros inseridos (ultimo timestamp {currentTime:yyyy-MM-dd HH:mm:ss}).[/]");
                        batchList.Clear();
                    }

                    // advance time
                    currentTime = currentTime.AddMinutes(settings.Interval);
                }

                // flush remaining
                if (batchList.Count > 0)
                {
                    await InsertBatchAndCommitAsync(connection, providerName, settings.Table, measureColumns, batchList);
                    AnsiConsole.MarkupLine($"[green]{totalInserted} registros inseridos (final).[/]");
                    batchList.Clear();
                }

                AnsiConsole.MarkupLine($"[bold green]Concluído. Total inserido: {totalInserted}[/]");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Erro: {ex.Message}[/]");
                return -1;
            }
        }

        private static void ValidateSettings(SimOptions s)
        {
            if (string.IsNullOrWhiteSpace(s.Table))
                throw new ArgumentException("Parâmetro --table (-t) obrigatório.");
            if (s.Interval <= 0)
                throw new ArgumentException("Intervalo (--interval / -i) deve ser >0.");
            if (string.IsNullOrWhiteSpace(s.StartDate) || string.IsNullOrWhiteSpace(s.EndDate))
                throw new ArgumentException("--startdate e --enddate obrigatórios.");
            ParseDate(s.StartDate);
            ParseDate(s.EndDate);
        }

        private static DateTime ParseDate(string d)
        {
            if (!DateTime.TryParseExact(d, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                throw new ArgumentException($"Data '{d}' inválida. Use dd/MM/yyyy.");
            return dt;
        }

        private static string ExtractProvider(string fullConn)
        {
            var parts = fullConn.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("Provider", StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }
            throw new InvalidOperationException("Connection string precisa conter Provider=SQLite|SqlServer|PostgreSQL;");
        }

        private static string RemoveProviderFromConnectionString(string fullConn)
        {
            var parts = fullConn.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.TrimStart().StartsWith("Provider=", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return string.Join(";", parts);
        }

        private static DbConnection CreateDbConnection(string provider, string connStr)
        {
            return provider switch
            {
                "SQLite" => new SqliteConnection(connStr),
                "SqlServer" => new SqlConnection(connStr),
                "PostgreSQL" => new NpgsqlConnection(connStr),
                _ => throw new NotSupportedException($"Provider '{provider}' não suportado.")
            };
        }

        private static async Task<List<string>> DiscoverTableColumnsAsync(DbConnection conn, string provider, string table)
        {
            if (provider == "SQLite")
            {
                var pragma = await conn.QueryAsync($"PRAGMA table_info({table});");
                // rows have 'name'
                return pragma.Select(r => (string)r.name).ToList();
            }
            else if (provider == "SqlServer")
            {
                var sql = @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table ORDER BY ORDINAL_POSITION";
                var rows = await conn.QueryAsync<string>(sql, new { table });
                return rows.ToList();
            }
            else if (provider == "PostgreSQL")
            {
                // postgres lowercases unquoted identifiers; try both
                var sql = @"SELECT column_name FROM information_schema.columns WHERE table_name = @table ORDER BY ordinal_position";
                var rows = await conn.QueryAsync<string>(sql, new { table = table.ToLowerInvariant() });
                if (rows.Any()) return rows.ToList();
                // fallback: try original case
                rows = await conn.QueryAsync<string>(sql, new { table });
                return rows.ToList();
            }
            else
            {
                throw new NotSupportedException("Provider não suportado");
            }
        }

        private static string QuoteIdentifier(string identifier, string provider)
        {
            return provider switch
            {
                "SQLite" => $"\"{identifier.Replace("\"", "\"\"")}\"",
                "SqlServer" => $"[{identifier.Replace("]", "]]")}]",
                "PostgreSQL" => $"\"{identifier.Replace("\"", "\"\"")}\"",
                _ => identifier
            };
        }

        private static async Task CleanTableAsync(DbConnection conn, string provider, string table)
        {
            AnsiConsole.MarkupLine($"[yellow]Limpando tabela {table} (provider {provider})...[/]");
            if (provider == "SQLite")
            {
                await conn.ExecuteAsync($"DELETE FROM {QuoteIdentifier(table, provider)};");
                await conn.ExecuteAsync("DELETE FROM sqlite_sequence WHERE name = @name;", new { name = table });
            }
            else if (provider == "SqlServer")
            {
                await conn.ExecuteAsync($"DELETE FROM {QuoteIdentifier(table, provider)};");
                try
                {
                    await conn.ExecuteAsync($"DBCC CHECKIDENT('{table}', RESEED, 0);");
                }
                catch
                {
                    // ignore if fails
                }
            }
            else if (provider == "PostgreSQL")
            {
                await conn.ExecuteAsync($"TRUNCATE TABLE {QuoteIdentifier(table, provider)} RESTART IDENTITY CASCADE;");
            }
        }

        private static async Task InsertBatchAndCommitAsync(DbConnection conn, string provider, string table, List<string> measureColumns, List<Dictionary<string, object>> batch)
        {
            if (batch.Count == 0) return;

            // Build full column list from first item (should be consistent)
            var first = batch[0];
            var columns = first.Keys.ToList();

            // Ensure proper quoting
            var quotedColumns = columns.Select(c => QuoteIdentifier(c, provider)).ToArray();
            var columnListSql = string.Join(", ", quotedColumns);

            // Build parameter placeholders: for Dapper we will pass batch as IEnumerable<IDictionary<string,object>> and use parameter names as-is
            // But Dapper needs a concrete parameter object. We'll build a list of ExpandoObject-like dictionaries (we already have dictionaries).
            var paramNames = columns.Select(c => "@" + c).ToArray();
            var valuesPlaceholders = string.Join(", ", paramNames);

            var insertSql = $"INSERT INTO {QuoteIdentifier(table, provider)} ({columnListSql}) VALUES ({valuesPlaceholders});";

            // Begin transaction for this batch
            using var transaction = conn.BeginTransaction();
            try
            {
                // Execute: Dapper supports IEnumerable<object> where each object matches parameters
                await conn.ExecuteAsync(insertSql, batch.Cast<object>(), transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private class MeasureState
        {
            public float Base { get; set; }
            public double Phase { get; set; }
            public int PeriodMinutes { get; set; }
            public float Amplitude { get; set; }
        }
    }

    public static class ProgramBootstrap
    {
        public static async Task<int> Main(string[] args)
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                config.SetApplicationName("SimulateAnDb");
                config.AddCommand<SimulateCommand>("simulate").WithDescription("Simula e insere dados em tabela SCADA existente");
            });

            if (args == null || args.Length == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Uso exemplo:[/] SimulateAnDb simulate -t ANA -i 1 -s 01/10/2025 -e 03/10/2025");
                return 0;
            }

            return await app.RunAsync(args);
        }
    }
}
