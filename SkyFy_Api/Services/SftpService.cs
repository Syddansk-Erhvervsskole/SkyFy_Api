using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using Renci.SshNet;
using System.IO;

namespace SkyFy_Api.Services
{
    public class SftpService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;

        public SftpService(IConfiguration config)
        {
            _host = config["DataServer:Host"];
            _port = int.Parse(config["DataServer:Sftp:Port"] ?? "22");
            _username = config["DataServer:Sftp:Username"];
            _password = config["DataServer:Sftp:Password"]; 
        }
        public async Task<string> DownloadToTempAsync(string remotePath)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(remotePath));
            using var client = new SftpClient(_host, _port, _username, _password);
            client.Connect();
            await Task.Run(() =>
            {
                using var fs = File.Create(tempFile);
                client.DownloadFile(remotePath, fs);
            });
            client.Disconnect();
            return tempFile;
        }
        public void EnsureDirectoryExists(SftpClient client, string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = "";

            for(int i = 0; i< parts.Length; i++)
            {
                if (i > 0)
                    current += "/";

                current +=  parts[i];
                if (!client.Exists(current))
                    client.CreateDirectory(current);
            }
        }
        public void CreateFolder(string remoteDirectory)
        {
            using (var client = new SftpClient(_host, _port, _username, _password))
            {
                client.Connect();

                if (!client.Exists(remoteDirectory))
                {
                    client.CreateDirectory(remoteDirectory);
                }

                client.Disconnect();
            }
        }


        public void DeleteFolder(string remoteDirectory)
        {
            using (var client = new SftpClient(_host, _port, _username, _password))
            {
                client.Connect();

                if (!client.Exists(remoteDirectory))
                    return;

                // Delete all child files & dirs first
                foreach (var file in client.ListDirectory(remoteDirectory))
                {
                    if (file.Name == "." || file.Name == "..")
                        continue;

                    var path = file.FullName;

                    if (file.IsDirectory)
                        DeleteFolder(path); // recursive delete
                    else
                        client.DeleteFile(path);
                }

                client.DeleteDirectory(remoteDirectory);
                client.Disconnect();
            }
        }


        public SftpClient GetClient()
        {
            var client = new SftpClient(_host, _port, _username, _password);
  

            return client;
        }




        public void UploadFile(IFormFile formFile, string remotePath)
        {
            using (var client = new SftpClient(_host, _port, _username, _password))
            {
                client.Connect();

                using (var fileStream = formFile.OpenReadStream())
                {
                    client.UploadFile(fileStream, remotePath);
                }

                client.Disconnect();
            }
        }

        public void UploadFile(MemoryStream stream, string remotePath)
        {
            using (var client = new SftpClient(_host, _port, _username, _password))
            {
                client.Connect();

                client.UploadFile(stream, remotePath);
                client.Disconnect();
            }
        }
    }
    public class FileCallbackResult : FileResult
    {
        private readonly Func<Stream, ActionContext, Task> _callback;

        public FileCallbackResult(string contentType, Func<Stream, ActionContext, Task> callback)
            : base(contentType)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public bool EnableRangeProcessing { get; set; } = false;

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;


            void TrySetHeader(string key, string value)
            {
                if (!response.Headers.ContainsKey(key))
                    response.Headers[key] = value;
            }


            TrySetHeader("Content-Type", ContentType.ToString());

            if (EnableRangeProcessing)
            {
                TrySetHeader("Accept-Ranges", "bytes");
            }

            await _callback(response.Body, context);
        }
    }



}
