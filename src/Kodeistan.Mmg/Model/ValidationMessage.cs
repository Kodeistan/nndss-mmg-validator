using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Kodeistan.Mmg.Model
{
    /// <summary>
    /// An error or warning generated during message validation
    /// </summary>
    public class ValidationMessage
    {
        /// <summary>
        /// Gets/sets the internal ID of the message
        /// </summary>
        public Guid Id { get; private set; } = System.Guid.NewGuid();

        public string Content { get; set; } = string.Empty;

        public Severity Severity { get; set; } = Severity.Information;

        public ValidationMessageType MessageType { get; set; } = ValidationMessageType.Other;

        public string Path { get; set; } = string.Empty;

        public ValidationMessage() {}

        public ValidationMessage(Exception exception)
        {
            Severity = Severity.Error;
            Path = string.Empty;
            Content = $"{exception.GetType().ToString()} : {exception.Message}";
        }

        public ValidationMessage(Severity severity, ValidationMessageType messageType, string content, string path)
        {
            Severity = severity;
            MessageType = messageType;
            Path = path;
            Content = content;
        }
    }
}
