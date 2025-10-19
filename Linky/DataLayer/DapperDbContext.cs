using System.Data;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Linky.DataLayer
{
    public class DapperDbContext : IDisposable
    {
        private string _connectionString;
        private IDbConnection _connection;
        private bool _disposed;

        public DapperDbContext(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public IDbConnection Connection
        {
            get
            {
                if (_connection == null)
                {
                    _connection = new NpgsqlConnection(_connectionString);
                }

                if (_connection.State != ConnectionState.Open)
                {
                    _connection.Open();
                }

                return _connection;
            }
        }

        public IDbConnection CreateConnection()
        {
            var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_connection != null)
                {
                    if (_connection.State == ConnectionState.Open)
                    {
                        _connection.Close();
                    }
                    _connection.Dispose();
                    _connection = null;
                }
            }

            _disposed = true;
        }

        ~DapperDbContext() //cand se termina obiectul setam functia dipose false
        {
            Dispose(false);
        }
    }
}