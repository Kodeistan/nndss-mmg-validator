using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kodeistan.Mmg.Model
{
    public sealed class ValidationResult
    {
        public List<ValidationMessage> ValidationMessages { get; set; } = new List<ValidationMessage>();

        public TimeSpan Elapsed { get; set; }

        public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

        public DateTimeOffset MessageCreated { get; set; } = DateTimeOffset.MinValue;

        public string Profile { get; set; } = string.Empty;

        public string LocalRecordId { get; set; } = string.Empty;

        public string Condition { get; set; } = string.Empty;

        public string ConditionCode { get; set; } = string.Empty;

        public string NationalReportingJurisdiction { get; set; } = string.Empty;

        public bool IsSuccess
        {
            get
            {
                return !(ValidationMessages.Any(m => m.Severity == Severity.Error));
            }
        }

        public int Errors
        {
            get
            {
                return ValidationMessages.Count(m => m.Severity == Severity.Error);
            }
        }

        public int Warnings
        {
            get
            {
                return ValidationMessages.Count(m => m.Severity == Severity.Warning);
            }
        }

        public int Others
        {
            get
            {
                return ValidationMessages.Count(m => m.Severity == Severity.Information);
            }
        }
    }
}
