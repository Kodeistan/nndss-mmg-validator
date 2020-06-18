using Kodeistan.Mmg.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kodeistan.Mmg
{
    public interface IValidator
    {
        ValidationResult ValidateMessage(string hl7v2message, MessageMappingGuide messageMappingGuide);
    }
}
