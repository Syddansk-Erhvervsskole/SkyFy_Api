using Npgsql;
using System.Data;
using System.Reflection;

namespace SkyFy_Api.Services
{
    public class DbService
    {
        private IConfiguration _config;
        private string _connection;
        public DbService(IConfiguration config)
        {
            _config = config;
            _connection = 
            $"Server={_config["DataServer:Host"]};" +
            $"Port={_config["DataServer:Database:Port"]};" +
            $"User Id={_config["DataServer:Database:Username"]};" +
            $"Password={_config["DataServer:Database:Password"]};" +
            $"Database={_config["DataServer:Database:Name"]};";
        }

        public T GetEntityById<T>(long id, string table) where T : new()
        {
            return GetEntityByField<T>("ID", id, table);
        }

        public T GetEntityByField<T>(string field, object value, string table) where T : new()
        {
            using (var conn = new NpgsqlConnection(_connection))
            {
                conn.Open();

                try
                {
                    using (var cmd = new NpgsqlCommand($"SELECT * FROM \"{table}\" WHERE \"{field}\" = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", value);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return default;

                            var entity = new T();
                            var props = typeof(T).GetProperties();

                            foreach (var prop in props)
                            {
                                if (!reader.HasColumn(prop.Name) || reader[prop.Name] is DBNull)
                                    continue;

                                prop.SetValue(entity, reader[prop.Name]);
                            }

                            return entity;
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Database connection error: " + ex.Message);
                }
            }
        }

        public bool DeleteEntity(long id, string table)
        {
            using (var conn = new NpgsqlConnection(_connection))
            {
                conn.Open();
                try
                {
                    using (var cmd = new NpgsqlCommand($"DELETE FROM \"{table}\" WHERE \"ID\" = {id}", conn))
                    {

                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Database connection error: " + ex.Message);
                }
                finally
                {
                    conn.Close();
                }
            }
            return true;
        }


        public T UpdateEntity<T>(long id, T elem, string table) where T : class
        {
            using (var conn = new NpgsqlConnection(_connection))
            {
                conn.Open();
                try
                {
                    var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    var setClauses = string.Join(", ", props.Select(p => $"\"{p.Name}\" = @{p.Name}"));

                    using (var cmd = new NpgsqlCommand(
                        $"UPDATE \"{table}\" SET {setClauses} WHERE \"ID\" = {id}", conn))
                    {
                        foreach (var prop in props)
                        {
                            var value = prop.GetValue(elem) ?? DBNull.Value;
                            cmd.Parameters.AddWithValue(prop.Name, value);
                        }

                        cmd.Parameters.AddWithValue("ID", id);
                        cmd.ExecuteNonQuery();
                    }

                }
                catch (Exception ex)
                {
                    throw new Exception("Database connection error: " + ex.Message);
                }
                finally
                {
                    conn.Close();
                }
            }
            return elem;
        }
        public string CreateEntity<T>(T elem, string table) where T : class
        {
            using (var conn = new NpgsqlConnection(_connection))
            {
                conn.Open();
                try
                {
                    var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    var columns = string.Join(", ", props
                        .Where(p => p.Name != "ID") // ✅ skip ID on insert
                        .Select(p => $"\"{p.Name}\""));

                    var values = string.Join(", ", props
                        .Where(p => p.Name != "ID")
                        .Select(p => "@" + p.Name));

                    var sql = $@"
                        INSERT INTO ""{table}"" ({columns})
                        VALUES ({values})
                        RETURNING ""ID"";
                    ";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        foreach (var prop in props.Where(p => p.Name != "ID"))
                        {
                            var value = prop.GetValue(elem) ?? DBNull.Value;
                            cmd.Parameters.AddWithValue("@" + prop.Name, value);
                        }

                        var insertedId = cmd.ExecuteScalar();

                        var idProp = typeof(T).GetProperty("id");

                        conn.Close();
                        return Convert.ChangeType(insertedId, typeof(string)) as string;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Database connection error: " + ex.Message);
                }
                finally
                {
                    conn.Close();
                }
            }

        }


        public List<T> GetData<T>(string table) where T : new()
    {
        var list = new List<T>();

            using (var conn = new NpgsqlConnection(_connection))
            {
                conn.Open();

                try
                {
                    using (var cmd = new NpgsqlCommand($"SELECT * FROM {table}", conn))
                    using (var dr = cmd.ExecuteReader())
                    {
                        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                        while (dr.Read())
                        {
                            var obj = new T();

                            foreach (var prop in props)
                            {
                                if (!dr.HasColumn(prop.Name) || dr[prop.Name] == DBNull.Value)
                                    continue;

                                prop.SetValue(obj, Convert.ChangeType(dr[prop.Name], prop.PropertyType));
                            }

                            list.Add(obj);
                        }

                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Database connection error: " + ex.Message);
                }
                finally
                {
                    conn.Close();
                }
    
            }

        return list;
        }
    }

    public static class DataReaderExtensions
    {
        public static bool HasColumn(this NpgsqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
