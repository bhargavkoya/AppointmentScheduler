using Mailjet.Client;
using Mailjet.Client.Resources;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationScheduling.Utility
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var apiKey = _configuration["Mailjet:ApiKey"];
            var apiSecret = _configuration["Mailjet:ApiSecret"];
            var fromEmail = _configuration["Mailjet:FromEmail"];
            var fromName = _configuration["Mailjet:FromName"] ?? "Appointment Scheduler";

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret) || string.IsNullOrWhiteSpace(fromEmail))
            {
                // Mailjet is not configured (e.g. local development). Skip sending instead of failing the request.
                _logger.LogWarning("Mailjet is not configured; skipping email to {Recipient} with subject '{Subject}'.", email, subject);
                return;
            }

            MailjetClient client = new MailjetClient(apiKey, apiSecret);
            MailjetRequest request = new MailjetRequest
            {
                Resource = Send.Resource,
            }
          .Property(Send.FromEmail, fromEmail)
          .Property(Send.FromName, fromName)
          .Property(Send.Subject, subject)
          .Property(Send.HtmlPart, htmlMessage)
          .Property(Send.Recipients, new JArray {
                new JObject {
                 {"Email", email}
                 }
              });

            MailjetResponse response = await client.PostAsync(request);
            _logger.LogInformation("Mailjet send to {Recipient} completed with status {Status}.", email, response.StatusCode);
        }
    }
}
