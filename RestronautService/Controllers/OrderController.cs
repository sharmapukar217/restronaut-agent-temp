using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace RestronautService.Controllers
{

    
    [Route("api/[controller]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Post()
        {
            using var reader = new StreamReader(Request.Body);
            var xmlContent = await reader.ReadToEndAsync();

            XDocument xmlDoc;
            try
            {
                xmlDoc = XDocument.Parse(xmlContent);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Invalid XML format", details = ex.Message });
            }

            var referenceElement = xmlDoc.XPathSelectElement("//ReferenceNumber");
            if (referenceElement == null || string.IsNullOrWhiteSpace(referenceElement.Value))
            {
                return BadRequest(new { message = "ReferenceNumber not found in XML" });
            }

            var referenceNumber = referenceElement.Value;
            string baseDir = @"C:/sc/xml/inorder";
            Directory.CreateDirectory(baseDir);

            string sourcePath = Path.Combine(baseDir, $"ORDER-{referenceNumber}.vendure");
            await System.IO.File.WriteAllTextAsync(sourcePath, xmlContent);

            string destinationPath = Path.Combine(baseDir, $"ORDER-{referenceNumber}.xml");
            System.IO.File.Move(sourcePath, destinationPath);

            return Ok();
        }

        [HttpPost("curbside")]
        public async Task<IActionResult> Curbside()
        {
            using var reader = new StreamReader(Request.Body);
            var xmlContent = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                return BadRequest(new
                {
                    errors = new[] { "Can only accept `PrintRequest` or `PopupRequest`" }
                });
            }

            XDocument xmlDoc;
            try
            {
                xmlDoc = XDocument.Parse(xmlContent);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "Invalid XML format",
                    details = ex.Message
                });
            }

            long id = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var rootElement = xmlDoc.Root?.Name.LocalName;

            if (rootElement != "PopupRequest" && rootElement != "PrintRequest")
            {
                return BadRequest(new
                {
                    errors = new[] { "Can only accept `PrintRequest` or `PopupRequest`" }
                });
            }

            string baseDir = @"C:/sc/xml/in";
            Directory.CreateDirectory(baseDir);

            if (rootElement == "PopupRequest")
            {
                var errors = new List<string>();
                var fields = new[] { "Terminal", "Line" };
                var values = new Dictionary<string, string>();

                foreach (var field in fields)
                {
                    var value = xmlDoc.XPathSelectElement($"//{field}")?.Value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        errors.Add($"{field} is required.");
                    }
                    else
                    {
                        values[field] = value;
                    }
                }

                if (errors.Any())
                {
                    return BadRequest(new { errors });
                }

                var popupXml = new XDocument(
                  new XElement("PopupRequest",
                    new XElement("Terminal", values["Terminal"]),
                    new XElement("Line", values["Line"])
                  )
                );

                string sourcePath = Path.Combine(baseDir, $"curbside_popup_req_{id}.temp");
                await System.IO.File.WriteAllTextAsync(sourcePath, popupXml.ToString());

                string destinationPath = Path.Combine(baseDir, $"curbside_popup_req_{id}.xml");
                System.IO.File.Move(sourcePath, destinationPath);

                return Ok(new
                {
                    type = rootElement,
                    message = "Popup created successfully.",
                    destinationPath
                });

            }
            else
            { // PrintRequest
                string sourcePath = Path.Combine(baseDir, $"curbside_print_req_{id}.temp");
                await System.IO.File.WriteAllTextAsync(sourcePath, xmlContent);

                string destinationPath = Path.Combine(baseDir, $"curbside_print_req_{id}.xml");
                System.IO.File.Move(sourcePath, destinationPath);

                return Ok(new
                {
                    type = rootElement,
                    message = "Print created successfully.",
                    destinationPath
                });
            }
        }
    }
}
