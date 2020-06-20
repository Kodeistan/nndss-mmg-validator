using System;
using System.Collections.Generic;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public interface IVocabularyService
    {
        VocabularyValidationResult IsValid(string conceptCode, string conceptName, string conceptCodeSystem, string valueSetCode);
    }
}
