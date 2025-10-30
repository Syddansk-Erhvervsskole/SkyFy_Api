using Microsoft.AspNetCore.Mvc;
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


        public FileStreamResult GetStream(string remotePath)
        {
            var client = new SftpClient(_host, _port, _username, _password);
            client.Connect();

            var remoteStream = client.OpenRead(remotePath);

            return new FileStreamResult(remoteStream, "audio/mpeg")
            {
                EnableRangeProcessing = true
            };
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
    }
    public class FileCallbackResult : FileResult
    {
        private readonly Func<Stream, ActionContext, Task> _callback;

        public FileCallbackResult(string contentType, Func<Stream, ActionContext, Task> callback)
            : base(contentType)
        {
            _callback = callback;
        }

        public bool EnableRangeProcessing { get; set; }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            context.HttpContext.Response.Headers.Add("Accept-Ranges", "bytes");
            await _callback(context.HttpContext.Response.Body, context);
        }
    }


}
