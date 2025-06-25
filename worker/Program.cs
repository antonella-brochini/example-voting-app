using System;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;      // <--- agregado
using Newtonsoft.Json;
using Npgsql;
using StackExchange.Redis;
using System.IO;
using System.Text;
using System.Net.Http;

namespace Worker
{
    public class Program
    {
        // Cambié Main a async Task<int> para usar await
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var pgsql = OpenDbConnection("Server=db;Username=postgres;Password=postgres;");
                var redisConn = OpenRedisConnection("redis");
                var redis = redisConn.GetDatabase();

               string secretPath = "/etc/secrets/BACKUP_API_URL";
               string backupApiUrl = "";
               backupApiUrl = File.ReadAllText(secretPath).Trim();
               byte[] data = Convert.FromBase64String(backupApiUrl);
               string decodedUrl = Encoding.UTF8.GetString(data);
               Console.WriteLine($"✔ BACKUP_API_URL decodificada: |{decodedUrl}|");
               
               string tokenApi = Environment.GetEnvironmentVariable("AUTH_TOKEN_API");
               byte[] dataTokenApi = Convert.FromBase64String(tokenApi);
               string decodedToken = Encoding.UTF8.GetString(dataTokenApi);
               Console.WriteLine($"✔ TOKEN API decodificada: {decodedToken}");
                // Lanzar el backup en tarea async en paralelo (no bloquea el bucle principal)
                var backupTask = RunBackupWithDelayAsync(pgsql, decodedUrl , decodedToken);

                var keepAliveCommand = pgsql.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                var definition = new { vote = "", voter_id = "" };
                while (true)
                {
                    Thread.Sleep(100);

                    if (redisConn == null || !redisConn.IsConnected)
                    {
                        Console.WriteLine("Reconnecting Redis");
                        redisConn = OpenRedisConnection("redis");
                        redis = redisConn.GetDatabase();
                    }

                    string json = redis.ListLeftPopAsync("votes").Result;
                    if (json != null)
                    {
                        var vote = JsonConvert.DeserializeAnonymousType(json, definition);
                        Console.WriteLine($"Processing vote for '{vote.vote}' by '{vote.voter_id}'");

                        if (!pgsql.State.Equals(System.Data.ConnectionState.Open))
                        {
                            Console.WriteLine("Reconnecting DB");
                            pgsql = OpenDbConnection("Server=db;Username=postgres;Password=postgres;");
                        }
                        else
                        {
                            UpdateVote(pgsql, vote.voter_id, vote.vote);
                        }
                    }
                    else
                    {
                        keepAliveCommand.ExecuteNonQuery();
                    }
                }

         
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        // Nueva función async para el backup con delay
        private static async Task RunBackupWithDelayAsync(NpgsqlConnection pgsql, string url, string token)
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            try
            {
                var votes = new System.Collections.Generic.Dictionary<string, int>();
                await using (var cmd = pgsql.CreateCommand())
                {
                    cmd.CommandText = "SELECT vote, COUNT(id) AS count FROM votes GROUP BY vote";

                    await using var reader = await cmd.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        string vote = reader.GetString(0);
                        int count = (int)reader.GetInt64(1);
                        votes[vote] = count;
                    }
                }

                var payload = new
                {
                    environment = "prod",
                    votes = votes
                };

                string json = JsonConvert.SerializeObject(payload);
                HttpClient httpClient = new System.Net.Http.HttpClient();
 
                string[] partes = token.Split(' ');
                string palabra1 = partes[0];
                string palabra2 = partes[1];
                // Agregar header Authorization con el token
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(palabra1, palabra2);

                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                Console.WriteLine("Backup enviado. Respuesta:");
                Console.WriteLine(responseBody);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error al enviar backup: " + ex.Message);
            }
        }

        // El resto de tus métodos OpenDbConnection, OpenRedisConnection, GetIp y UpdateVote quedan igual

        private static NpgsqlConnection OpenDbConnection(string connectionString)
        {
            NpgsqlConnection connection;

            while (true)
            {
                try
                {
                    connection = new NpgsqlConnection(connectionString);
                    connection.Open();
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
                catch (DbException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
            }

            Console.Error.WriteLine("Connected to db");

            var command = connection.CreateCommand();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS votes (
                                        id VARCHAR(255) NOT NULL UNIQUE,
                                        vote VARCHAR(255) NOT NULL
                                    )";
            command.ExecuteNonQuery();

            return connection;
        }

        private static ConnectionMultiplexer OpenRedisConnection(string hostname)
        {
            var ipAddress = GetIp(hostname);
            Console.WriteLine($"Found redis at {ipAddress}");

            while (true)
            {
                try
                {
                    Console.Error.WriteLine("Connecting to redis");
                    return ConnectionMultiplexer.Connect(ipAddress);
                }
                catch (RedisConnectionException)
                {
                    Console.Error.WriteLine("Waiting for redis");
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetIp(string hostname)
            => Dns.GetHostEntryAsync(hostname)
                .Result
                .AddressList
                .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToString();

        private static void UpdateVote(NpgsqlConnection connection, string voterId, string vote)
        {
            var command = connection.CreateCommand();
            try
            {
                command.CommandText = "INSERT INTO votes (id, vote) VALUES (@id, @vote)";
                command.Parameters.AddWithValue("@id", voterId);
                command.Parameters.AddWithValue("@vote", vote);
                command.ExecuteNonQuery();
            }
            catch (DbException)
            {
                command.CommandText = "UPDATE votes SET vote = @vote WHERE id = @id";
                command.ExecuteNonQuery();
            }
            finally
            {
                command.Dispose();
            }
        }
    }
}