using Mailjet.Client;
using Mailjet.Client.Resources;
using Microsoft.AspNetCore.Identity.UI.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationScheduling.Utility
{
    public class EmailSender : IEmailSender
    {
        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            MailjetClient client = new MailjetClient("fd2325403193f7c4110bfc9d0801f1cf", "4d365f8c11745ae2f7a224b890a5042d")
            {
                
            };
            MailjetRequest request = new MailjetRequest
            {
                Resource = Send.Resource,
            }
          .Property(Send.FromEmail, "bhargavkoya99@gmail.com")
          .Property(Send.FromName, "Appointment Scheduler")
          .Property(Send.Subject, subject)
          .Property(Send.HtmlPart, htmlMessage)
          .Property(Send.Recipients, new JArray {
                new JObject {
                 {"Email", email}
                 }
              });
            MailjetResponse response = await client.PostAsync(request);

        }
    }
}
