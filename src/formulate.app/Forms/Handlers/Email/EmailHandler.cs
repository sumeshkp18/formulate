﻿namespace formulate.app.Forms.Handlers.Email
{

    // Namespaces.
    using core.Types;
    using Helpers;
    using Managers;
    using Newtonsoft.Json.Linq;
    using Resolvers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Mail;


    /// <summary>
    /// A handler that sends an email.
    /// </summary>
    public class EmailHandler : IFormHandlerType
    {

        #region Private Properties

        /// <summary>
        /// Configuration manager.
        /// </summary>
        private IConfigurationManager Config
        {
            get
            {
                return Configuration.Current.Manager;
            }
        }

        #endregion


        #region Public Properties

        /// <summary>
        /// The Angular directive that renders this handler.
        /// </summary>
        public string Directive => "formulate-email-handler";


        /// <summary>
        /// The icon shown in the picker dialog.
        /// </summary>
        public string Icon => "icon-formulate-email";


        /// <summary>
        /// The ID that uniquely identifies this handler (useful for serialization).
        /// </summary>
        public Guid TypeId => new Guid("A0C06033CB94424F9C035B10A420DB16");


        /// <summary>
        /// The label that appears when the user is choosing the handler.
        /// </summary>
        public string TypeLabel => "Email";

        #endregion


        #region Public Methods

        /// <summary>
        /// Deserializes the configuration for an email handler.
        /// </summary>
        /// <param name="configuration">
        /// The serialized configuration.
        /// </param>
        /// <returns>
        /// The deserialized configuration.
        /// </returns>
        public object DeserializeConfiguration(string configuration)
        {

            // Variables.
            var recipients = new List<string>();
            var recipientFields = new List<Guid>();
            var config = new EmailConfiguration()
            {
                Recipients = recipients,
                RecipientFields = recipientFields
            };
            var configData = JsonHelper.Deserialize<JObject>(configuration);
            var dynamicConfig = configData as dynamic;
            var properties = configData.Properties().Select(x => x.Name);
            var propertySet = new HashSet<string>(properties);


            // Get recipients.
            if (propertySet.Contains("recipients"))
            {
                foreach (var recipient in dynamicConfig.recipients)
                {
                    recipients.Add(recipient.email.Value as string);
                }
            }


            // Get email recipient fields.
            if (propertySet.Contains("recipientFields"))
            {
                foreach (var recipient in dynamicConfig.recipientFields)
                {
                    recipientFields.Add(GuidHelper.GetGuid(recipient.id.Value as string));
                }
            }


            // Get simple properties.
            if (propertySet.Contains("senderEmail"))
            {
                config.SenderEmail = dynamicConfig.senderEmail.Value as string;
            }
            if (propertySet.Contains("appendFields"))
            {
                config.AppendFields = (dynamicConfig.appendFields.Value as bool?).GetValueOrDefault();
            }
            if (propertySet.Contains("includeHiddenFields"))
            {
                config.IncludeHiddenFields = (dynamicConfig.includeHiddenFields.Value as bool?)
                    .GetValueOrDefault();
            }
            if (propertySet.Contains("appendPayload"))
            {
                config.AppendPayload = (dynamicConfig.appendPayload.Value as bool?).GetValueOrDefault();
            }
            if (propertySet.Contains("message"))
            {
                config.Message = dynamicConfig.message.Value as string;
            }
            if (propertySet.Contains("subject"))
            {
                config.Subject = dynamicConfig.subject.Value as string;
            }


            // Return the email configuration.
            return config;

        }


        /// <summary>
        /// Prepares to handle to form submission.
        /// </summary>
        /// <param name="context">
        /// The form submission context.
        /// </param>
        /// <param name="configuration">
        /// The handler configuration.
        /// </param>
        /// <remarks>
        /// In this case, no preparation is necessary.
        /// </remarks>
        public void PrepareHandleForm(FormSubmissionContext context, object configuration)
        {
        }


        /// <summary>
        /// Handles a form submission (sends an email).
        /// </summary>
        /// <param name="context">
        /// The form submission context.
        /// </param>
        /// <param name="configuration">
        /// The handler configuration.
        /// </param>
        public void HandleForm(FormSubmissionContext context, object configuration)
        {

            // Variables.
            var form = context.Form;
            var data = context.Data;
            var files = context.Files;
            var payload = context.Payload;

            // Create message.
            var config = configuration as EmailConfiguration;
            var message = new MailMessage()
            {
                IsBodyHtml = false
            };
            message.From = new MailAddress(config.SenderEmail);
            message.Subject = config.Subject;


            // Get recipients from field values.
            var emailFieldIds = new HashSet<Guid>(config.RecipientFields);
            var fieldEmails = data.Where(x => emailFieldIds.Contains(x.FieldId)).SelectMany(x => x.FieldValues)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => IsEmailInValidFormat(x));


            // Any allowed recipients (if not, abort early)?
            var rawRecipients = config.Recipients.Concat(fieldEmails);
            var allowedRecipients = FilterEmails(rawRecipients);
            if (!allowedRecipients.Any())
            {
                return;
            }
            foreach (var recipient in allowedRecipients)
            {

                // We don't want recipients to see each other, so we use BCC instead of TO.
                message.Bcc.Add(recipient);

            }


            // Append fields?
            if (config.AppendFields)
            {
                var chosenPayload = config.AppendPayload
                    ? payload
                    : payload.Take(0);
                message.Body = ConstructMessage(form, data, files, chosenPayload, config,
                    config.IncludeHiddenFields);
                foreach(var file in files)
                {
                    var dataStream = new MemoryStream(file.FileData);
                    message.Attachments.Add(new Attachment(dataStream, file.FileName));
                }
            }
            else
            {
                message.Body = config.Message;
            }


            // Send email.
            using (var client = new SmtpClient())
            {
                client.Send(message);
            }

        }

        #endregion


        #region Private Methods

        /// <summary>
        /// Constructs an email message from the form fields.
        /// </summary>
        /// <param name="form">
        /// The form being submitted.
        /// </param>
        /// <param name="data">
        /// The form fields.
        /// </param>
        /// <param name="files">
        /// The form files.
        /// </param>
        /// <param name="payload">
        /// Extra data related to the submission.
        /// </param>
        /// <param name="config">
        /// The email configuration.
        /// </param>
        /// <param name="includeHiddenFields">
        /// Include hidden fields in the message?
        /// </param>
        /// <returns>
        /// The email message.
        /// </returns>
        private string ConstructMessage(Form form, IEnumerable<FieldSubmission> data,
            IEnumerable<FileFieldSubmission> files, IEnumerable<PayloadSubmission> payload,
            EmailConfiguration config, bool includeHiddenFields)
        {

            // Variables.
            var nl = Environment.NewLine;
            var lines = new List<string>();
            var valuesById = data.GroupBy(x => x.FieldId).Select(x => new
            {
                Id = x.Key,
                Values = x.SelectMany(y => y.FieldValues).ToList()
            }).ToDictionary(x => x.Id, x => x.Values);
            var filesById = files.GroupBy(x => x.FieldId).Select(x => new
            {
                Id = x.Key,
                Filename = x.Select(y => y.FileName).FirstOrDefault()
            }).ToDictionary(x => x.Id, x => x.Filename);
            var fieldsById = form.Fields.ToDictionary(x => x.Id, x => x);


            // Payload items.
            foreach (var item in payload)
            {
                lines.Add($"{item.Name}: {item.Value}");
            }


            // Normal fields.
            foreach (var key in valuesById.Keys)
            {
                var values = valuesById[key];
                var formatted = string.Join(", ", values);
                var field = default(IFormField);
                var fieldName = "Unknown Field";
                if (fieldsById.TryGetValue(key, out field))
                {

                    // Skip this value?
                    if (!includeHiddenFields && field.IsHidden)
                    {
                        continue;
                    }


                    // Get field name and formatted value.
                    fieldName = field.Name;
                    formatted = field.FormatValue(values, FieldPresentationFormats.Email);

                }
                var line = string.Format("{0}: {1}", fieldName, formatted);
                lines.Add(line);
            }


            // File fields.
            foreach (var key in filesById.Keys)
            {
                var filename = filesById[key];
                var field = default(IFormField);
                var fieldName = "Unknown Field";
                if (fieldsById.TryGetValue(key, out field))
                {

                    // Skip this file?
                    if (!includeHiddenFields && field.IsHidden)
                    {
                        continue;
                    }


                    // Get the field name.
                    fieldName = field.Name;

                }
                var line = string.Format(@"{0}: See attachment, ""{1}""", fieldName, filename);
                lines.Add(line);
            }


            // Return message.
            return config.Message + nl + string.Join(nl, lines);

        }


        /// <summary>
        /// Filters the email addresses to only return those allowed by the whitelist.
        /// </summary>
        /// <param name="emails">
        /// The email addresses to filter.
        /// </param>
        /// <returns>
        /// The allowed email addresses.
        /// </returns>
        private IEnumerable<string> FilterEmails(IEnumerable<string> emails)
        {
            var shouldFilter = Config.EnableEmailWhitelist;
            if (shouldFilter)
            {
                var whitelist = Config.EmailWhitelist;
                var emailWhitelist = whitelist
                    .Where(x => !string.IsNullOrWhiteSpace(x.Email))
                    .Select(x => x.Email.ToLower()).Distinct();
                var emailSet = new HashSet<string>(emailWhitelist);
                var domainWhitelist = whitelist
                    .Where(x => !string.IsNullOrWhiteSpace(x.Domain))
                    .Select(x => x.Domain.ToLower()).Distinct();
                var domainSet = new HashSet<string>(domainWhitelist);
                var emailInfo = emails.Select(x => new
                {
                    Original = x,
                    Email = x.ToLower(),
                    Domain = new MailAddress(x).Host.ToLower()
                });
                return emailInfo
                    .Where(x => emailSet.Contains(x.Email) || domainSet.Contains(x.Domain))
                    .Select(x => x.Original);
            }
            else
            {
                return emails;
            }
        }


        /// <summary>
        /// Indicates whether or not the specified email address is in a valid format.
        /// </summary>
        /// <param name="email">
        /// The email address.
        /// </param>
        /// <returns>
        /// True, if the email address is in a valid format; otherwise, false.
        /// </returns>
        /// <remarks>
        /// This code is based on this Stack Overflow answer: http://stackoverflow.com/a/1374644/2052963
        /// </remarks>
        private bool IsEmailInValidFormat(string email)
        {
            try
            {
                var address = new MailAddress(email);
                return address.Address == email;
            }
            catch
            {
                return false;
            }
        }

        #endregion

    }

}