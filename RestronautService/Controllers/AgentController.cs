using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace RestronautService.Controllers
{

    public class CreateFileRequest
    {
        public required string Filename { get; set; }
        public required string Xml { get; set; }
    }

    public class ExecutionSession
    {
        public required Process Process { get; init; }
        public DateTime StartTime { get; init; } = DateTime.Now;
    }

    public static class ExecutionTracker
    {
        private static readonly Dictionary<string, ExecutionSession> Sessions = new();

        public static ExecutionSession? Get(string key)
        {
            lock (Sessions)
            {
                if (Sessions.TryGetValue(key, out var s))
                {
                    if (s.Process != null && s.Process.HasExited)
                    {
                        Sessions.Remove(key);
                        return null;
                    }
                    return s;
                }
                return null;
            }
        }

        public static void Create(string key, Process process)
        {
            lock (Sessions)
            {
                Sessions[key] = new ExecutionSession { Process = process, StartTime = DateTime.Now };
            }
        }

        public static void End(string key)
        {
            lock (Sessions) Sessions.Remove(key);
        }
    }

    [ApiController]
    [Route("misc")]
    public class MiscController : ControllerBase
    {
        private readonly IConfiguration _config;
        public MiscController(IConfiguration config) => _config = config;

        [HttpGet("execute-bat")]
        public IActionResult Execute([FromQuery(Name = "run")] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "run parameter is required" });
            }

            var map = _config.GetSection("ExecutionMap").Get<Dictionary<string, string>>();
            if (map == null || !map.TryGetValue(name, out var batPath))
            {
                return StatusCode(421, new { error = "Execution name not found" });
            }

            if (!System.IO.File.Exists(batPath))
            {
                return NotFound(new { error = $"File not found: {batPath}" });
            }

            var session = ExecutionTracker.Get(name);
            if (session != null)
            {
                return StatusCode(423, new { message = $"{name} is already running since {session.StartTime:yyyy-MM-dd HH:mm:ss}" });
            }

            var process = Process.Start(new ProcessStartInfo { FileName = batPath, UseShellExecute = true, CreateNoWindow = false });

            if(process != null)
            {
                ExecutionTracker.Create(name, process);

                // Remove from tracker when done
                process.EnableRaisingEvents = true;
                process.Exited += (_, __) => ExecutionTracker.End(name);
            }

            return Ok(new { message = $"Executing {name} in a window" });
        }

        [HttpPost("create-file")]
        public IActionResult CreateFile([FromBody] CreateFileRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Filename))
            {
                return BadRequest(new { error = "Filename is required" });
            }

            var rootDir = _config.GetValue<string>("CreateFileRootDir");
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                return StatusCode(500, new { error = "CreateFileRootDir not configured in appsettings.json" });
            }

            try
            {
                if (!Directory.Exists(rootDir)) Directory.CreateDirectory(rootDir);

                var fullPath = Path.Combine(rootDir, request.Filename + ".xml");
                System.IO.File.WriteAllText(fullPath, request.Xml);

                return Ok(new { message = "File created successfully", path = fullPath });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { error = "Failed to create file", details = ex.Message });
            }
        }
    }
}
